using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Core.Security;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace CleanPotal.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly CleanPotalDbContext _db;
    private readonly IConfiguration _config;

    public AuthService(CleanPotalDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
            return null;
        return IssueToken(user);
    }

    /// <summary>본인 아이디/비밀번호 변경. 현재 비밀번호 검증 후 적용, 새 토큰 발급.</summary>
    public async Task<(bool ok, string? error, LoginResponse? res)> ChangeCredentialsAsync(int userId, ChangeCredentialsRequest request)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return (false, "사용자를 찾을 수 없습니다.", null);
        if (!PasswordHasher.Verify(request.CurrentPassword ?? "", user.PasswordHash))
            return (false, "현재 비밀번호가 올바르지 않습니다.", null);

        var newUsername = request.NewUsername?.Trim();
        var newPassword = request.NewPassword;
        if (string.IsNullOrWhiteSpace(newUsername) && string.IsNullOrEmpty(newPassword))
            return (false, "변경할 아이디 또는 비밀번호를 입력하세요.", null);

        if (!string.IsNullOrWhiteSpace(newUsername) && newUsername != user.Username)
        {
            if (await _db.Users.AnyAsync(u => u.Username == newUsername && u.Id != userId))
                return (false, "이미 사용 중인 아이디입니다.", null);
            user.Username = newUsername;
        }
        if (!string.IsNullOrEmpty(newPassword))
        {
            if (newPassword.Length < 4) return (false, "비밀번호는 4자 이상이어야 합니다.", null);
            user.PasswordHash = PasswordHasher.Hash(newPassword);
        }
        await _db.SaveChangesAsync();
        return (true, null, IssueToken(user));
    }

    /// <summary>사용자로부터 JWT + 응답 DTO 생성.</summary>
    private LoginResponse IssueToken(User user)
    {
        var jwt = _config.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = DateTime.UtcNow.AddHours(int.Parse(jwt["ExpiryHours"] ?? "12"));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Username),
            new(ClaimTypes.Name, user.RealName),
            new("uid", user.Id.ToString()),
            new("team", user.TeamName),
        };
        if (user.IsAdmin) claims.Add(new Claim(ClaimTypes.Role, "admin"));
        // 권한은 매 요청 DB에서 검증(DbPermissionHandler)하므로 토큰에 perm 클레임을 싣지 않는다.

        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: claims,
            expires: expiry,
            signingCredentials: creds);

        var tokenStr = new JwtSecurityTokenHandler().WriteToken(token);
        return new LoginResponse(tokenStr, expiry, ToDto(user));
    }

    public static UserDto ToDto(User u) => new(
        u.Id, u.Username, u.RealName, u.Department, u.TeamName, u.JobTitle, u.Email, u.PhoneNumber,
        u.EmployeeNumber, u.HireDate, u.IsResigned, u.ResignDate, u.IsAdmin,
        u.AccessSchedule, u.AccessRoster, u.AccessHandover, u.AccessField, u.AccessOffice);
}
