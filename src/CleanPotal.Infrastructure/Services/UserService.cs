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

    public async Task<UserDto> CreateAsync(UserUpsertRequest req)
    {
        if (await _db.Users.AnyAsync(u => u.Username == req.Username))
            throw new InvalidOperationException("이미 존재하는 아이디입니다.");

        var u = new User { Username = req.Username };
        Apply(u, req);
        u.PasswordHash = PasswordHasher.Hash(string.IsNullOrEmpty(req.Password) ? "1234" : req.Password);
        _db.Users.Add(u);
        await _db.SaveChangesAsync();
        return AuthService.ToDto(u);
    }

    public async Task<UserDto?> UpdateAsync(int id, UserUpsertRequest req)
    {
        var u = await _db.Users.FindAsync(id);
        if (u is null) return null;

        // 1004(최고관리자) 아이디 변경 차단
        if (u.Username == "1004" && req.Username != "1004")
            throw new InvalidOperationException("최고 관리자(1004)의 아이디는 변경할 수 없습니다.");

        if (req.Username != u.Username && await _db.Users.AnyAsync(x => x.Username == req.Username))
            throw new InvalidOperationException("이미 사용 중인 아이디입니다.");

        u.Username = req.Username;
        Apply(u, req);
        if (!string.IsNullOrEmpty(req.Password))
            u.PasswordHash = PasswordHasher.Hash(req.Password);
        if (u.Username == "1004") u.IsAdmin = true;   // 최고관리자 권한 고정
        await _db.SaveChangesAsync();
        return AuthService.ToDto(u);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var u = await _db.Users.FindAsync(id);
        if (u is null) return false;
        if (u.Username == "1004")
            throw new InvalidOperationException("최고 관리자(1004) 계정은 삭제할 수 없습니다.");
        _db.Users.Remove(u);
        await _db.SaveChangesAsync();
        return true;
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
        u.CanAccessEtcMenu = r.CanAccessEtcMenu;
    }
}
