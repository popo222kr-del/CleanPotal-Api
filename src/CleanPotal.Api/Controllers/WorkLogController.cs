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

    // ── BAKE OVEN 그을음 ──

    [HttpGet("bake")]
    [MenuGate("/work/bake")]
    public async Task<ActionResult<BakeDayDto>> Bake([FromQuery] DateOnly? date)
        => Ok(await _svc.GetBakeDayAsync(date ?? DateOnly.FromDateTime(DateTime.Now)));

    [HttpGet("bake/search")]
    [MenuGate("/work/bake")]
    public async Task<ActionResult<BakeSearchDto>> SearchBake([FromQuery] string? q, [FromQuery] bool issues = false)
        => Ok(await _svc.SearchBakeAsync(q, issues));

    [HttpPut("bake")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/bake")]
    public async Task<ActionResult<BakeLogDto?>> SaveBake([FromBody] BakeSaveRequest req) => Ok(await _svc.SaveBakeAsync(req, Actor));

    [HttpDelete("bake/round")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/bake")]
    public async Task<ActionResult<int>> DeleteBakeRound([FromQuery] DateOnly date, [FromQuery] string shift, [FromQuery] int round)
        => Ok(await _svc.DeleteBakeRoundAsync(date, shift, round));

    [HttpPost("bake/import")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/bake")]
    public async Task<ActionResult<BakeImportResultDto>> ImportBake([FromBody] BakeImportRequest req)
        => Ok(await _svc.ImportBakeAsync(req.Rows, req.Overwrite, Actor));

    // ── 폐기품 관리 ──

    [HttpGet("scrap/batches")]
    [MenuGate("/work/scrap")]
    public async Task<ActionResult<IReadOnlyList<ScrapBatchSummaryDto>>> ScrapBatches() => Ok(await _svc.GetScrapBatchesAsync());

    [HttpGet("scrap/batches/{id:int}")]
    [MenuGate("/work/scrap")]
    public async Task<ActionResult<ScrapBatchDto>> ScrapBatch(int id) => Ok(await _svc.GetScrapBatchAsync(id));

    [HttpPost("scrap/batches")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/scrap")]
    public async Task<ActionResult<ScrapBatchDto>> CreateScrapBatch([FromBody] ScrapBatchSaveRequest req)
        => Ok(await _svc.SaveScrapBatchAsync(0, req, Actor));

    [HttpPut("scrap/batches/{id:int}")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/scrap")]
    public async Task<ActionResult<ScrapBatchDto>> SaveScrapBatch(int id, [FromBody] ScrapBatchSaveRequest req)
        => Ok(await _svc.SaveScrapBatchAsync(id, req, Actor));

    [HttpDelete("scrap/batches/{id:int}")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/scrap")]
    public async Task<IActionResult> DeleteScrapBatch(int id) { await _svc.DeleteScrapBatchAsync(id); return NoContent(); }

    [HttpPost("scrap/batches/{id:int}/items")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/scrap")]
    public async Task<ActionResult<ScrapBatchDto>> AddScrapItems(int id, [FromBody] ScrapItemsAddRequest req)
        => Ok(await _svc.AddScrapItemsAsync(id, req.Items, Actor));

    [HttpPut("scrap/items/{id:int}")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/scrap")]
    public async Task<ActionResult<ScrapItemDto>> SaveScrapItem(int id, [FromBody] ScrapItemSaveRequest req)
        => Ok(await _svc.SaveScrapItemAsync(id, req, Actor));

    [HttpDelete("scrap/items/{id:int}")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/scrap")]
    public async Task<IActionResult> DeleteScrapItem(int id) { await _svc.DeleteScrapItemAsync(id); return NoContent(); }

    [HttpGet("scrap/materials")]
    [MenuGate("/work/scrap")]
    public async Task<ActionResult<IReadOnlyList<ScrapMaterialDto>>> ScrapMaterials() => Ok(await _svc.GetScrapMaterialsAsync());

    [HttpGet("scrap/search")]
    [MenuGate("/work/scrap")]
    public async Task<ActionResult<ScrapSearchDto>> SearchScrap([FromQuery] string? q) => Ok(await _svc.SearchScrapAsync(q));

    [HttpGet("scrap/tags")]
    [MenuGate("/work/scrap")]
    public async Task<ActionResult<IReadOnlyList<ScrapTagDto>>> ScrapTags() => Ok(await _svc.GetScrapTagsAsync());

    [HttpPost("scrap/tags")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/scrap")]
    public async Task<ActionResult<ScrapTagDto>> CreateScrapTag([FromBody] ScrapTagSaveRequest req) => Ok(await _svc.SaveScrapTagAsync(0, req, Actor));

    [HttpPut("scrap/tags/{id:int}")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/scrap")]
    public async Task<ActionResult<ScrapTagDto>> SaveScrapTag(int id, [FromBody] ScrapTagSaveRequest req) => Ok(await _svc.SaveScrapTagAsync(id, req, Actor));

    [HttpDelete("scrap/tags/{id:int}")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/scrap")]
    public async Task<IActionResult> DeleteScrapTag(int id) { await _svc.DeleteScrapTagAsync(id); return NoContent(); }

    [HttpGet("scrap/circles")]
    [MenuGate("/work/scrap")]
    public async Task<ActionResult<IReadOnlyList<ScrapCircleDto>>> ScrapCircles() => Ok(await _svc.GetScrapCirclesAsync());

    [HttpPut("scrap/circles")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/scrap")]
    public async Task<ActionResult<IReadOnlyList<ScrapCircleDto>>> SaveScrapCircles([FromBody] ScrapCirclesSaveRequest req)
        => Ok(await _svc.SaveScrapCirclesAsync(req.Items));

    [HttpPost("scrap/import")]
    [Authorize(Policy = "EditOffice")]
    [MenuGate("/work/scrap")]
    public async Task<ActionResult<ScrapImportResultDto>> ImportScrap([FromBody] ScrapImportRequest req)
        => Ok(await _svc.ImportScrapAsync(req, Actor));

    // ── 업무보고(세정/BAKE) ──

    [HttpGet("report")]
    [MenuGate("/work/report")]
    public async Task<ActionResult<WorkReportDto>> Report([FromQuery] DateOnly? date)
        => Ok(await _svc.GetReportAsync(date ?? DateOnly.FromDateTime(DateTime.Now)));
}
