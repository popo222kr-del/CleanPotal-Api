using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>현장 점검 체크시트 API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChecklistController : ControllerBase
{
    private readonly IChecklistService _svc;
    public ChecklistController(IChecklistService svc) => _svc = svc;

    private string Worker => User.Identity?.Name ?? "system";

    [HttpGet("zones")]
    public ActionResult<IReadOnlyList<ZoneDto>> GetZones() => Ok(_svc.GetZones());

    [HttpGet("items")]
    public async Task<ActionResult<IReadOnlyList<InspectionItemDto>>> GetItems([FromQuery] string zone)
        => Ok(await _svc.GetItemsAsync(zone));

    [HttpPost("items")]
    [Authorize(Policy = "CanManageSchedule")]
    public async Task<ActionResult<InspectionItemDto>> AddItem([FromBody] InspectionItemRequest req)
        => Ok(await _svc.AddItemAsync(req));

    [HttpDelete("items/{id:int}")]
    [Authorize(Policy = "CanManageSchedule")]
    public async Task<IActionResult> DeleteItem(int id)
        => await _svc.DeleteItemAsync(id) ? NoContent() : NotFound();

    [HttpPost("submit")]
    public async Task<ActionResult<InspectionRecordDto>> Submit([FromBody] SubmitInspectionRequest req)
        => Ok(await _svc.SubmitAsync(req, Worker));

    [HttpGet("records")]
    public async Task<ActionResult<IReadOnlyList<InspectionRecordDto>>> GetRecords(
        [FromQuery] DateOnly? date, [FromQuery] string? zone)
        => Ok(await _svc.GetRecordsAsync(date ?? DateOnly.FromDateTime(DateTime.Today), zone));
}
