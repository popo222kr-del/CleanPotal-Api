using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
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

    /// <summary>본인 아이디/비밀번호 변경. POST /api/auth/change-credentials (로그인 필요)</summary>
    [Authorize]
    [HttpPost("change-credentials")]
    public async Task<ActionResult<LoginResponse>> ChangeCredentials([FromBody] ChangeCredentialsRequest req)
    {
        if (!int.TryParse(User.FindFirst("uid")?.Value, out var uid))
            return Unauthorized(new { error = "세션 정보를 확인할 수 없습니다." });
        var (ok, error, res) = await _auth.ChangeCredentialsAsync(uid, req);
        return ok ? Ok(res) : BadRequest(new { error });
    }
}
