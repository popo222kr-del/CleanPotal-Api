using System.Reflection;
using CleanPotal.Api.Infrastructure;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 관리자 › 외부 접속 보안 — 사외(모바일 데이터 등) 접속 제한 설정, 사외 허용 계정, 강제 로그아웃, 로그인 기록.
/// 관리자 영역이라 제한이 켜져 있으면 사외에서는 이 화면도 열리지 않는다(DbPermissionHandler).
/// </summary>
[ApiController]
[Route("api/security")]
[Authorize(Policy = "IsAdmin")]
public class SecurityController : ControllerBase
{
    private readonly CleanPotalDbContext _db;
    private readonly ExternalAccessPolicy _ext;
    public SecurityController(CleanPotalDbContext db, ExternalAccessPolicy ext) { _db = db; _ext = ext; }

    public record ConfigDto(bool Enforce, List<string> InternalRanges, List<string> ExternalHidden,
        string MyIp, bool MyInternal, bool DisabledByConfig, List<string> ServerGated);
    public record ConfigRequest(bool Enforce, List<string>? InternalRanges, List<string>? ExternalHidden);

    /// <summary>
    /// 서버도 막는 메뉴(API 에 MenuGate 가 붙은 메뉴). 나머지는 화면에서만 가린다 — 여러 화면이 같이 쓰는 API 라
    /// 한 메뉴 때문에 막으면 다른 화면이 깨진다.
    /// </summary>
    private static readonly List<string> Gated = typeof(SecurityController).Assembly.GetTypes()
        .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
        .SelectMany(t => t.GetCustomAttributes<MenuGateAttribute>()
            .Concat(t.GetMethods().SelectMany(m => m.GetCustomAttributes<MenuGateAttribute>())))
        .Select(a => a.Route).Distinct().OrderBy(r => r, StringComparer.Ordinal).ToList();

