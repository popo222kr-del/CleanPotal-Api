using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Core.Security;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class UserService : IUserService
{
    private readonly CleanPotalDbContext _db;
    public UserService(CleanPotalDbContext db) => _db = db;

    // 영역 키 ↔ 라벨/게터/세터 (감사 로그·매트릭스 공용). 등급: 0 없음 / 1 조회 / 2 편집
    private static readonly (string Key, string Label, Func<User, int> Get, Action<User, int> Set)[] AreaMap =
    {
        ("schedule", "일정관리", u => u.AccessSchedule, (u, v) => u.AccessSchedule = v),
        ("roster", "근무표", u => u.AccessRoster, (u, v) => u.AccessRoster = v),
        ("handover", "현장 인수인계", u => u.AccessHandover, (u, v) => u.AccessHandover = v),
        ("field", "현장 점검", u => u.AccessField, (u, v) => u.AccessField = v),
        ("office", "OFFICE 업무", u => u.AccessOffice, (u, v) => u.AccessOffice = v),
    };
    private static string LevelName(int v) => v switch { 0 => "없음", 1 => "조회", _ => "편집" };
    private static int Clamp(int v) => Math.Clamp(v, 0, 2);

    private void Audit(string target, string action, string detail, string byUser) =>
        _db.UserAuditLogs.Add(new UserAuditLog { TargetUser = target, Action = action, Detail = detail, ByUser = byUser, CreatedAt = DateTime.Now });

    private static string Who(User u) => $"{u.RealName}({u.Username})";

    public async Task<IReadOnlyList<UserDto>> GetAllAsync(bool includeResigned)
    {
        var q = _db.Users.AsQueryable();
        if (!includeResigned) q = q.Where(u => !u.IsResigned);
        var users = await q.OrderBy(u => u.TeamName).ThenBy(u => u.RealName).ToListAsync();
        return users.Select(AuthService.ToDto).ToList();
    }

    public async Task<UserDto?> GetAsync(int id)
    {
        var u = await _db.Users.FindAsync(id);
        return u is null ? null : AuthService.ToDto(u);
    }

    public async Task<UserDto> CreateAsync(UserUpsertRequest req, string byUser)
    {
        if (await _db.Users.AnyAsync(u => u.Username == req.Username))
            throw new InvalidOperationException("이미 존재하는 아이디입니다.");

        var u = new User { Username = req.Username };
        Apply(u, req);
        u.IsAdmin = req.IsAdmin;
        u.PasswordHash = PasswordHasher.Hash(string.IsNullOrEmpty(req.Password) ? "1234" : req.Password);
        _db.Users.Add(u);
        Audit(Who(u), "생성", AccessSummary(u), byUser);
        await _db.SaveChangesAsync();
        return AuthService.ToDto(u);
    }

    public async Task<UserDto?> UpdateAsync(int id, UserUpsertRequest req, string byUser)
    {
        var u = await _db.Users.FindAsync(id);
        if (u is null) return null;

        // 1004(최고관리자) 아이디 변경 차단
        if (u.Username == "1004" && req.Username != "1004")
            throw new InvalidOperationException("최고 관리자(1004)의 아이디는 변경할 수 없습니다.");

        if (req.Username != u.Username && await _db.Users.AnyAsync(x => x.Username == req.Username))
            throw new InvalidOperationException("이미 사용 중인 아이디입니다.");

        // 변경 전 스냅샷 → diff 감사 로그
        var before = AreaMap.ToDictionary(p => p.Key, p => p.Get(u));
        bool beforeAdmin = u.IsAdmin;
        bool wasResigned = u.IsResigned;

        u.Username = req.Username;
        Apply(u, req);
        u.IsAdmin = req.IsAdmin;
        if (!string.IsNullOrEmpty(req.Password))
        {
            u.PasswordHash = PasswordHasher.Hash(req.Password);
            Audit(Who(u), "비밀번호 변경", "관리자에 의한 재설정", byUser);
        }
        if (u.Username == "1004") u.IsAdmin = true;   // 최고관리자 권한 고정

        var diffs = AreaMap.Where(p => before[p.Key] != p.Get(u))
            .Select(p => $"{p.Label} {LevelName(before[p.Key])}→{LevelName(p.Get(u))}").ToList();
        if (beforeAdmin != u.IsAdmin) diffs.Insert(0, $"관리자 {(u.IsAdmin ? "부여" : "회수")}");
        if (diffs.Count > 0) Audit(Who(u), "권한 변경", string.Join(", ", diffs), byUser);
        if (!wasResigned && u.IsResigned) Audit(Who(u), "퇴사 처리", u.ResignDate, byUser);
        else if (wasResigned && !u.IsResigned) Audit(Who(u), "복직 처리", "", byUser);

        await _db.SaveChangesAsync();
        return AuthService.ToDto(u);
    }

    public async Task<bool> DeleteAsync(int id, string byUser)
    {
        var u = await _db.Users.FindAsync(id);
        if (u is null) return false;
        if (u.Username == "1004")
            throw new InvalidOperationException("최고 관리자(1004) 계정은 삭제할 수 없습니다.");
        Audit(Who(u), "삭제", "", byUser);
        _db.Users.Remove(u);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<int> BulkPermAsync(IReadOnlyList<UserPermChange> changes, string byUser)
    {
        if (changes.Count == 0) return 0;
        var ids = changes.Select(c => c.Id).Distinct().ToList();
        var users = await _db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
        int applied = 0;
        foreach (var c in changes)
        {
            if (!users.TryGetValue(c.Id, out var u)) continue;
            if (c.Key == "isAdmin")
            {
                bool v = c.Value != 0;
                if (u.Username == "1004" && !v) continue;   // 마스터 강등 차단
                if (u.IsAdmin == v) continue;
                u.IsAdmin = v;
                Audit(Who(u), "권한 변경", $"관리자 {(v ? "부여" : "회수")}", byUser);
                applied++;
                continue;
            }
            var map = AreaMap.FirstOrDefault(p => p.Key == c.Key);
            if (map.Key is null) continue;
            var nv = Clamp(c.Value);
            var old = map.Get(u);
            if (old == nv) continue;
            map.Set(u, nv);
            Audit(Who(u), "권한 변경", $"{map.Label} {LevelName(old)}→{LevelName(nv)}", byUser);
            applied++;
        }
        await _db.SaveChangesAsync();
        return applied;
    }

    public async Task<int> TeamBulkAsync(TeamBulkRequest req, string byUser)
    {
        var team = (req.Team ?? "").Trim();
        if (team.Length == 0) return 0;
        var users = await _db.Users.Where(u => u.TeamName == team).ToListAsync();
        if (users.Count == 0) return 0;
        var newTeam = req.NewTeam?.Trim();
        var newDept = req.NewDepartment?.Trim();
        foreach (var u in users)
        {
            if (!string.IsNullOrEmpty(newTeam)) u.TeamName = newTeam;
            if (newDept is not null) u.Department = newDept;
        }
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(newTeam) && newTeam != team) parts.Add($"팀명 {team}→{newTeam}");
        if (newDept is not null) parts.Add($"부서 지정 '{newDept}'");
        Audit($"팀 '{team}' ({users.Count}명)", "팀 일괄 변경", string.Join(", ", parts), byUser);
        await _db.SaveChangesAsync();
        return users.Count;
    }

    public async Task<IReadOnlyList<UserAuditDto>> GetAuditAsync()
    {
        // ToString(포맷)은 SQLite로 번역 불가 → 메모리에서 변환
        var rows = await _db.UserAuditLogs.OrderByDescending(l => l.Id).Take(500).ToListAsync();
        return rows.Select(l => new UserAuditDto(l.Id, l.TargetUser, l.Action, l.Detail, l.ByUser, l.CreatedAt.ToString("yyyy-MM-dd HH:mm"))).ToList();
    }

    private static string AccessSummary(User u)
    {
        var parts = AreaMap.Select(p => $"{p.Label} {LevelName(p.Get(u))}").ToList();
        if (u.IsAdmin) parts.Insert(0, "관리자");
        return string.Join(", ", parts);
    }

    private static void Apply(User u, UserUpsertRequest r)
    {
        u.RealName = r.RealName;
        u.Department = r.Department ?? "";
        u.TeamName = r.TeamName;
        u.JobTitle = r.JobTitle;
        u.Email = r.Email;
        u.PhoneNumber = r.PhoneNumber;
        u.EmployeeNumber = string.IsNullOrWhiteSpace(r.EmployeeNumber) ? r.Username : r.EmployeeNumber;
        u.HireDate = r.HireDate;
        u.IsResigned = r.IsResigned;
        u.ResignDate = r.IsResigned ? r.ResignDate : "";
        u.AccessSchedule = Clamp(r.AccessSchedule);
        u.AccessRoster = Clamp(r.AccessRoster);
        u.AccessHandover = Clamp(r.AccessHandover);
        u.AccessField = Clamp(r.AccessField);
        u.AccessOffice = Clamp(r.AccessOffice);
    }
}
