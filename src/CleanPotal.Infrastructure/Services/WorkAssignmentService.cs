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

    private static WorkMemberDto ToDto(WorkMember m, User? u) => new(
        m.Id, m.Username,
        // 계정을 못 찾아도 사번을 이름인 것처럼 보여주지 않는다 — 계정 연결이 빠졌음을 드러낸다
        string.IsNullOrWhiteSpace(u?.RealName) ? $"{m.Username} (계정 미등록)" : u!.RealName,
        u?.TeamName ?? "", u?.JobTitle ?? "", m.IsHidden, m.ResignDate);

    private static WorkAccountDto ToDto(WorkAccount a) => new(a.Id, a.Username, a.ServiceName, a.AccountId, a.AccountPassword, a.Note);
    private static WorkEduDto ToDto(WorkEdu e) => new(e.Id, e.Username, e.EduName, e.EduDate, e.Instructor, e.Note, e.StartDate, e.EndDate);

    private async Task<WorkMemberDto> ToDtoAsync(WorkMember m)
    {
        var lookup = new UserLookup(await _db.Users.ToListAsync());
        return ToDto(m, lookup.Find(m.Username));
    }

    public async Task<IReadOnlyList<WorkMemberDto>> GetMembersAsync(bool includeHidden)
    {
        var q = _db.WorkMembers.AsQueryable();
        if (!includeHidden) q = q.Where(m => !m.IsHidden);
        var members = await q.ToListAsync();
        var lookup = new UserLookup(await _db.Users.ToListAsync());
        return members
            .Select(m => ToDto(m, lookup.Find(m.Username)))
            .OrderBy(m => m.TeamName).ThenBy(m => m.RealName).ToList();
    }

    public async Task<WorkMemberDetailDto?> GetMemberAsync(string username)
    {
        var m = await _db.WorkMembers.FirstOrDefaultAsync(x => x.Username == username);
        if (m is null) return null;
        var accounts = await _db.WorkAccounts.Where(a => a.Username == username).OrderBy(a => a.ServiceName).ToListAsync();
        var edus = await _db.WorkEdus.Where(e => e.Username == username).OrderByDescending(e => e.EduDate).ToListAsync();
        return new WorkMemberDetailDto(await ToDtoAsync(m), accounts.Select(ToDto).ToList(), edus.Select(ToDto).ToList());
    }

    public async Task<WorkMemberDto> AddMemberAsync(WorkMemberUpsertRequest r)
    {
        var m = await _db.WorkMembers.FirstOrDefaultAsync(x => x.Username == r.Username)
                ?? new WorkMember { Username = r.Username };
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
}
