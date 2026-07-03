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

    private static WorkAccountDto ToDto(WorkAccount a) => new(a.Id, a.Username, a.ServiceName, a.AccountId, a.AccountPassword, a.Note);
    private static WorkEduDto ToDto(WorkEdu e) => new(e.Id, e.Username, e.EduName, e.EduDate, e.Instructor, e.Note, e.StartDate, e.EndDate);

    private async Task<WorkMemberDto> ToDtoAsync(WorkMember m)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Username == m.Username);
        return new WorkMemberDto(m.Id, m.Username, u?.RealName ?? m.Username, u?.TeamName ?? "", u?.JobTitle ?? "", m.IsHidden, m.ResignDate);
    }

    public async Task<IReadOnlyList<WorkMemberDto>> GetMembersAsync(bool includeHidden)
    {
        var q = _db.WorkMembers.AsQueryable();
        if (!includeHidden) q = q.Where(m => !m.IsHidden);
        var members = await q.ToListAsync();
        var users = await _db.Users.ToDictionaryAsync(u => u.Username, u => u);
        return members
            .Select(m =>
            {
                users.TryGetValue(m.Username, out var u);
                return new WorkMemberDto(m.Id, m.Username, u?.RealName ?? m.Username, u?.TeamName ?? "", u?.JobTitle ?? "", m.IsHidden, m.ResignDate);
            })
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
