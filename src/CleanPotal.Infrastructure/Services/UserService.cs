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
            // 하위 메뉴 표시/숨김: key="menu:/meeting", value 1=표시 / 0=숨김
            if (c.Key.StartsWith("menu:", StringComparison.Ordinal))
            {
                var route = c.Key.Substring(5).Trim();
                if (route.Length == 0) continue;
                var set = ParseHidden(u.HiddenMenus);
                bool show = c.Value != 0;
                bool changed = show ? set.Remove(route) : set.Add(route);
                if (!changed) continue;
                u.HiddenMenus = System.Text.Json.JsonSerializer.Serialize(set.OrderBy(s => s).ToList());
                Audit(Who(u), "권한 변경", $"메뉴 {route} {(show ? "표시" : "숨김")}", byUser);
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
        var newTeam = req.NewTeam?.Trim();
        var newDept = req.NewDepartment?.Trim();
        foreach (var u in users)
        {
            if (!string.IsNullOrEmpty(newTeam)) u.TeamName = newTeam;
            if (newDept is not null) u.Department = newDept;
        }
        // 등록부 팀 단위도 함께 갱신 (이름 변경/부서 이동)
        var teamUnits = await _db.OrgUnits.Where(o => o.Kind == "team" && o.Name == team).ToListAsync();
        foreach (var o in teamUnits)
        {
            if (!string.IsNullOrEmpty(newTeam)) o.Name = newTeam;
            if (newDept is not null) o.Parent = newDept;
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
        u.HiddenMenus = NormalizeHidden(r.HiddenMenus);
    }

    // 숨긴 메뉴 JSON 배열 정규화 — 유효한 문자열 경로만 남긴다. 빈/오류 시 "[]".
    private static string NormalizeHidden(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "[]";
        try
        {
            var arr = System.Text.Json.JsonSerializer.Deserialize<List<string>>(raw);
            if (arr is null) return "[]";
            var clean = arr.Where(s => !string.IsNullOrWhiteSpace(s) && s.StartsWith("/")).Distinct().ToList();
            return System.Text.Json.JsonSerializer.Serialize(clean);
        }
        catch { return "[]"; }
    }
    private static HashSet<string> ParseHidden(string? raw)
    {
        try
        {
            var arr = System.Text.Json.JsonSerializer.Deserialize<List<string>>(string.IsNullOrWhiteSpace(raw) ? "[]" : raw);
            return arr is null ? new HashSet<string>() : new HashSet<string>(arr.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        catch { return new HashSet<string>(); }
    }

    /// <summary>부서명 일괄 변경 — 해당 부서 전원(팀 무관)의 부서를 바꾼다. 등록부도 함께 갱신.</summary>
    public async Task<int> DeptBulkAsync(string oldDept, string newDept, string byUser)
    {
        oldDept = (oldDept ?? "").Trim();
        newDept = (newDept ?? "").Trim();
        if (oldDept.Length == 0) return 0;
        var users = await _db.Users.Where(u => u.Department == oldDept).ToListAsync();
        foreach (var u in users) u.Department = newDept;
        // 등록부: 부서 단위명 + 그 부서 소속 팀들의 Parent 갱신
        var units = await _db.OrgUnits.Where(o => (o.Kind == "dept" && o.Name == oldDept) || (o.Kind == "team" && o.Parent == oldDept)).ToListAsync();
        foreach (var o in units) { if (o.Kind == "dept") o.Name = newDept; else o.Parent = newDept; }
        Audit($"부서 '{oldDept}' ({users.Count}명)", "부서 일괄 변경", $"부서명 {oldDept}→{(newDept.Length == 0 ? "(미지정)" : newDept)}", byUser);
        await _db.SaveChangesAsync();
        return users.Count;
    }

    // ── 조직도(부서·팀) 등록부 ──

    /// <summary>등록부 + 사용자 소속을 합쳐 부서→팀→인원 트리 반환.</summary>
    public async Task<IReadOnlyList<OrgDeptDto>> GetOrgAsync()
    {
        var users = await _db.Users.Where(u => !u.IsResigned).ToListAsync();
        var units = await _db.OrgUnits.OrderBy(o => o.OrderIndex).ThenBy(o => o.Id).ToListAsync();
        var regDepts = units.Where(o => o.Kind == "dept").Select(o => o.Name.Trim()).Where(s => s.Length > 0).ToHashSet();
        var regTeams = units.Where(o => o.Kind == "team")
            .Select(o => (Dept: o.Parent.Trim(), Team: o.Name.Trim())).ToList();

        // 사용자에서 유도되는 부서/팀 + 등록부 부서/팀 병합
        var deptNames = new List<string>();
        void addDept(string d) { if (!deptNames.Contains(d)) deptNames.Add(d); }
        foreach (var d in regDepts.OrderBy(x => x)) addDept(d);
        foreach (var d in users.Select(u => u.Department?.Trim() ?? "").Distinct().OrderBy(x => x)) addDept(d.Length == 0 ? "(부서 미지정)" : d);

        var result = new List<OrgDeptDto>();
        foreach (var dept in deptNames)
        {
            bool noDept = dept == "(부서 미지정)";
            var deptKey = noDept ? "" : dept;
            var teamNames = new List<string>();
            void addTeam(string tName) { if (!teamNames.Contains(tName)) teamNames.Add(tName); }
            foreach (var rt in regTeams.Where(t => t.Dept == deptKey)) addTeam(rt.Team.Length == 0 ? "(팀 미지정)" : rt.Team);
            foreach (var t in users.Where(u => (u.Department?.Trim() ?? "") == deptKey)
                         .Select(u => u.TeamName?.Trim() ?? "").Distinct().OrderBy(x => x))
                addTeam(t.Length == 0 ? "(팀 미지정)" : t);

            var teams = new List<OrgTeamDto>();
            foreach (var team in teamNames)
            {
                var teamKey = team == "(팀 미지정)" ? "" : team;
                var members = users.Where(u => (u.Department?.Trim() ?? "") == deptKey && (u.TeamName?.Trim() ?? "") == teamKey)
                    .OrderBy(u => u.RealName)
                    .Select(u => new OrgMemberDto(u.Id, u.RealName, u.JobTitle)).ToList();
                bool reg = teamKey.Length > 0 && regTeams.Any(t => t.Dept == deptKey && t.Team == teamKey);
                teams.Add(new OrgTeamDto(team, reg, members));
            }
            result.Add(new OrgDeptDto(dept, !noDept && regDepts.Contains(dept), teams));
        }
        return result;
    }

    public async Task<string?> AddOrgAsync(string kind, string name, string? parent, string byUser)
    {
        kind = (kind ?? "").Trim();
        name = (name ?? "").Trim();
        var par = (parent ?? "").Trim();
        if (name.Length == 0) return "이름을 입력하세요.";
        if (kind == "dept")
        {
            if (await _db.OrgUnits.AnyAsync(o => o.Kind == "dept" && o.Name == name) ||
                await _db.Users.AnyAsync(u => u.Department == name))
                return "이미 있는 부서입니다.";
            _db.OrgUnits.Add(new OrgUnit { Kind = "dept", Name = name });
            Audit($"부서 '{name}'", "부서 추가", "부서 등록", byUser);
        }
        else if (kind == "team")
        {
            if (await _db.OrgUnits.AnyAsync(o => o.Kind == "team" && o.Name == name && o.Parent == par) ||
                await _db.Users.AnyAsync(u => u.TeamName == name && u.Department == par))
                return "이미 있는 팀입니다.";
            _db.OrgUnits.Add(new OrgUnit { Kind = "team", Name = name, Parent = par });
            Audit($"팀 '{name}'", "팀 추가", $"부서 '{(par.Length == 0 ? "(미지정)" : par)}'에 등록", byUser);
        }
        else return "알 수 없는 종류입니다.";
        await _db.SaveChangesAsync();
        return null;
    }

    /// <summary>부서/팀 삭제 — 소속 인원이 있으면 막는다(먼저 이동/재배치 필요).</summary>
    public async Task<string?> DeleteOrgAsync(string kind, string name, string? parent, string byUser)
    {
        kind = (kind ?? "").Trim();
        name = (name ?? "").Trim();
        var par = (parent ?? "").Trim();
        if (kind == "dept")
        {
            if (await _db.Users.AnyAsync(u => u.Department == name))
                return "소속 인원이 있어 삭제할 수 없습니다. 먼저 인원을 다른 부서로 옮기세요.";
            var rows = await _db.OrgUnits.Where(o => (o.Kind == "dept" && o.Name == name) || (o.Kind == "team" && o.Parent == name)).ToListAsync();
            _db.OrgUnits.RemoveRange(rows);
            Audit($"부서 '{name}'", "부서 삭제", "부서 등록 삭제", byUser);
        }
        else if (kind == "team")
        {
            if (await _db.Users.AnyAsync(u => u.TeamName == name && u.Department == par))
                return "소속 인원이 있어 삭제할 수 없습니다. 먼저 인원을 다른 팀으로 옮기세요.";
            var rows = await _db.OrgUnits.Where(o => o.Kind == "team" && o.Name == name && o.Parent == par).ToListAsync();
            _db.OrgUnits.RemoveRange(rows);
            Audit($"팀 '{name}'", "팀 삭제", $"부서 '{(par.Length == 0 ? "(미지정)" : par)}'에서 삭제", byUser);
        }
        else return "알 수 없는 종류입니다.";
        await _db.SaveChangesAsync();
        return null;
    }
}
