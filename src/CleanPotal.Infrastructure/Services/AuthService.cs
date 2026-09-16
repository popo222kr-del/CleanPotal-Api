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
        // 퇴사 처리된 계정은 비밀번호가 맞아도 토큰을 발급하지 않는다.
        // (DbPermissionHandler 는 정책이 걸린 API만 막으므로, 발급 단계에서 함께 차단해야
        //  [Authorize] 만 걸린 me / change-credentials 까지 닫힌다.)
        if (user.IsResigned) return null;

        // WPF에서 넘어온 구형 해시(SHA-256/SHA-1/MD5)로 로그인에 성공했다면
        // 즉시 BCrypt 로 재해시해 저장한다 → 약한 해시가 DB에 남지 않는다.
        if (PasswordHasher.NeedsRehash(user.PasswordHash))
        {
            user.PasswordHash = PasswordHasher.Hash(request.Password);
            await _db.SaveChangesAsync();
        }
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
            // 비밀번호 지문 — 비밀번호가 바뀌면 기존 토큰이 자동으로 무효가 된다(Program.cs 토큰 검증).
            new("pwv", PasswordHasher.Fingerprint(user.PasswordHash)),
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
        u.Id, u.Username, u.RealName, u.Department, u.TeamName, u.Rank, u.JobTitle, u.Email, u.PhoneNumber,
        // 근속은 서버에서 계산해 내려준다 — 입사일 표기가 WPF 시절 형식과 섞여 있어(2018.06.01 등)
        // 화면마다 따로 계산하면 서로 다른 값이 나온다.
        u.EmployeeNumber, u.HireDate, CleanPotal.Core.Tenure.Format(u.HireDate), u.IsResigned, u.ResignDate, u.IsAdmin,
        u.AccessSchedule, u.AccessRoster, u.AccessHandover, u.AccessField, u.AccessOffice, u.AccessMes,
        CleanPotal.Core.MesPermissionCodes.Normalize(u.MesPermissions),
        string.IsNullOrWhiteSpace(u.HiddenMenus) ? "[]" : u.HiddenMenus);
}
