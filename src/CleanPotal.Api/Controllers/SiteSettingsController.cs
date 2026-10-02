using System.Text.Json;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 포털 전체 화면 설정(모든 사람에게 같게 보이는 것) — 관리자만 바꾸고 누구나 읽는다.
/// 지금은 모바일 하단 메뉴 구성(mobile-tabs) 하나. 값은 JSON 그대로 CheckSettings 키-값 표에 'site:이름' 으로 둔다.
/// 개인 화면 설정은 MePrefsController(본인 계정)에 둔다.
/// </summary>
[ApiController]
[Route("api/site-settings")]
[Authorize]
public class SiteSettingsController : ControllerBase
{
    /// <summary>읽고 쓸 수 있는 설정 이름 — 아무 이름이나 쌓이지 않게 정해 둔다.</summary>
    private static readonly HashSet<string> Keys = new(StringComparer.Ordinal) { "mobile-tabs" };
    private const int MaxChars = 4000;

    private readonly CleanPotalDbContext _db;
    public SiteSettingsController(CleanPotalDbContext db) => _db = db;

    /// <summary>설정 값(JSON). 정한 적이 없으면 null — 화면이 기본값을 쓴다.</summary>
    [HttpGet("{key}")]
    public async Task<ActionResult<JsonElement?>> Get(string key)
    {
        if (!Keys.Contains(key)) return NotFound();
        var raw = (await _db.CheckSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "site:" + key))?.Value;
        if (string.IsNullOrWhiteSpace(raw)) return Ok(null);
        try { return Ok(JsonDocument.Parse(raw).RootElement.Clone()); }
        catch (JsonException) { return Ok(null); }
    }

    /// <summary>설정 저장(관리자). 본문이 null 이면 지워서 기본값으로 돌린다.</summary>
    [HttpPut("{key}")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<ActionResult<JsonElement?>> Put(string key, [FromBody] JsonElement? value)
    {
        if (!Keys.Contains(key)) return NotFound();
        var row = await _db.CheckSettings.FirstOrDefaultAsync(s => s.Key == "site:" + key);
        if (value is null || value.Value.ValueKind == JsonValueKind.Null)
        {
            if (row is not null) { _db.CheckSettings.Remove(row); await _db.SaveChangesAsync(); }
            return Ok(null);
        }
        var json = value.Value.GetRawText();
        if (json.Length > MaxChars) return BadRequest(new { message = "설정 값이 너무 깁니다." });
        if (row is null) _db.CheckSettings.Add(new CheckSetting { Key = "site:" + key, Value = json });
        else row.Value = json;
        await _db.SaveChangesAsync();
        return Ok(value);
    }
}
