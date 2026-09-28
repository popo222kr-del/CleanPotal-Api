using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 내 화면 설정(달력에 켜 둔 부서·교대 근무 표시 등). 예전에는 브라우저에만 기억해서 PC 와 폰이 따로 놀았다.
/// 설정은 이름(key)별 JSON 값으로 계정(User.UiPrefs)에 모아 둔다. 본인 것만 읽고 쓴다.
/// </summary>
[ApiController]
[Route("api/me/prefs")]
[Authorize]
public partial class MePrefsController : ControllerBase
{
    private const int MaxValueChars = 4000;
    private const int MaxTotalChars = 16000;
    private readonly CleanPotalDbContext _db;
    public MePrefsController(CleanPotalDbContext db) => _db = db;

    [GeneratedRegex("^[a-z][a-z0-9-]{0,39}$")]
    private static partial Regex KeyPattern();

    private static JsonObject Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new JsonObject();
        try { return JsonNode.Parse(raw) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }

    private int? Uid => int.TryParse(User.FindFirst("uid")?.Value, out var id) ? id : null;

    /// <summary>GET /api/me/prefs → { "calendar": {...}, ... }</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (Uid is not int uid) return Unauthorized(new { error = "세션 정보를 확인할 수 없습니다." });
        var u = await _db.Users.FindAsync(uid);
        if (u is null) return Unauthorized(new { error = "사용자를 찾을 수 없습니다." });
        return Content(Parse(u.UiPrefs).ToJsonString(), "application/json");
    }

    /// <summary>PUT /api/me/prefs/{key} (본문 = 그 설정의 JSON 값). null 이면 그 설정을 지운다(화면 기본값으로).</summary>
    [HttpPut("{key}")]
    public async Task<IActionResult> Put(string key, [FromBody] JsonElement value)
    {
        if (Uid is not int uid) return Unauthorized(new { error = "세션 정보를 확인할 수 없습니다." });
        if (!KeyPattern().IsMatch(key)) return BadRequest(new { error = "설정 이름이 올바르지 않습니다." });
        var raw = value.GetRawText();
        if (raw.Length > MaxValueChars) return BadRequest(new { error = "설정 값이 너무 큽니다." });

        var u = await _db.Users.FindAsync(uid);
        if (u is null) return Unauthorized(new { error = "사용자를 찾을 수 없습니다." });
        var prefs = Parse(u.UiPrefs);
        if (value.ValueKind == JsonValueKind.Null) prefs.Remove(key);
        else prefs[key] = JsonNode.Parse(raw);
        var json = prefs.ToJsonString();
        if (json.Length > MaxTotalChars) return BadRequest(new { error = "저장할 수 있는 설정이 너무 많습니다." });
        u.UiPrefs = json;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
