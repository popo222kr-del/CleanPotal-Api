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

    // 권한 키 ↔ 라벨/게터/세터 (감사 로그·매트릭스 공용)
    private static readonly (string Key, string Label, Func<User, bool> Get, Action<User, bool> Set)[] PermMap =
    {
        ("isAdmin", "관리자", u => u.IsAdmin, (u, v) => u.IsAdmin = v),
        ("files", "파일 관리", u => u.CanManageFiles, (u, v) => u.CanManageFiles = v),
        ("notices", "공지 관리", u => u.CanManageNotices, (u, v) => u.CanManageNotices = v),
        ("vendors", "업체 관리", u => u.CanManageVendors, (u, v) => u.CanManageVendors = v),
        ("schedule", "일정/교육 관리", u => u.CanManageSchedule, (u, v) => u.CanManageSchedule = v),
        ("broken", "BROKEN 관리", u => u.CanManageBroken, (u, v) => u.CanManageBroken = v),
        ("etc", "기타 메뉴", u => u.CanAccessEtcMenu, (u, v) => u.CanAccessEtcMenu = v),
        ("shiftboard", "생산근무표", u => u.CanManageShiftBoard, (u, v) => u.CanManageShiftBoard = v),
        ("inventory", "재고 관리", u => u.CanManageInventory, (u, v) => u.CanManageInventory = v),
    };

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
        Audit(Who(u), "생성", PermSummary(u), byUser);
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

        // 변경 전 권한 스냅샷 → diff 감사 로그
        var before = PermMap.ToDictionary(p => p.Key, p => p.Get(u));
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

        var diffs = PermMap.Where(p => before[p.Key] != p.Get(u))
            .Select(p => $"{p.Label} {(p.Get(u) ? "부여" : "회수")}").ToList();
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
            var map = PermMap.FirstOrDefault(p => p.Key == c.Key);
            if (map.Key is null) continue;
            if (u.Username == "1004" && c.Key == "isAdmin" && !c.Value) continue;   // 마스터 강등 차단
            if (map.Get(u) == c.Value) continue;
            map.Set(u, c.Value);
            Audit(Who(u), "권한 변경", $"{map.Label} {(c.Value ? "부여" : "회수")}", byUser);
            applied++;
        }
        await _db.SaveChangesAsync();
        return applied;
    }

    public async Task<IReadOnlyList<UserAuditDto>> GetAuditAsync()
    {
        // ToString(포맷)은 SQLite로 번역 불가 → 메모리에서 변환
        var rows = await _db.UserAuditLogs.OrderByDescending(l => l.Id).Take(500).ToListAsync();
        return rows.Select(l => new UserAuditDto(l.Id, l.TargetUser, l.Action, l.Detail, l.ByUser, l.CreatedAt.ToString("yyyy-MM-dd HH:mm"))).ToList();
    }

    private static string PermSummary(User u)
    {
        var on = PermMap.Where(p => p.Get(u)).Select(p => p.Label).ToList();
        return on.Count > 0 ? "권한: " + string.Join(", ", on) : "권한 없음";
    }

    private static void Apply(User u, UserUpsertRequest r)
    {
        u.RealName = r.RealName;
        u.TeamName = r.TeamName;
        u.JobTitle = r.JobTitle;
        u.Email = r.Email;
        u.PhoneNumber = r.PhoneNumber;
        u.EmployeeNumber = string.IsNullOrWhiteSpace(r.EmployeeNumber) ? r.Username : r.EmployeeNumber;
        u.HireDate = r.HireDate;
        u.IsResigned = r.IsResigned;
        u.ResignDate = r.IsResigned ? r.ResignDate : "";
        u.CanManageFiles = r.CanManageFiles;
        u.CanManageNotices = r.CanManageNotices;
        u.CanManageVendors = r.CanManageVendors;
        u.CanManageSchedule = r.CanManageSchedule;
        u.CanManageBroken = r.CanManageBroken;
        u.CanAccessEtcMenu = r.CanAccessEtcMenu;
        u.CanManageShiftBoard = r.CanManageShiftBoard;
        u.CanManageInventory = r.CanManageInventory;
    }
}
