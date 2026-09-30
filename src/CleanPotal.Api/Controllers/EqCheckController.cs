using CleanPotal.Api.Infrastructure;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 체크시트 (설비) — 설비 점검표 AQ-C-13 Rev.7. 호기 QR 은 http://서버/e/{호기코드} 를 가리킨다.
/// 일상·주간 점검은 조회(1) 등급 생산직이 하고, 월간 점검은 설비팀(설정), NG 조치 완료는 편집 등급·설비팀, 양식 관리는 관리자.
/// </summary>
[ApiController]
[Route("api/eqcheck")]
[Authorize(Policy = "ViewField")]
[MenuGate("/eq-check")]
public class EqCheckController : ControllerBase
{
    private readonly EqCheckService _svc;
    private readonly IConfiguration _cfg;
    public EqCheckController(EqCheckService svc, IConfiguration cfg) { _svc = svc; _cfg = cfg; }

    private EqCheckActor Actor
    {
        get
        {
            var u = HttpContext.Items["auth_user"] as User;
            return u is null
                ? new EqCheckActor(User.Identity?.Name ?? "", User.Identity?.Name ?? "", false, false, "", "")
                : new EqCheckActor(u.Username, u.RealName, u.IsAdmin, u.IsAdmin || u.AccessField >= 2, u.Department, u.TeamName);
        }
    }

    // ── 현장(QR) ──

    [HttpGet("sheet/{code}")]
    public async Task<ActionResult<EqCheckSheetDto>> Sheet(string code, [FromQuery] DateOnly? date)
    {
        var sheet = await _svc.GetSheetAsync(code, date, Actor);
        return sheet is null ? NotFound(new { error = $"'{code}' 설비 점검표를 찾을 수 없습니다. QR 을 다시 확인하세요." }) : Ok(sheet);
    }

    [HttpPut("sheet/{code}/items/{itemId:int}")]
    public async Task<ActionResult<EqCheckResultDto?>> Save(string code, int itemId, [FromBody] EqCheckSaveRequest req)
        => Ok(await _svc.SaveResultAsync(code, itemId, req, Actor));

    [HttpPut("sheet/{code}/note")]
    public async Task<ActionResult<object>> SaveNote(string code, [FromBody] EqCheckNoteRequest req)
        => Ok(new { note = await _svc.SaveNoteAsync(code, req, Actor) });

    [HttpPost("fault")]
    public async Task<ActionResult<EqCheckNgDto>> AddFault([FromBody] EqCheckFaultRequest req)
        => Ok(await _svc.AddFaultAsync(req, Actor));

    // ── 현황·NG·월간 점검표 ──

    [HttpGet("status")]
    public async Task<ActionResult<EqCheckStatusDto>> Status([FromQuery] DateOnly? date) => Ok(await _svc.GetStatusAsync(date, Actor));

    [HttpGet("ng")]
    public async Task<ActionResult<IReadOnlyList<EqCheckNgDto>>> Ngs([FromQuery] bool open = true, [FromQuery] string? line = null,
        [FromQuery] string? unit = null, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null)
        => Ok(await _svc.GetNgsAsync(open, line, unit, from, to));

    [HttpPut("ng/{resultId:int}/close")]
    public async Task<ActionResult<EqCheckNgDto>> CloseNg(int resultId, [FromBody] EqCheckNgCloseRequest req)
        => Ok(await _svc.CloseNgAsync(resultId, req.Note, Actor));

    [HttpGet("month/{code}")]
    public async Task<ActionResult<EqCheckMonthDto>> Month(string code, [FromQuery] int year, [FromQuery] int month)
    {
        var m = await _svc.GetMonthAsync(code, year, month);
        return m is null ? NotFound() : Ok(m);
    }

    // ── 양식 관리 ──

    [HttpGet("templates")]
    public async Task<ActionResult<IReadOnlyList<EqCheckTemplateDto>>> Templates() => Ok(await _svc.GetTemplatesAsync());

    [HttpPut("templates")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<ActionResult<EqCheckTemplateDto>> SaveTemplate([FromBody] EqCheckTemplateSaveRequest req)
        => Ok(await _svc.SaveTemplateAsync(req, Actor.RealName));

    [HttpPut("items")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<ActionResult<EqCheckItemDto>> SaveItem([FromBody] EqCheckItemDto dto) => Ok(await _svc.SaveItemAsync(dto, Actor.RealName));

    [HttpPost("items/reorder")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<IActionResult> ReorderItems([FromBody] List<int> ids) { await _svc.ReorderItemsAsync(ids); return NoContent(); }

    [HttpDelete("items/{id:int}")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<IActionResult> DeleteItem(int id) => await _svc.DeleteItemAsync(id) ? NoContent() : NotFound();

    [HttpGet("units")]
    public async Task<ActionResult<IReadOnlyList<EqCheckUnitDto>>> Units() => Ok(await _svc.GetUnitsAsync());

    [HttpPut("units")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<ActionResult<EqCheckUnitDto>> SaveUnit([FromBody] EqCheckUnitSaveRequest req) => Ok(await _svc.SaveUnitAsync(req));

    [HttpGet("settings")]
    public async Task<ActionResult<object>> Settings() => Ok(new { monthlyTeams = await _svc.MonthlyTeamsAsync() });

    [HttpPut("settings")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<ActionResult<object>> SaveSettings([FromBody] Dictionary<string, string> values)
        => Ok(new { monthlyTeams = await _svc.SaveMonthlyTeamsAsync(values.GetValueOrDefault("monthlyTeams")) });

    /// <summary>호기 QR — 체크시트(현장) QR 과 같은 기본 주소(설정 파일 → 저장된 QR 주소 → 지금 접속 주소).</summary>
    [HttpGet("qr")]
    public async Task<ActionResult<object>> Qr([FromServices] CleanPotal.Core.Interfaces.ICheckSheetService check)
    {
        var settings = await check.GetSettingsAsync();
        var overrideUrl = (_cfg["Checklist:QrBaseUrl"] ?? "").Trim().TrimEnd('/');
        var saved = settings.TryGetValue("QrBaseUrl", out var b) ? (b ?? "").Trim().TrimEnd('/') : "";
        var baseUrl = overrideUrl.Length > 0 ? overrideUrl : saved.Length > 0 ? saved : $"{Request.Scheme}://{Request.Host}";
        var units = await _svc.GetUnitsAsync();
        var labels = units.Where(u => u.IsActive).Select(u =>
        {
            var url = $"{baseUrl}/e/{Uri.EscapeDataString(u.Code)}";
            return new EqCheckQrDto(u.Code, $"{u.TemplateName}{(u.Process.Length > 0 ? " · " + u.Process : "")}", url, QrSvg.Render(url));
        }).ToList();
        return Ok(new { baseUrl, labels });
    }
}
