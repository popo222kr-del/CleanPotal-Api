using CleanPotal.Api.Infrastructure;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// QR 체크시트(현장 점검). 조회는 현장 점검 조회(1), 입력·제출·NG 조치는 편집(2), 양식 관리는 관리자.
/// 구역 QR 은 http://서버/c/{구역코드} 를 가리키고, 그 화면이 이 API 를 부른다.
/// </summary>
[ApiController]
[Route("api/checklist")]
[Authorize(Policy = "ViewField")]
[MenuGate("/checklist")]
public class ChecklistController : ControllerBase
{
    private readonly ICheckSheetService _svc;
    public ChecklistController(ICheckSheetService svc) => _svc = svc;

    private CheckActor Actor
    {
        get
        {
            var u = HttpContext.Items["auth_user"] as User;
            return u is null
                ? new CheckActor(User.Identity?.Name ?? "", User.Identity?.Name ?? "", false, false)
                : new CheckActor(u.Username, u.RealName, u.IsAdmin, u.IsAdmin || u.AccessField >= 2);
        }
    }

    // ── 현장(QR) ──

    [HttpGet("sheet/{code}")]
    public async Task<ActionResult<CheckSheetDto>> Sheet(string code, [FromQuery] DateOnly? date, [FromQuery] string? shift)
    {
        var sheet = await _svc.GetSheetAsync(code, date, shift, Actor);
        return sheet is null ? NotFound(new { error = $"'{code}' 구역을 찾을 수 없습니다. QR 을 다시 확인하세요." }) : Ok(sheet);
    }

    [HttpPut("sheet/{code}/items/{itemId:int}")]
    [Authorize(Policy = "EditField")]
    public async Task<ActionResult<CheckResultDto?>> Save(string code, int itemId, [FromBody] CheckResultSaveRequest req)
        => Ok(await _svc.SaveResultAsync(code, itemId, req, Actor));

    [HttpPost("sheet/{code}/submit")]
    [Authorize(Policy = "EditField")]
    public async Task<ActionResult<CheckSheetDto>> Submit(string code, [FromBody] CheckSubmitRequest req)
        => Ok(await _svc.SubmitAsync(code, req, Actor));

    // ── 현황·NG·리포트 ──

    [HttpGet("status")]
    public async Task<ActionResult<CheckStatusDto>> Status([FromQuery] DateOnly? date)
        => Ok(await _svc.GetStatusAsync(date));

    [HttpGet("ng")]
    public async Task<ActionResult<IReadOnlyList<CheckNgDto>>> Ngs(
        [FromQuery] bool open = true, [FromQuery] string? line = null, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null)
        => Ok(await _svc.GetNgsAsync(open, line, from, to));

    [HttpPut("ng/{resultId:int}/close")]
    [Authorize(Policy = "EditField")]
    public async Task<ActionResult<CheckNgDto>> CloseNg(int resultId, [FromBody] CheckNgCloseRequest req)
        => Ok(await _svc.CloseNgAsync(resultId, req.Note, Actor));

    [HttpGet("report")]
    public async Task<ActionResult<CheckReportDto>> Report([FromQuery] string line, [FromQuery] int year, [FromQuery] int month)
        => Ok(await _svc.GetReportAsync(line, year, month));

    // ── QR 라벨 ──

    /// <summary>구역 QR — 주소와 SVG 그림. 기본 주소는 설정(QrBaseUrl), 없으면 지금 접속한 주소.</summary>
    [HttpGet("qr")]
    public async Task<ActionResult<IReadOnlyList<CheckQrDto>>> Qr()
    {
        var settings = await _svc.GetSettingsAsync();
        var baseUrl = settings.TryGetValue("QrBaseUrl", out var b) && !string.IsNullOrWhiteSpace(b)
            ? b.TrimEnd('/')
            : $"{Request.Scheme}://{Request.Host}";
        var zones = await _svc.GetZonesAsync();
        return Ok(zones.Where(z => z.HasQr && z.IsActive && !z.IsCommon)
            .Select(z =>
            {
                var url = $"{baseUrl}/c/{Uri.EscapeDataString(z.Code)}";
                return new CheckQrDto(z.Code, z.Name, url, QrSvg.Render(url));
            }).ToList());
    }

    // ── 양식 관리 ──

    [HttpGet("settings")]
    public async Task<ActionResult<IReadOnlyDictionary<string, string>>> Settings() => Ok(await _svc.GetSettingsAsync());

    [HttpPut("settings")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<ActionResult<IReadOnlyDictionary<string, string>>> SaveSettings([FromBody] Dictionary<string, string> values)
        => Ok(await _svc.SaveSettingsAsync(values));

    [HttpGet("zones")]
    public async Task<ActionResult<IReadOnlyList<CheckZoneDto>>> Zones() => Ok(await _svc.GetZonesAsync());

    [HttpPut("zones")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<ActionResult<CheckZoneDto>> SaveZone([FromBody] CheckZoneDto dto) => Ok(await _svc.SaveZoneAsync(dto));

    [HttpGet("items")]
    public async Task<ActionResult<IReadOnlyList<CheckItemDto>>> Items() => Ok(await _svc.GetItemsAsync());

    [HttpPut("items")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<ActionResult<CheckItemDto>> SaveItem([FromBody] CheckItemDto dto) => Ok(await _svc.SaveItemAsync(dto, Actor));

    [HttpDelete("items/{id:int}")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<IActionResult> DeleteItem(int id) => await _svc.DeleteItemAsync(id) ? NoContent() : NotFound();

    [HttpPost("import")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<ActionResult<CheckImportResultDto>> Import([FromBody] CheckImportRequest req)
        => Ok(await _svc.ImportAsync(req, Actor));
}
