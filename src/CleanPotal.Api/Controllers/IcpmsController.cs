using System.Security.Claims;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>설비 ICP-MS API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class IcpmsController : ControllerBase
{
    private readonly IIcpmsService _svc;
    public IcpmsController(IIcpmsService svc) => _svc = svc;

    private string User_ => User.FindFirst(ClaimTypes.Name)?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "?";
    private bool IsMaster => User.IsInRole("admin");
    private static string[] Csv(string? s) => string.IsNullOrWhiteSpace(s)
        ? Array.Empty<string>() : s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [HttpGet("equipment")]
    public async Task<ActionResult<IReadOnlyList<EquipmentDto>>> Equipment() => Ok(await _svc.GetEquipmentAsync());

    [HttpGet("filters")]
    public async Task<ActionResult<IcpmsFiltersDto>> Filters([FromQuery] string? processTypes)
        => Ok(await _svc.GetFiltersAsync(Csv(processTypes)));

    [HttpGet("measurements")]
    public async Task<ActionResult<IReadOnlyList<MeasurementDto>>> Measurements(
        [FromQuery] string? processTypes, [FromQuery] string? baths, [FromQuery] string? eqIds, [FromQuery] string? dates)
        => Ok(await _svc.GetMeasurementsAsync(Csv(processTypes), Csv(baths), Csv(eqIds), Csv(dates)));

    [HttpGet("comparison")]
    public async Task<ActionResult<object>> Comparison(
        [FromQuery] string? processTypes, [FromQuery] string? baths, [FromQuery] string? eqIds, [FromQuery] string? dates)
    {
        var list = await _svc.GetComparisonAsync(Csv(processTypes), Csv(baths), Csv(eqIds), Csv(dates));
        return Ok(list.Select(x => new { eqId = x.EqId, process = x.Process, values = x.Values }));
    }

    [HttpGet("summary")]
    public async Task<ActionResult<IcpmsSummaryDto>> Summary([FromQuery] string? dates, [FromQuery] string? elements)
        => Ok(await _svc.GetSummaryAsync(Csv(dates), Csv(elements)));

    [HttpPost("measurements/bulk")]
    public async Task<ActionResult<MeasurementBulkResult>> Bulk([FromBody] MeasurementBulkRequest req)
        => Ok(await _svc.BulkInsertAsync(req.Rows, User_));

    [HttpPut("equipment/{eqId}")]
    public async Task<ActionResult<EquipmentDto>> UpdateEquipment(string eqId, [FromBody] EquipmentUpsertRequest req)
    {
        try
        {
            var dto = await _svc.UpdateEquipmentAsync(eqId, req.NewEqId, req.Process, User_);
            return dto is null ? NotFound() : Ok(dto);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("equipment")]
    public async Task<ActionResult<EquipmentDto>> AddEquipment([FromBody] EquipmentAddRequest req)
    {
        try { return Ok(await _svc.AddEquipmentAsync(req.EqId, User_)); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("checknotes")]
    public async Task<ActionResult<IReadOnlyList<CheckNoteItemDto>>> CheckNotes([FromQuery] string date)
        => Ok(await _svc.GetCheckNotesAsync(date));

    [HttpPut("checknotes")]
    public async Task<IActionResult> SaveCheckNote([FromBody] CheckNoteSaveRequest req)
    {
        await _svc.SaveCheckNoteAsync(req.EqId, req.Date, req.Note, User_);
        return NoContent();
    }

    [HttpGet("checknotes/history")]
    public async Task<ActionResult<IReadOnlyList<CheckNoteHistoryDto>>> History([FromQuery] string eqId)
        => Ok(await _svc.GetCheckNoteHistoryAsync(eqId));

    // ── 마스터 전용 ──
    [HttpDelete("measurements")]
    public async Task<ActionResult<object>> DeleteAll()
    {
        if (!IsMaster) return Forbid();
        return Ok(new { deleted = await _svc.DeleteAllAsync(User_) });
    }

    [HttpGet("actionlog")]
    public async Task<ActionResult<IReadOnlyList<ActionLogDto>>> ActionLog()
    {
        if (!IsMaster) return Forbid();
        return Ok(await _svc.GetActionLogAsync());
    }
}
