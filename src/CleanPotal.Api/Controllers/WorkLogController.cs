using CleanPotal.Api.Infrastructure;
using CleanPotal.Core.DTOs;
using CleanPotal.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 업무 파일 통합 관리 — 엑셀로 쓰던 세정·BAKE 업무 기록. 조회는 OFFICE 조회(1), 입력은 OFFICE 편집(2).
/// 화면마다 메뉴가 따로라 숨긴 메뉴 확인(MenuGate)도 동작마다 건다.
/// </summary>
[ApiController]
[Route("api/worklog")]
[Authorize(Policy = "ViewOffice")]
public class WorkLogController : ControllerBase
{
    private readonly WorkLogService _svc;
    public WorkLogController(WorkLogService svc) => _svc = svc;

    private string Actor => User.Identity?.Name ?? "";

    // ── 설비 목록(약액 교체·업무보고·그을음이 같이 쓴다) ──

    [HttpGet("equipment")]
    public async Task<ActionResult<IReadOnlyList<WorkEquipmentDto>>> Equipment() => Ok(await _svc.GetEquipmentAsync());

    [HttpPut("equipment")]
    [Authorize(Policy = "EditOffice")]
    public async Task<ActionResult<IReadOnlyList<WorkEquipmentDto>>> SaveEquipment([FromBody] WorkEquipmentSaveRequest req)
        => Ok(await _svc.SaveEquipmentAsync(req.Items));

    // ── 약액(CHEMICAL) 교체 ──

    [HttpGet("chemical")]
    [MenuGate("/work/chemical")]
    public async Task<ActionResult<ChemicalMonthDto>> Chemical([FromQuery] int year, [FromQuery] int month)
        => Ok(await _svc.GetChemicalMonthAsync(year, month));

    [HttpPut("chemical")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/chemical")]
    public async Task<ActionResult<ChemicalChangeDto?>> SaveChemical([FromBody] ChemicalSaveRequest req)
        => Ok(await _svc.SaveChemicalAsync(req, Actor));

    [HttpPost("chemical/import")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/chemical")]
    public async Task<ActionResult<ChemicalImportResultDto>> ImportChemical([FromBody] ChemicalImportRequest req)
        => Ok(await _svc.ImportChemicalAsync(req.Cells, req.Overwrite, Actor));

    // ── 가성소다·폐액 ──

    [HttpGet("waste")]
    [MenuGate("/work/waste")]
    public async Task<ActionResult<WasteMonthDto>> Waste([FromQuery] int year, [FromQuery] int month)
        => Ok(await _svc.GetWasteMonthAsync(year, month));

    [HttpGet("waste/trend")]
    [MenuGate("/work/waste")]
    public async Task<ActionResult<IReadOnlyList<WasteTrendPointDto>>> WasteTrend() => Ok(await _svc.GetWasteTrendAsync());

    [HttpPut("waste")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/waste")]
    public async Task<ActionResult<WasteLogDto?>> SaveWaste([FromBody] WasteSaveRequest req) => Ok(await _svc.SaveWasteAsync(req, Actor));

    [HttpPost("waste/import")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/waste")]
    public async Task<ActionResult<WasteImportResultDto>> ImportWaste([FromBody] WasteImportRequest req)
        => Ok(await _svc.ImportWasteAsync(req.Rows, req.Overwrite, Actor));

    // ── 업무보고(세정/BAKE) ──

    [HttpGet("report")]
    [MenuGate("/work/report")]
    public async Task<ActionResult<WorkReportDto>> Report([FromQuery] DateOnly? date)
        => Ok(await _svc.GetReportAsync(date ?? DateOnly.FromDateTime(DateTime.Now)));
}
