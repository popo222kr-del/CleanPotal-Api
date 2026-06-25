using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    public AuthController(IAuthService auth) => _auth = auth;

    /// <summary>로그인 → JWT 발급. POST /api/auth/login { username, password }</summary>
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req)
    {
        var res = await _auth.LoginAsync(req);
        if (res is null)
            return Unauthorized(new { error = "아이디 또는 비밀번호가 올바르지 않습니다." });
        return Ok(res);
    }
}