    [HttpGet("config")]
    public async Task<ActionResult<ConfigDto>> GetConfig()
    {
        // 화면에는 저장된 값 그대로(비상 해제로 꺼져 있어도 켜 둔 상태를 보여 준다)
        var raw = (await _db.CheckSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == ExternalAccessPolicy.SettingKey))?.Value;
        var cfg = ExternalAccessPolicy.Parse(raw);
        var ip = HttpContext.Connection.RemoteIpAddress;
        return Ok(new ConfigDto(cfg.Enforce, cfg.InternalRanges.ToList(), cfg.ExternalHidden.ToList(),
            ShowIp(ip), ExternalAccessPolicy.IsInternal(ip, cfg.InternalRanges), _ext.DisabledByConfig, Gated));
    }

    [HttpPut("config")]
    public async Task<ActionResult<ConfigDto>> PutConfig([FromBody] ConfigRequest req)
    {
        var ranges = (req.InternalRanges ?? new()).Select(r => r.Trim()).Where(r => r.Length > 0).Distinct().ToList();
        var bad = ranges.Where(r => !ExternalAccessPolicy.TryParseRange(r, out _, out _)).ToList();
        if (bad.Count > 0)
            return BadRequest(new { error = $"IP 대역을 읽을 수 없습니다: {string.Join(", ", bad)} (예: 10.10.0.0/16 또는 10.10.10.13)" });
        if (ranges.Count > 50) return BadRequest(new { error = "IP 대역은 50개까지 넣을 수 있습니다." });
        var hidden = (req.ExternalHidden ?? new()).Select(r => r.Trim()).Where(r => r.StartsWith('/') && r.Length <= 80)
            .Distinct().Take(100).ToList();

        // 잠김 방지 — 제한을 켤 때 지금 이 PC 가 사내로 잡히지 않으면 저장하지 않는다(켜는 순간 관리자도 쫓겨난다).
        var ip = HttpContext.Connection.RemoteIpAddress;
        if (req.Enforce && !ExternalAccessPolicy.IsInternal(ip, ranges))
            return BadRequest(new { error = $"지금 접속한 IP({ShowIp(ip)})가 사내 대역에 없습니다. 이대로 켜면 관리자도 들어올 수 없어 저장하지 않았습니다. 사내 대역에 이 IP 를 넣거나 회사 PC 에서 켜세요." });

        await _ext.SaveAsync(_db, new SecurityConfig(req.Enforce, ranges, hidden));
        return await GetConfig();
    }

    public record SecUserDto(int Id, string Username, string RealName, string Department, string TeamName, string JobTitle,
        bool IsAdmin, bool AllowExternal, string? LastLogin, string? LastExternal, string? RevokedAt);

    /// <summary>재직 중인 계정 + 마지막 로그인(사내·사외 모두) / 마지막 사외 로그인.</summary>
    [HttpGet("users")]
    public async Task<ActionResult<List<SecUserDto>>> Users()
    {
        var users = await _db.Users.AsNoTracking().Where(u => !u.IsResigned)
            .OrderBy(u => u.Department).ThenBy(u => u.TeamName).ThenBy(u => u.RealName).ToListAsync();
        var last = await _db.AccessLogs.AsNoTracking().Where(l => l.Result == "ok" && l.UserId != null)
            .GroupBy(l => l.UserId!.Value).Select(g => new { Id = g.Key, At = g.Max(x => x.At) }).ToListAsync();
        var lastExt = await _db.AccessLogs.AsNoTracking().Where(l => l.Result == "ok" && l.External && l.UserId != null)
            .GroupBy(l => l.UserId!.Value).Select(g => new { Id = g.Key, At = g.Max(x => x.At) }).ToListAsync();
        var lm = last.ToDictionary(x => x.Id, x => x.At);
        var le = lastExt.ToDictionary(x => x.Id, x => x.At);
        return Ok(users.Select(u => new SecUserDto(u.Id, u.Username, u.RealName, u.Department, u.TeamName, u.JobTitle,
            u.IsAdmin, u.AllowExternal,
            lm.TryGetValue(u.Id, out var a) ? Fmt(a) : null,
            le.TryGetValue(u.Id, out var b) ? Fmt(b) : null,
            u.SessionsRevokedAt is { } r ? Fmt(DateTime.SpecifyKind(r, DateTimeKind.Utc).ToLocalTime()) : null)).ToList());
    }

    public record AllowRequest(List<int> Ids, bool Allow);

    /// <summary>사외 접속 허용/해제(여러 명). 해제된 사람이 지금 사외에 있으면 다음 요청부터 끊긴다.</summary>
    [HttpPost("users/allow")]
    public async Task<ActionResult<int>> Allow([FromBody] AllowRequest req)
    {
        var ids = (req.Ids ?? new()).Distinct().ToList();
        if (ids.Count == 0) return Ok(0);
        var users = await _db.Users.Where(u => ids.Contains(u.Id)).ToListAsync();
        foreach (var u in users) u.AllowExternal = req.Allow;
        await _db.SaveChangesAsync();
        return Ok(users.Count);
    }

    /// <summary>
    /// 강제 로그아웃 — 지금까지 발급된 그 사람의 로그인(모든 기기)을 끊는다. 다시 로그인하면 들어올 수 있다
    /// (폰 분실이면 비밀번호도 바꾸거나 사외 허용을 해제한다).
    /// </summary>
    [HttpPost("users/{id:int}/revoke")]
    public async Task<ActionResult<string>> Revoke(int id)
    {
        var u = await _db.Users.FindAsync(id);
        if (u is null) return NotFound(new { error = "사용자를 찾을 수 없습니다." });
        // 토큰 발급 시각(nbf)은 초 단위라 초 아래를 버린다 — 버리지 않으면 같은 초에 다시 로그인한 토큰까지 끊긴다.
        var now = DateTime.UtcNow;
        u.SessionsRevokedAt = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        await _db.SaveChangesAsync();
        return Ok(Fmt(u.SessionsRevokedAt.Value.ToLocalTime()));
    }

    public record LogDto(long Id, string At, string Username, string RealName, string Ip, bool External, string Result, string Device);

    /// <summary>로그인 기록(최근 것부터 500줄까지).</summary>
    [HttpGet("logs")]
    public async Task<ActionResult<List<LogDto>>> Logs([FromQuery] bool externalOnly = false, [FromQuery] int days = 7,
        [FromQuery] string? q = null)
    {
        days = Math.Clamp(days, 1, 180);
        var from = DateTime.Now.Date.AddDays(-(days - 1));
        var query = _db.AccessLogs.AsNoTracking().Where(l => l.At >= from);
        if (externalOnly) query = query.Where(l => l.External);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            query = query.Where(l => l.Username.Contains(t) || l.RealName.Contains(t) || l.Ip.Contains(t));
        }
        var rows = await query.OrderByDescending(l => l.Id).Take(500).ToListAsync();
        return Ok(rows.Select(l => new LogDto(l.Id, Fmt(l.At), l.Username, l.RealName, l.Ip, l.External, l.Result, Device(l.UserAgent))).ToList());
    }

    private static string Fmt(DateTime t) => t.ToString("yyyy-MM-dd HH:mm:ss");

    private static string ShowIp(System.Net.IPAddress? ip)
        => ip is null ? "" : (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();

    /// <summary>접속 기기 대략(브라우저 정보에서) — 아이폰/안드로이드/PC 정도만.</summary>
    internal static string Device(string ua)
    {
        if (string.IsNullOrEmpty(ua)) return "";
        if (ua.Contains("iPhone")) return "아이폰";
        if (ua.Contains("iPad")) return "아이패드";
        if (ua.Contains("Android")) return ua.Contains("Mobile") ? "안드로이드폰" : "안드로이드";
        if (ua.Contains("Windows")) return "Windows PC";
        if (ua.Contains("Macintosh")) return "Mac";
        return "기타";
    }
}
