using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class WorkAssignmentService : IWorkAssignmentService
{
    private readonly CleanPotalDbContext _db;
    public WorkAssignmentService(CleanPotalDbContext db) => _db = db;

    /// <summary>
    /// 분장표 인원 키 → 계정 찾기.
    ///
    /// <c>WorkMember.Username</c> 은 WPF 에서 그대로 옮겨온 값인데 실제로는 <b>사번</b>이 들어 있다.
    /// 대부분의 직원은 로그인 아이디를 사번으로 쓰고 있어 아이디로 찾아도 맞았지만,
    /// 로그인 아이디가 사번과 다른 사람(예: 로그인 0907 / 사번 1210045)은 매칭이 되지 않아
    /// 화면에 이름 대신 사번이 그대로 찍혔다. 그래서 <b>아이디 → 사번</b> 순으로 찾는다.
    /// </summary>
    private sealed class UserLookup
    {
        private readonly Dictionary<string, User> _byLogin;
        private readonly Dictionary<string, User> _byEmployeeNo;

        public UserLookup(IEnumerable<User> users)
        {
            var list = users.ToList();
            _byLogin = list
                .Where(u => !string.IsNullOrWhiteSpace(u.Username))
                .GroupBy(u => u.Username.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            // 사번이 중복 입력된 계정이 있어도 터지지 않도록 GroupBy 로 하나만 고른다
            _byEmployeeNo = list
                .Where(u => !string.IsNullOrWhiteSpace(u.EmployeeNumber))
                .GroupBy(u => u.EmployeeNumber.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        public User? Find(string? key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) return null;
            if (_byLogin.TryGetValue(key, out var byLogin)) return byLogin;
            return _byEmployeeNo.TryGetValue(key, out var byEmpNo) ? byEmpNo : null;
        }
    }

    private static WorkMemberDto ToDto(WorkMember m, User? u, int accountCount = 0, int eduCount = 0) => new(
        m.Id, m.Username,
        // 계정을 못 찾아도 사번을 이름인 것처럼 보여주지 않는다 — 계정 연결이 빠졌음을 드러낸다
        string.IsNullOrWhiteSpace(u?.RealName) ? $"{m.Username} (계정 미등록)" : u!.RealName,
        u?.Department ?? "", u?.TeamName ?? "", u?.JobTitle ?? "",
        // 사번은 계정 값이 정본이고, 계정을 못 찾으면 분장표에 저장된 키가 곧 사번이다
        string.IsNullOrWhiteSpace(u?.EmployeeNumber) ? m.Username : u!.EmployeeNumber,
        u?.HireDate ?? "",
        CleanPotal.Core.Tenure.Format(u?.HireDate),
        u?.Email ?? "",
        u?.PhoneNumber ?? "",
        // 재직 여부는 계정을 정본으로 본다 — 사용자 계정 관리 화면과 같은 기준
        u?.IsResigned ?? false,
        // 퇴사일은 계정 값 우선, 없으면 WPF 시절 분장표에 남아 있던 값
        string.IsNullOrWhiteSpace(u?.ResignDate) ? m.ResignDate : u!.ResignDate,
        m.IsHidden,
        u is not null,
        u?.Id,
        accountCount,
        eduCount);

    private static WorkAccountDto ToDto(WorkAccount a) => new(a.Id, a.Username, a.ServiceName, a.AccountId, a.AccountPassword, a.Note);
    private static WorkEduDto ToDto(WorkEdu e) => new(
        e.Id, e.Username, e.EduName, e.EduDate, e.Instructor, e.Note, e.StartDate, e.EndDate,
        EduPeriod.Format(e.StartDate, e.EndDate, e.EduDate));

    private async Task<WorkMemberDto> ToDtoAsync(WorkMember m)
    {
        var lookup = new UserLookup(await _db.Users.ToListAsync());
        return ToDto(m, lookup.Find(m.Username));
    }

    /// <summary>
    /// 인원 목록. 숨김 인원까지 <b>전부</b> 돌려주고, 재직/퇴사·숨김 구분은 화면에서 한다.
    /// (여기서 먼저 걸러내면 "숨김 처리된 퇴사자"가 퇴사자 탭에서도 사라진다.
    ///  사용자 계정 관리 화면도 전부 받아서 화면에서 나누는 방식이다.)
    /// <paramref name="includeHidden"/> 는 옛 호출자 호환용으로만 남겨 둔다.
    /// </summary>
    public async Task<IReadOnlyList<WorkMemberDto>> GetMembersAsync(bool includeHidden)
    {
        var members = await _db.WorkMembers.ToListAsync();
        var lookup = new UserLookup(await _db.Users.ToListAsync());

        // 중복 등록된 인원 중 어느 행이 비어 있는지 화면에서 바로 보이도록 건수를 함께 싣는다.
        var accountCounts = CountByUsername(await _db.WorkAccounts.Select(a => a.Username).ToListAsync());
        var eduCounts = CountByUsername(await _db.WorkEdus.Select(e => e.Username).ToListAsync());

        return members
            .Select(m => ToDto(m, lookup.Find(m.Username),
                               accountCounts.GetValueOrDefault(m.Username),
                               eduCounts.GetValueOrDefault(m.Username)))
            .OrderBy(m => m.TeamName).ThenBy(m => m.RealName).ToList();
    }

    private static Dictionary<string, int> CountByUsername(IEnumerable<string> usernames)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var u in usernames)
        {
            var key = u ?? "";
            d[key] = d.GetValueOrDefault(key) + 1;
        }
        return d;
    }

    public async Task<WorkMemberDetailDto?> GetMemberAsync(string username)
    {
        var m = await _db.WorkMembers.FirstOrDefaultAsync(x => x.Username == username);
        if (m is null) return null;

        var users = await _db.Users.ToListAsync();
        var user = new UserLookup(users).Find(m.Username);

        var accounts = await _db.WorkAccounts.Where(a => a.Username == username).OrderBy(a => a.ServiceName).ToListAsync();
        // WPF 기본 교육 기록은 1, 2, 3, 4, 4-1 … 처럼 정해진 순서가 있고 그 순서대로 적재된다.
        // 예전에는 EduDate 로 내림차순 정렬했는데 그 컬럼은 WPF 에 없어 전부 비어 있었다
        // (= 정렬이 사실상 무작위). 적재 순서를 그대로 쓴다.
        var edus = await _db.WorkEdus.Where(e => e.Username == username).OrderBy(e => e.Id).ToListAsync();

        // ── 외부 교육 기록 (교육 현황 대시보드 자동 연동) ──
        // 대시보드는 사람을 실명 문자열로 기록하므로 이름으로 이어 붙인다.
        // 이름이 없거나(계정 미연결) 비어 있으면 아무것도 끌어오지 않는다 —
        // 빈 이름으로 조회하면 남의 기록이 통째로 딸려 온다.
        var realName = (user?.RealName ?? "").Trim();
        var external = new List<EducationPlan>();
        var ambiguous = false;
        if (realName.Length > 0)
        {
            external = await _db.EducationPlans
                .Where(e => e.MemberName == realName)
                .OrderByDescending(e => e.StartDate).ThenByDescending(e => e.Id)
                .ToListAsync();
            // 동명이인이면 남의 교육이 섞여 보인다 — 숨기지 말고 화면에서 알리게 한다.
            ambiguous = users.Count(u => string.Equals((u.RealName ?? "").Trim(), realName, StringComparison.Ordinal)) > 1;
        }

        return new WorkMemberDetailDto(
            ToDto(m, user,
                  await _db.WorkAccounts.CountAsync(a => a.Username == username),
                  await _db.WorkEdus.CountAsync(e => e.Username == username)),
            accounts.Select(ToDto).ToList(),
            edus.Select(ToDto).ToList(),
            external.Select(EducationService.ToDto).ToList(),
            ambiguous);
    }

    public async Task<WorkMemberDto> AddMemberAsync(WorkMemberUpsertRequest r)
    {
        var m = await _db.WorkMembers.FirstOrDefaultAsync(x => x.Username == r.Username);
        if (m is null)
        {
            // 같은 사람을 로그인 아이디로 한 번, 사번으로 또 한 번 등록하면 목록에 두 줄이 된다.
            // (실제로 김태종 님이 0907 / 1210045 두 키로 등록돼 있었다)
            // 키가 다르면 같은 키 검사만으로는 못 막으므로, 계정까지 풀어서 확인한다.
            var users = await _db.Users.ToListAsync();
            var lookup = new UserLookup(users);
            var target = lookup.Find(r.Username);
            if (target is not null)
            {
                var existing = await _db.WorkMembers.ToListAsync();
                var already = existing.FirstOrDefault(x => lookup.Find(x.Username)?.Id == target.Id);
                if (already is not null)
                    throw new BusinessRuleException(
                        $"{target.RealName} 님은 이미 '{already.Username}' 로 등록되어 있습니다. " +
                        "한 사람을 아이디와 사번으로 따로 등록하면 목록에 두 번 나옵니다.");
            }
            m = new WorkMember { Username = r.Username };
        }
        m.IsHidden = r.IsHidden;
        m.ResignDate = r.ResignDate ?? "";
        if (m.Id == 0) _db.WorkMembers.Add(m);
        await _db.SaveChangesAsync();
        return await ToDtoAsync(m);
    }

    public async Task<WorkMemberDto?> UpdateMemberAsync(int id, WorkMemberUpsertRequest r)
    {
        var m = await _db.WorkMembers.FindAsync(id);
        if (m is null) return null;
        m.IsHidden = r.IsHidden;
        m.ResignDate = r.ResignDate ?? "";
        await _db.SaveChangesAsync();
        return await ToDtoAsync(m);
    }

    public async Task<bool> DeleteMemberAsync(int id)
    {
        var m = await _db.WorkMembers.FindAsync(id);
        if (m is null) return false;
        _db.WorkAccounts.RemoveRange(_db.WorkAccounts.Where(a => a.Username == m.Username));
        _db.WorkEdus.RemoveRange(_db.WorkEdus.Where(e => e.Username == m.Username));
        _db.WorkMembers.Remove(m);
        await _db.SaveChangesAsync();
        return true;
    }

    // ── 계정 ──
    public async Task<WorkAccountDto> SaveAccountAsync(WorkAccountUpsertRequest r)
    {
        var a = new WorkAccount
        {
            Username = r.Username, ServiceName = r.ServiceName, AccountId = r.AccountId,
            AccountPassword = r.AccountPassword ?? "", Note = r.Note ?? "",
        };
        _db.WorkAccounts.Add(a);
        await _db.SaveChangesAsync();
        return ToDto(a);
    }

    public async Task<WorkAccountDto?> UpdateAccountAsync(int id, WorkAccountUpsertRequest r)
    {
        var a = await _db.WorkAccounts.FindAsync(id);
        if (a is null) return null;
        a.ServiceName = r.ServiceName; a.AccountId = r.AccountId;
        a.AccountPassword = r.AccountPassword ?? ""; a.Note = r.Note ?? "";
        await _db.SaveChangesAsync();
        return ToDto(a);
    }

    public async Task<bool> DeleteAccountAsync(int id)
    {
        var a = await _db.WorkAccounts.FindAsync(id);
        if (a is null) return false;
        _db.WorkAccounts.Remove(a);
        await _db.SaveChangesAsync();
        return true;
    }

    // ── 교육 이수 ──
    public async Task<WorkEduDto> SaveEduAsync(WorkEduUpsertRequest r)
    {
        var e = new WorkEdu
        {
            Username = r.Username, EduName = r.EduName, EduDate = r.EduDate ?? "",
            Instructor = r.Instructor ?? "", Note = r.Note ?? "", StartDate = r.StartDate ?? "", EndDate = r.EndDate ?? "",
        };
        _db.WorkEdus.Add(e);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public async Task<WorkEduDto?> UpdateEduAsync(int id, WorkEduUpsertRequest r)
    {
        var e = await _db.WorkEdus.FindAsync(id);
        if (e is null) return null;
        e.EduName = r.EduName; e.EduDate = r.EduDate ?? "";
        e.Instructor = r.Instructor ?? ""; e.Note = r.Note ?? "";
        e.StartDate = r.StartDate ?? ""; e.EndDate = r.EndDate ?? "";
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public async Task<bool> DeleteEduAsync(int id)
    {
        var e = await _db.WorkEdus.FindAsync(id);
        if (e is null) return false;
        _db.WorkEdus.Remove(e);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>한 사람이 가질 수 있는 교육 줄 수 상한 — 실수로 거대한 요청이 오는 것을 막는다.</summary>
    private const int MaxEduRows = 300;

    /// <summary>
    /// 기본 교육 기록 표를 통째로 저장한다.
    /// <b>보낸 목록이 곧 최종 상태</b>다 — 목록에서 빠진 줄은 삭제된다(표에서 ✕ 로 지운 줄).
    /// 화면에서 여러 줄을 고친 뒤 한 번에 저장하는 방식이라, 줄마다 따로 저장하면
    /// 중간에 실패했을 때 절반만 반영된 상태가 남는다.
    /// </summary>
    public async Task<IReadOnlyList<WorkEduDto>> SaveEdusAsync(WorkEduBulkSaveRequest req)
    {
        var username = (req.Username ?? "").Trim();
        if (username.Length == 0)
            throw new BusinessRuleException("대상 인원이 지정되지 않았습니다.");
        if (!await _db.WorkMembers.AnyAsync(m => m.Username == username))
            throw new BusinessRuleException("분장표에 없는 인원입니다.");

        // 교육명이 빈 줄은 저장하지 않는다 — '행 추가' 후 입력하지 않고 저장한 경우.
        var rows = (req.Rows ?? Array.Empty<WorkEduRowInput>())
            .Where(r => !string.IsNullOrWhiteSpace(r.EduName))
            .ToList();
        if (rows.Count > MaxEduRows)
            throw new BusinessRuleException($"교육 기록은 한 번에 {MaxEduRows}줄까지 저장할 수 있습니다.");

        var existing = await _db.WorkEdus.Where(e => e.Username == username).ToListAsync();
        var byId = existing.ToDictionary(e => e.Id);
        var kept = new HashSet<int>();

        foreach (var r in rows)
        {
            if (r.Id > 0 && byId.TryGetValue(r.Id, out var e))
            {
                Apply(e, r);
                kept.Add(e.Id);
            }
            else
            {
                var added = new WorkEdu { Username = username };
                Apply(added, r);
                _db.WorkEdus.Add(added);
            }
        }

        // 화면이 알고 있던 줄 가운데 빠진 것만 지운다. 예전에는 요청을 전체 상태로 보고 나머지를 모두 지워서,
        // 같은 사람의 교육 기록을 둘이 동시에 고치면 먼저 저장한 사람이 추가한 줄이 사라졌다.
        var known = req.KnownIds?.ToHashSet();
        foreach (var e in existing.Where(e => !kept.Contains(e.Id) && (known is null || known.Contains(e.Id))))
            _db.WorkEdus.Remove(e);

        await _db.SaveChangesAsync();
        return await ReadEdusAsync(username);

        static void Apply(WorkEdu e, WorkEduRowInput r)
        {
            e.EduName = r.EduName.Trim();
            e.StartDate = (r.StartDate ?? "").Trim();
            e.EndDate = (r.EndDate ?? "").Trim();
            e.Instructor = (r.Instructor ?? "").Trim();
            e.Note = (r.Note ?? "").Trim();
        }
    }

    /// <summary>
    /// 다른 사람의 교육 목록을 가져온다. <b>교육명만</b> 복사하고 이수 내역(날짜·강사·비고)은
    /// 복사하지 않는다 — 남의 이수일을 그대로 옮기면 받지 않은 교육을 받은 것처럼 기록된다.
    /// 이미 같은 교육명이 있으면 건너뛴다(여러 번 눌러도 줄이 불어나지 않는다).
    /// </summary>
    public async Task<IReadOnlyList<WorkEduDto>> CopyEdusAsync(WorkEduCopyRequest req)
    {
        var from = (req.FromUsername ?? "").Trim();
        var to = (req.ToUsername ?? "").Trim();
        if (from.Length == 0 || to.Length == 0)
            throw new BusinessRuleException("가져올 대상과 받을 대상을 모두 지정하세요.");
        if (string.Equals(from, to, StringComparison.Ordinal))
            throw new BusinessRuleException("같은 사람에게서 가져올 수는 없습니다.");
        if (!await _db.WorkMembers.AnyAsync(m => m.Username == to))
            throw new BusinessRuleException("분장표에 없는 인원입니다.");

        var source = await _db.WorkEdus.Where(e => e.Username == from).OrderBy(e => e.Id).ToListAsync();
        if (source.Count == 0)
            throw new BusinessRuleException("가져올 교육 기록이 없습니다.");

        var already = (await _db.WorkEdus.Where(e => e.Username == to).Select(e => e.EduName).ToListAsync())
            .Select(n => (n ?? "").Trim())
            .ToHashSet(StringComparer.Ordinal);

        var added = 0;
        foreach (var src in source)
        {
            var name = (src.EduName ?? "").Trim();
            if (name.Length == 0 || !already.Add(name)) continue;
            _db.WorkEdus.Add(new WorkEdu { Username = to, EduName = name });
            added++;
        }

        if (added > 0) await _db.SaveChangesAsync();
        return await ReadEdusAsync(to);
    }

    /// <summary>적재 순서(= WPF 템플릿 순서)대로 읽는다.</summary>
    private async Task<IReadOnlyList<WorkEduDto>> ReadEdusAsync(string username)
    {
        var list = await _db.WorkEdus.Where(e => e.Username == username).OrderBy(e => e.Id).ToListAsync();
        return list.Select(ToDto).ToList();
    }
}
