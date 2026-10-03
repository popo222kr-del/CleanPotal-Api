using CleanPotal.Api.Infrastructure;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IUserService _users;
    private readonly LoginThrottle _throttle;
    private readonly ExternalAccessPolicy _ext;
    private readonly CleanPotalDbContext _db;
    public AuthController(IAuthService auth, IUserService users, LoginThrottle throttle, ExternalAccessPolicy ext, CleanPotalDbContext db)
    {
        _auth = auth; _users = users; _throttle = throttle; _ext = ext; _db = db;
    }

    /// <summary>접속 기록 보관 기간 — 이보다 오래된 기록은 가끔 지운다.</summary>
    private const int LogKeepDays = 180;

    /// <summary>현재 사용자 정보(DB 기준 최신 권한). 프론트가 주기적으로 호출해 권한 변경을 즉시 반영.</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me()
    {
        if (!int.TryParse(User.FindFirst("uid")?.Value, out var uid))
            return Unauthorized(new { error = "세션 정보를 확인할 수 없습니다." });
        var u = await _users.GetAsync(uid);
        return u is null ? Unauthorized(new { error = "사용자를 찾을 수 없습니다." })
            : Ok(ExternalAccessPolicy.ForClient(u, ExternalAccessPolicy.Restricted(HttpContext)));
    }

    /// <summary>로그인 → JWT 발급. POST /api/auth/login { username, password }</summary>
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var sec = await _ext.GetAsync();
        var external = !ExternalAccessPolicy.IsInternal(HttpContext.Connection.RemoteIpAddress, sec.InternalRanges);

        // 같은 아이디·같은 IP 에서 연속 실패가 많으면 일정 시간 차단한다.
        if (_throttle.RetryAfter(req.Username, ip) is { } remain)
        {
            await LogAsync(req.Username, null, external, "throttled");
            Response.Headers.RetryAfter = ((int)Math.Ceiling(remain.TotalSeconds)).ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = $"로그인 시도가 너무 많습니다. {Math.Ceiling(remain.TotalMinutes)}분 뒤에 다시 시도하세요."
            });
        }

        var res = await _auth.LoginAsync(req);
        if (res is null)
        {
            _throttle.RecordFailure(req.Username, ip);
            await LogAsync(req.Username, null, external, "fail");
            // 비밀번호 오류와 퇴사 계정을 같은 문구로 응답한다(계정 존재 여부 노출 방지).
            return Unauthorized(new { error = "아이디 또는 비밀번호가 올바르지 않습니다." });
        }
        _throttle.RecordSuccess(req.Username, ip);

        // 외부 접속 제한 — 비밀번호가 맞아도 허용되지 않은 계정은 사외에서 들어오지 못한다.
        // (비밀번호를 확인한 뒤에 알려 준다 — 먼저 알려 주면 계정이 있는지 밖에서 알 수 있다.)
        if (sec.Enforce && external && !res.User.AllowExternal)
        {
            await LogAsync(res.User.Username, res.User, external, "blocked");
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "이 계정은 사외(모바일 데이터 등)에서 접속할 수 없습니다. 회사 와이파이에서 접속하세요."
            });
        }
        await LogAsync(res.User.Username, res.User, external, "ok");
        if (sec.Enforce && external)
            res = res with { User = ExternalAccessPolicy.ForClient(res.User, sec.ExternalHidden) };
        return Ok(res);
    }

    /// <summary>
    /// 로그인 기록 한 줄(관리자 › 외부 접속 보안 › 접속 기록). 기록이 실패해도 로그인은 막지 않는다.
    /// 비밀번호는 남기지 않는다 — 아이디 칸에 비밀번호를 잘못 넣은 경우까지 생각해 실패 기록의 아이디도 길이를 자른다.
    /// </summary>
    private async Task LogAsync(string? username, UserDto? user, bool external, string result)
    {
        try
        {
            var ua = Request.Headers.UserAgent.ToString();
            _db.AccessLogs.Add(new AccessLog
            {
                At = DateTime.Now,
                UserId = user?.Id,
                Username = Cut((username ?? "").Trim(), 100),
                RealName = Cut(user?.RealName ?? "", 100),
                Ip = Cut(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "", 64),
                External = external,
                Result = result,
                UserAgent = Cut(ua, 300),
            });
            await _db.SaveChangesAsync();
            // 오래된 기록 정리 — 로그인 몇백 번에 한 번
            if (Random.Shared.Next(300) == 0)
            {
                var cut = DateTime.Now.AddDays(-LogKeepDays);
                await _db.AccessLogs.Where(l => l.At < cut).ExecuteDeleteAsync();
            }
        }
        catch (Exception) { /* 기록 실패는 로그인과 무관 */ }
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>본인 아이디/비밀번호 변경. POST /api/auth/change-credentials (로그인 필요)</summary>
    [Authorize]
    [HttpPost("change-credentials")]
    public async Task<ActionResult<LoginResponse>> ChangeCredentials([FromBody] ChangeCredentialsRequest req)
    {
        if (!int.TryParse(User.FindFirst("uid")?.Value, out var uid))
            return Unauthorized(new { error = "세션 정보를 확인할 수 없습니다." });
        var (ok, error, res) = await _auth.ChangeCredentialsAsync(uid, req);
        return ok ? Ok(res! with { User = ExternalAccessPolicy.ForClient(res.User, ExternalAccessPolicy.Restricted(HttpContext)) })
            : BadRequest(new { error });
    }
}
