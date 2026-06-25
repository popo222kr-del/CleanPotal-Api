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
        if (user.CanManageFiles) claims.Add(new Claim("perm", "files"));
        if (user.CanManageSchedule) claims.Add(new Claim("perm", "schedule"));

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
        u.Id, u.Username, u.RealName, u.TeamName, u.JobTitle, u.Email, u.PhoneNumber,
        u.EmployeeNumber, u.HireDate, u.IsResigned, u.ResignDate, u.IsAdmin,
        u.CanManageFiles, u.CanManageNotices, u.CanManageVendors, u.CanManageSchedule, u.CanAccessEtcMenu);
}
