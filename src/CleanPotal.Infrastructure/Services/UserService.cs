using CleanPotal.Core;
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
        ("mes", "MES (생산관리)", u => u.AccessMes, (u, v) => u.AccessMes = v),
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
            throw new BusinessRuleException("이미 존재하는 아이디입니다.");

        // 공통 기본 비밀번호(1234)를 자동 부여하지 않는다 — 모든 신규 계정이 같은 비밀번호로
        // 열리는 상태를 막기 위해, 생성 시에는 비밀번호를 반드시 입력받는다.
        if (string.IsNullOrWhiteSpace(req.Password))
            throw new BusinessRuleException("새 계정의 비밀번호를 입력하세요.");
        if (req.Password.Length < 4)
            throw new BusinessRuleException("비밀번호는 4자 이상이어야 합니다.");

        await EnsureNameFreeAsync(null, req.RealName);

        var u = new User { Username = req.Username };
        Apply(u, req);
        u.IsAdmin = req.IsAdmin;
        u.PasswordHash = PasswordHasher.Hash(req.Password);
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
            throw new BusinessRuleException("최고 관리자(1004)의 아이디는 변경할 수 없습니다.");
        // 반대로 다른 계정을 1004 로 바꾸면 그 계정이 저장할 때마다 관리자로 고정되고 지울 수도 없게 된다.
        if (u.Username != "1004" && req.Username == "1004")
            throw new BusinessRuleException("1004 는 최고 관리자 전용 아이디입니다.");

        if (req.Username != u.Username && await _db.Users.AnyAsync(x => x.Username == req.Username))
            throw new BusinessRuleException("이미 사용 중인 아이디입니다.");
        if ((req.RealName ?? "").Trim() != (u.RealName ?? "").Trim())
            await EnsureNameFreeAsync(u.Id, req.RealName);

        // 변경 전 스냅샷 → diff 감사 로그
        var before = AreaMap.ToDictionary(p => p.Key, p => p.Get(u));
        bool beforeAdmin = u.IsAdmin;
        bool wasResigned = u.IsResigned;
        var beforeMes = MesPermissionCodes.Parse(u.MesPermissions);
        var oldRealName = u.RealName;

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

        // MES 세부 권한은 등급과 다른 축이라 위 diff 에 잡히지 않는다. 공정 무효화처럼 무거운 권한이
        // 여기 있어서, 누가 언제 켜 줬는지 남지 않으면 나중에 확인할 방법이 없다.
        var afterMes = MesPermissionCodes.Parse(u.MesPermissions);
        var mesChanges = MesPermissionCodes.All
            .Where(code => beforeMes.Contains(code) != afterMes.Contains(code))
            .Select(code => $"{(afterMes.Contains(code) ? "+" : "-")}{MesPermissionCodes.Label(code)}")
            .ToList();
        if (mesChanges.Count > 0) diffs.Add($"MES 세부 권한 {string.Join(" ", mesChanges)}");
        if (diffs.Count > 0) Audit(Who(u), "권한 변경", string.Join(", ", diffs), byUser);
        if (!wasResigned && u.IsResigned) Audit(Who(u), "퇴사 처리", u.ResignDate, byUser);
        else if (wasResigned && !u.IsResigned) Audit(Who(u), "복직 처리", "", byUser);

        var moved = await CarryNameChangeAsync(u.Id, oldRealName, u.RealName);
        if (moved is { } m) Audit(Who(u), "이름 변경", $"{oldRealName}→{u.RealName} (근무표 {m.Shifts}건 · 교육 {m.Educations}건 함께 변경)", byUser);

        await _db.SaveChangesAsync();
        return AuthService.ToDto(u);
    }

    /// <summary>
    /// 근무표·교육·요청사항은 사람을 이름(문자열)으로 찾는다. 이름이 겹치면 두 사람의 기록이 한 줄로 합쳐지고,
    /// 퇴사자와 같은 이름으로 새 계정을 만들면 옛 기록을 물려받는다(docs/known-issues.md §1).
    /// 이름 대신 ID 로 잇도록 바꾸기 전까지는 겹치는 이름을 받지 않는다 — 기존에 이미 겹친 이름은 그대로 둔다.
    /// </summary>
    private async Task EnsureNameFreeAsync(int? selfId, string? realName)
    {
        var name = (realName ?? "").Trim();
        if (name.Length == 0) return;
        var other = await _db.Users.AsNoTracking()
            .Where(x => x.Id != selfId && x.RealName.Trim() == name)
            .Select(x => new { x.Username, x.IsResigned })
            .FirstOrDefaultAsync();
        if (other is null) return;
        throw new BusinessRuleException(other.IsResigned
            ? $"'{name}' 은(는) 퇴사한 계정({other.Username})이 쓰던 이름입니다. 같은 사람이면 그 계정을 복직 처리하고, 다른 사람이면 '{name}(B)' 처럼 구분해 주세요."
            : $"'{name}' 은(는) 이미 다른 계정({other.Username})이 쓰는 이름입니다. 근무표·교육이 이름으로 이어져 기록이 섞이므로 '{name}(B)' 처럼 구분해 주세요.");
    }

    /// <summary>
    /// 근무표·교육 일정은 사람을 이름(문자열)으로 가리킨다. 이름을 바꾸면 과거 근무표·교육이 옛 이름에 남아
    /// 아무도 보지 못하는 줄이 됐다. 옛 이름을 쓰는 다른 계정이 없고 새 이름을 쓰는 다른 계정도 없을 때만
    /// 따라 바꾼다 — 동명이인이 있으면 누구의 줄인지 알 수 없어 건드리지 않는다.
    /// </summary>
    private async Task<(int Shifts, int Educations)?> CarryNameChangeAsync(int userId, string? oldName, string? newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (oldName.Length == 0 || newName.Length == 0 || oldName == newName) return null;
        if (await _db.Users.AnyAsync(x => x.Id != userId && (x.RealName == oldName || x.RealName == newName)))
            return null;

        var shifts = await _db.ShiftSchedules.Where(s => s.MemberName == oldName).ToListAsync();
        // 새 이름으로 이미 적힌 날이 있으면((이름, 날짜) 고유) 그 날은 옮기지 않는다.
        var taken = (await _db.ShiftSchedules.Where(s => s.MemberName == newName).Select(s => s.TargetDate).ToListAsync()).ToHashSet();
        var movedShifts = 0;
        foreach (var s in shifts.Where(s => !taken.Contains(s.TargetDate)))
        {
            s.MemberName = newName;
            movedShifts++;
        }

        var edus = await _db.EducationPlans.Where(e => e.MemberName == oldName).ToListAsync();
        foreach (var e in edus) e.MemberName = newName;

        return movedShifts + edus.Count > 0 ? (movedShifts, edus.Count) : null;
    }

    public async Task<bool> DeleteAsync(int id, string byUser)
    {
        var u = await _db.Users.FindAsync(id);
        if (u is null) return false;
        if (u.Username == "1004")
            throw new BusinessRuleException("최고 관리자(1004) 계정은 삭제할 수 없습니다.");
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

        // Office 처럼 같은 이름 팀이 여러 부서에 있을 수 있다. 부서를 주면 그 부서의 팀만 바꾼다.
        // (비우면 예전처럼 같은 이름 팀 전체가 대상 — 기존 호출부 동작 보존)
        var curDept = req.Department?.Trim();

        var userQuery = _db.Users.Where(u => u.TeamName == team);
        if (curDept is not null) userQuery = userQuery.Where(u => u.Department == curDept);
        var users = await userQuery.ToListAsync();

        var newTeam = req.NewTeam?.Trim();
        var newDept = req.NewDepartment?.Trim();
        foreach (var u in users)
        {
            if (!string.IsNullOrEmpty(newTeam)) u.TeamName = newTeam;
            if (newDept is not null) u.Department = newDept;
        }
        // 등록부 팀 단위도 함께 갱신 (이름 변경/부서 이동)
        var unitQuery = _db.OrgUnits.Where(o => o.Kind == "team" && o.Name == team);
        if (curDept is not null) unitQuery = unitQuery.Where(o => o.Parent == curDept);
        var teamUnits = await unitQuery.ToListAsync();
        foreach (var o in teamUnits)
        {
            if (!string.IsNullOrEmpty(newTeam)) o.Name = newTeam;
            if (newDept is not null) o.Parent = newDept;
        }

        // 이미 찍어 둔 근무표 행도 새 팀 이름으로 따라가게 한다.
        // 근무표는 팀 이름을 문자열로 들고 있어서, 이름만 바꾸면 과거 근무가 옛 이름에 묶여
        // 달력·오늘 현황의 주/야 팀 표시가 어긋난다.
        // 부서를 지정한 경우에는 그 부서 인원의 행만 바꾼다 — 다른 부서의 같은 이름 팀을 건드리면 안 된다.
        var renamedRows = 0;
        if (!string.IsNullOrEmpty(newTeam) && newTeam != team)
        {
            var rowQuery = _db.ShiftSchedules.Where(s => s.TeamGroup == team);
            if (curDept is not null)
            {
                var names = users.Select(u => u.RealName).ToList();
                rowQuery = rowQuery.Where(s => names.Contains(s.MemberName));
            }
            var rows = await rowQuery.ToListAsync();
            foreach (var r in rows) r.TeamGroup = newTeam;
            renamedRows = rows.Count;
        }
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(newTeam) && newTeam != team)
            parts.Add($"팀명 {team}→{newTeam}" + (renamedRows > 0 ? $" (근무표 {renamedRows}건 갱신)" : ""));
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
        var mes = MesPermissionCodes.Parse(u.MesPermissions);
        if (mes.Count > 0)
            parts.Add($"MES 세부 권한 {string.Join(" ", MesPermissionCodes.All.Where(mes.Contains).Select(MesPermissionCodes.Label))}");
        return string.Join(", ", parts);
    }

    private static void Apply(User u, UserUpsertRequest r)
    {
        u.RealName = (r.RealName ?? "").Trim();
        u.Department = r.Department ?? "";
        u.TeamName = r.TeamName;
        u.Rank = (r.Rank ?? "").Trim();
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
        u.AccessMes = Clamp(r.AccessMes);
        u.MesPermissions = MesPermissionCodes.Normalize(r.MesPermissions);
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

    /// <summary>등록부 + 사용자 소속을 합쳐 본부→부서→팀→인원 트리 반환.</summary>
    public async Task<OrgTreeDto> GetOrgAsync()
    {
        var users = await _db.Users.Where(u => !u.IsResigned).ToListAsync();
        var units = await _db.OrgUnits.OrderBy(o => o.OrderIndex).ThenBy(o => o.Id).ToListAsync();
        var deptUnits = units.Where(o => o.Kind == "dept" && o.Name.Trim().Length > 0)
            .GroupBy(o => o.Name.Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var regDepts = deptUnits.Keys.ToHashSet();
        var regTeams = units.Where(o => o.Kind == "team")
            .Select(o => (Dept: o.Parent.Trim(), Team: o.Name.Trim(), o.ShiftGroup, o.LegacyNames,
                          o.IsProduction, o.ShowOnDashboard)).ToList();

        // 본부(사업본부). 부서 행의 Parent 가 본부명을 가리킨다 — 팀만 쓰던 칸이라 새 컬럼이 필요 없다.
        var divisions = units.Where(o => o.Kind == "division" && o.Name.Trim().Length > 0)
            .GroupBy(o => o.Name.Trim(), StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(o => o.OrderIndex).ThenBy(o => o.Name, StringComparer.Ordinal)
            .Select(o => o.Name.Trim())
            .ToList();
        var divisionSet = divisions.ToHashSet(StringComparer.Ordinal);

        // 마스터(관리자) 계정뿐인 부서는 실제 조직이 아니라 로그인용 버킷이므로 이 화면에서 뺀다.
        // 인원이 아예 없는 부서는(향후 배치를 위해 미리 등록해 둔 경우) 그대로 보여준다.
        var adminOnlyDepts = users
            .GroupBy(u => (u.Department ?? "").Trim(), StringComparer.Ordinal)
            .Where(g => g.All(u => u.IsAdmin))
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        // 사용자에서 유도되는 부서/팀 + 등록부 부서/팀 병합
        var deptNames = new List<string>();
        void addDept(string d) { if (!deptNames.Contains(d)) deptNames.Add(d); }
        foreach (var d in regDepts.OrderBy(x => x)) { if (!adminOnlyDepts.Contains(d)) addDept(d); }
        foreach (var d in users.Select(u => u.Department?.Trim() ?? "").Distinct().OrderBy(x => x))
        {
            if (adminOnlyDepts.Contains(d)) continue;
            addDept(d.Length == 0 ? "(부서 미지정)" : d);
        }

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
                    .Select(u => new OrgMemberDto(u.Id, u.RealName, u.Rank, u.JobTitle)).ToList();
                var unit = regTeams.FirstOrDefault(t => t.Dept == deptKey && t.Team == teamKey);
                bool reg = teamKey.Length > 0 && unit.Team is not null;
                // 교대조가 지정돼 있으면 생산팀으로 본다 — 칸이 생기기 전 데이터와 화면 표시를 맞춘다
                // 등록되지 않은 팀은 튜플 기본값(false)이 나오므로, 등록된 팀만 저장된 값을 쓴다.
                // 기본은 '표시' 다 — 칸이 생겼다고 화면에서 사라지면 안 된다.
                teams.Add(new OrgTeamDto(team, reg, members, unit.ShiftGroup, unit.LegacyNames ?? "",
                                         unit.IsProduction || unit.ShiftGroup > 0,
                                         !reg || unit.ShowOnDashboard));
            }
            deptUnits.TryGetValue(dept, out var du);
            // 지워진 본부를 가리키고 있으면 '본부 미지정' 으로 본다
            var div = (du?.Parent ?? "").Trim();
            if (!divisionSet.Contains(div)) div = "";
            result.Add(new OrgDeptDto(
                dept, !noDept && regDepts.Contains(dept), teams,
                du?.Id ?? 0,
                du is null ? "" : DeptPalette.Resolve(du.Color, du.Id),
                du is null ? "" : DeptPalette.ResolveShortName(du.ShortName, du.Name),
                div,
                du?.ShowOnDashboard ?? true, du?.ShowOnCalendar ?? true));
        }
        return new OrgTreeDto(divisions, result);
    }

    /// <summary>부서 이름 → 본부 이름. 등록부에 없는 부서는 빈 문자열.</summary>
    private async Task<Dictionary<string, string>> DeptDivisionMapAsync()
    {
        var rows = await _db.OrgUnits.Where(o => o.Kind == "dept")
            .Select(o => new { o.Name, o.Parent }).ToListAsync();
        return rows
            .GroupBy(x => (x.Name ?? "").Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.First().Parent ?? "").Trim(), StringComparer.Ordinal);
    }

    /// <summary>부서를 본부에 연결한다. division 이 비면 '본부 미지정' 으로 돌린다.</summary>
    public async Task<string?> SetDeptDivisionAsync(string dept, string division, string byUser)
    {
        dept = (dept ?? "").Trim();
        division = (division ?? "").Trim();
        if (dept.Length == 0) return "부서를 지정하세요.";
        if (division.Length > 0 && !await _db.OrgUnits.AnyAsync(o => o.Kind == "division" && o.Name == division))
            return "등록되지 않은 본부입니다. 먼저 본부를 추가하세요.";

        // 조직도의 부서는 대부분 '자동'(사용자 소속에서 유도)이라 등록부에 행이 없다.
        // 본부는 등록부에 저장하므로, 소속 인원이 있을 때만 행을 만들어 준다.
        var units = await _db.OrgUnits.Where(o => o.Kind == "dept" && o.Name == dept).ToListAsync();
        if (units.Count == 0)
        {
            if (!await _db.Users.AnyAsync(u => u.Department == dept))
                return "소속 인원이 없는 부서입니다. 먼저 부서를 등록하세요.";
            var created = new OrgUnit { Kind = "dept", Name = dept };
            _db.OrgUnits.Add(created);
            units = new List<OrgUnit> { created };
        }

        foreach (var o in units) o.Parent = division;
        Audit($"부서 '{dept}'", "본부 지정", division.Length == 0 ? "본부 미지정" : $"본부 '{division}'", byUser);
        await _db.SaveChangesAsync();
        return null;
    }

    /// <summary>본부 이름 변경. 그 본부를 가리키는 부서들의 Parent 도 함께 바꾼다.</summary>
    public async Task<string?> RenameDivisionAsync(string oldName, string newName, string byUser)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (oldName.Length == 0 || newName.Length == 0) return "이름을 입력하세요.";
        if (oldName == newName) return null;
        if (await _db.OrgUnits.AnyAsync(o => o.Kind == "division" && o.Name == newName))
            return "이미 있는 본부입니다.";

        var rows = await _db.OrgUnits.Where(o => o.Kind == "division" && o.Name == oldName).ToListAsync();
        if (rows.Count == 0) return "본부를 찾을 수 없습니다.";
        foreach (var o in rows) o.Name = newName;

        var depts = await _db.OrgUnits.Where(o => o.Kind == "dept" && o.Parent == oldName).ToListAsync();
        foreach (var o in depts) o.Parent = newName;

        Audit($"본부 '{oldName}'", "본부명 변경", $"{oldName}→{newName} (부서 {depts.Count}개 갱신)", byUser);
        await _db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> AddOrgAsync(string kind, string name, string? parent, string byUser)
    {
        kind = (kind ?? "").Trim();
        name = (name ?? "").Trim();
        var par = (parent ?? "").Trim();
        if (name.Length == 0) return "이름을 입력하세요.";
        if (kind == "division")
        {
            if (await _db.OrgUnits.AnyAsync(o => o.Kind == "division" && o.Name == name))
                return "이미 있는 본부입니다.";
            _db.OrgUnits.Add(new OrgUnit { Kind = "division", Name = name });
            Audit($"본부 '{name}'", "본부 추가", "본부 등록", byUser);
        }
        else if (kind == "dept")
        {
            if (await _db.OrgUnits.AnyAsync(o => o.Kind == "dept" && o.Name == name) ||
                await _db.Users.AnyAsync(u => u.Department == name))
                return "이미 있는 부서입니다.";
            // par 를 주면 그 본부 소속으로 바로 만든다
            if (par.Length > 0 && !await _db.OrgUnits.AnyAsync(o => o.Kind == "division" && o.Name == par))
                return "등록되지 않은 본부입니다. 먼저 본부를 추가하세요.";
            _db.OrgUnits.Add(new OrgUnit { Kind = "dept", Name = name, Parent = par });
            Audit($"부서 '{name}'", "부서 추가", par.Length == 0 ? "부서 등록" : $"본부 '{par}'에 등록", byUser);
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
        if (kind == "division")
        {
            if (await _db.OrgUnits.AnyAsync(o => o.Kind == "dept" && o.Parent == name))
                return "소속 부서가 있어 삭제할 수 없습니다. 먼저 부서를 다른 본부로 옮기세요.";
            var divRows = await _db.OrgUnits.Where(o => o.Kind == "division" && o.Name == name).ToListAsync();
            _db.OrgUnits.RemoveRange(divRows);
            Audit($"본부 '{name}'", "본부 삭제", "본부 등록 삭제", byUser);
        }
        else if (kind == "dept")
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

    /// <summary>
    /// 이 팀이 WPF 에서 쓰던 이름들을 기록한다(쉼표 구분).
    /// WPF 와 병행하는 동안 임포트가 옛 이름을 현재 이름으로 바꿔 넣는 데 쓴다.
    /// </summary>

    /// <summary>
    /// 설정을 붙일 팀 등록부 행을 찾는다. 없으면 만든다.
    ///
    /// 조직도 화면의 팀은 대부분 '자동'(사용자 소속에서 유도된 것)이라 등록부에 행이 없다.
    /// 교대 조나 WPF 옛 이름은 등록부에 저장하므로, 여기서 행을 만들어 주지 않으면
    /// "먼저 팀을 등록하세요" 라는 막다른 길이 된다. 실제로 그 팀에 소속된 사람이 있을 때만
    /// 만들어, 오타로 엉뚱한 팀이 생기는 것은 막는다.
    /// </summary>
    private async Task<List<OrgUnit>?> EnsureTeamUnitsAsync(string name, string? parent)
    {
        // Office 처럼 같은 이름 팀이 여러 부서에 있을 수 있으므로, 부서를 받은 호출은
        // 그 부서의 등록부 행만 건드린다. parent 가 null 인 호출은 예전처럼 이름만 보고 찾는다.
        var par = parent?.Trim();

        var unitQuery = _db.OrgUnits.Where(o => o.Kind == "team" && o.Name == name);
        if (par is not null) unitQuery = unitQuery.Where(o => o.Parent == par);
        var units = await unitQuery.ToListAsync();
        if (units.Count > 0) return units;

        // 아래 검사는 '오타로 엉뚱한 팀이 생기는 것'만 막으면 되므로 이름만 본다.
        // 부서까지 맞춰 보면, 소속 부서를 비워 둔 팀은 설정 자체가 막혀 막다른 길이 된다.
        // 다른 부서의 같은 이름 팀을 건드리지 않는 것은 위의 등록부 조회와 아래 Parent 가 보장한다.
        if (!await _db.Users.AnyAsync(u => u.TeamName == name)) return null;

        var created = new OrgUnit { Kind = "team", Name = name, Parent = par ?? "" };
        _db.OrgUnits.Add(created);
        return new List<OrgUnit> { created };
    }

    /// <summary>
    /// 대시보드 근무 현황 · 일정 달력에 띄울지 정한다. 보내지 않은 값은 그대로 둔다.
    ///
    /// 조직을 지우는 것과는 다른 이야기다 — 인원도 과거 일정도 그대로 살아 있고, 목록에서만 빠진다.
    /// </summary>
    public async Task<string?> SetOrgVisibilityAsync(
        string kind, string name, string? parent, bool? showOnDashboard, bool? showOnCalendar, string byUser)
    {
        kind = (kind ?? "").Trim().ToLowerInvariant();
        name = (name ?? "").Trim();
        if (name.Length == 0) return "대상을 지정하세요.";
        if (showOnDashboard is null && showOnCalendar is null) return null;   // 바꿀 것이 없다

        List<OrgUnit> units;
        if (kind == "team")
        {
            units = await EnsureTeamUnitsAsync(name, parent)
                ?? new List<OrgUnit>();
            if (units.Count == 0) return "소속 인원이 없는 팀입니다. 먼저 팀원의 소속팀을 지정하세요.";
        }
        else
        {
            units = await _db.OrgUnits.Where(o => o.Kind == "dept" && o.Name == name).ToListAsync();
            if (units.Count == 0) return "등록되지 않은 부서입니다.";
        }

        var changes = new List<string>();
        foreach (var o in units)
        {
            if (showOnDashboard is { } d && o.ShowOnDashboard != d)
            {
                o.ShowOnDashboard = d;
                changes.Add(d ? "대시보드 표시" : "대시보드 숨김");
            }
            // 달력 목록은 부서 단위로만 고른다 — 팀은 달력에 줄이 없다.
            if (kind != "team" && showOnCalendar is { } c && o.ShowOnCalendar != c)
            {
                o.ShowOnCalendar = c;
                changes.Add(c ? "달력 표시" : "달력 숨김");
            }
        }

        if (changes.Count == 0) return null;
        Audit($"{(kind == "team" ? "팀" : "부서")} '{name}'", "표시 설정", string.Join(", ", changes.Distinct()), byUser);
        await _db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> SetOrgLegacyNamesAsync(string name, string legacyNames, string byUser, string? parent = null)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return "팀을 지정하세요.";

        var units = await EnsureTeamUnitsAsync(name, parent);
        if (units is null) return "소속 인원이 없는 팀입니다. 먼저 팀원의 소속팀을 지정하세요.";

        var cleaned = string.Join(", ", (legacyNames ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0 && !string.Equals(x, name, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        if (cleaned.Length > 400) return "옛 이름 목록이 너무 깁니다.";

        foreach (var o in units) o.LegacyNames = cleaned;
        Audit($"팀 '{name}'", "WPF 옛 이름 지정", cleaned.Length == 0 ? "(비움)" : cleaned, byUser);
        await _db.SaveChangesAsync();
        return null;
    }

    /// <summary>팀의 생산팀 여부. 근무표에 나올지, 통계에서 생산직으로 셀지를 가른다(교대조와 별개 축).</summary>
    public async Task<string?> SetOrgProductionAsync(string name, bool isProduction, string byUser, string? parent = null)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return "팀을 지정하세요.";

        var units = await EnsureTeamUnitsAsync(name, parent);
        if (units is null) return "소속 인원이 없는 팀입니다. 먼저 팀원의 소속팀을 지정하세요.";

        // 교대조가 지정된 팀은 정의상 생산팀이라 해제할 수 없다. 먼저 교대조를 풀어야 한다.
        if (!isProduction && units.Any(o => o.ShiftGroup > 0))
            return "교대조가 지정된 팀은 생산팀에서 뺄 수 없습니다. 먼저 교대 조를 '교대 없음'으로 바꾸세요.";

        foreach (var o in units) o.IsProduction = isProduction;
        Audit($"팀 '{name}'", "생산팀 지정", isProduction ? "생산팀" : "생산팀 아님", byUser);
        await _db.SaveChangesAsync();
        return null;
    }

    /// <summary>
    /// 부서의 달력 표시 설정(색·약칭). 비워 보내면 자동값으로 되돌린다.
    /// 색은 #RRGGBB 만 받는다 — 임의 문자열이 들어가면 화면에서 색이 아예 안 칠해진다.
    /// </summary>
    public async Task<string?> SetDeptStyleAsync(string name, string? color, string? shortName, string byUser)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return "부서를 지정하세요.";

        var c = (color ?? "").Trim();
        if (c.Length > 0 && !(c.Length == 7 && c[0] == '#' && c.Skip(1).All(Uri.IsHexDigit)))
            return "색은 #RRGGBB 형식으로 입력하세요 (예: #3D6E93). 비우면 자동으로 정합니다.";

        var sn = (shortName ?? "").Trim();
        if (sn.Length > 6) return "약칭은 6자 이내로 입력하세요.";

        var units = await _db.OrgUnits.Where(o => o.Kind == "dept" && o.Name == name).ToListAsync();
        if (units.Count == 0)
        {
            if (!await _db.Users.AnyAsync(u => u.Department == name))
                return "소속 인원이 없는 부서입니다. 먼저 부서를 등록하세요.";
            var created = new OrgUnit { Kind = "dept", Name = name };
            _db.OrgUnits.Add(created);
            units = new List<OrgUnit> { created };
        }

        foreach (var o in units) { o.Color = c; o.ShortName = sn; }
        Audit($"부서 '{name}'", "달력 표시 설정",
              $"색 {(c.Length == 0 ? "자동" : c)} / 약칭 {(sn.Length == 0 ? "자동" : sn)}", byUser);
        await _db.SaveChangesAsync();
        return null;
    }

    /// <summary>
    /// 팀의 교대 조를 지정한다. 0 = 교대 없음, 1 = 1조, 2 = 2조(1조와 반대 근무).
    ///
    /// 근무 예측·근무표·달력은 팀 이름이 아니라 이 값을 보므로, 여기만 맞춰 두면
    /// 팀 이름을 바꿔도 일정이 그대로 따라온다.
    /// </summary>
    public async Task<string?> SetOrgShiftGroupAsync(string name, int shiftGroup, string byUser, string? parent = null)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return "팀을 지정하세요.";
        if (shiftGroup is < 0 or > 2) return "교대 조는 0(없음), 1, 2 중 하나여야 합니다.";

        var units = await EnsureTeamUnitsAsync(name, parent);
        if (units is null) return "소속 인원이 없는 팀입니다. 먼저 팀원의 소속팀을 지정하세요.";

        // 같은 조를 두 팀에 줄 수 없다 — 1조와 2조는 서로 반대 근무라는 전제가 깨진다.
        // 단, 본부(사업본부)가 다르면 교대 주기 자체가 달라서 세정 1조와 wafer 1조가 공존할 수 있다.
        // 그래서 조 번호는 '전사 유일' 이 아니라 '같은 본부 안에서만 유일' 이면 된다.
        if (shiftGroup > 0)
        {
            var deptDivision = await DeptDivisionMapAsync();
            string DivisionOf(string deptName) => deptDivision.GetValueOrDefault((deptName ?? "").Trim(), "");

            var myIds = units.Select(u => u.Id).ToHashSet();
            var myDivision = DivisionOf(units[0].Parent);

            var others = await _db.OrgUnits
                .Where(o => o.Kind == "team" && o.ShiftGroup == shiftGroup)
                .Select(o => new { o.Id, o.Name, o.Parent })
                .ToListAsync();
            var taken = others.FirstOrDefault(o => !myIds.Contains(o.Id) && DivisionOf(o.Parent) == myDivision);
            if (taken is not null)
            {
                var where = myDivision.Length == 0 ? "" : $"'{myDivision}' 본부에서 ";
                return $"{shiftGroup}조는 {where}이미 '{taken.Name}' 에 지정돼 있습니다. 먼저 그 팀을 해제하세요.";
            }
        }

        foreach (var o in units) o.ShiftGroup = shiftGroup;
        var what = shiftGroup == 0 ? "교대 없음" : $"{shiftGroup}조";
        Audit($"팀 '{name}'", "교대 조 지정", what, byUser);
        await _db.SaveChangesAsync();
        return null;
    }
}
