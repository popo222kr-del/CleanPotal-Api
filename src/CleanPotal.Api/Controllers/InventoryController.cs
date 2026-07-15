using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>현장 재고관리 API (WPF FieldInventory).</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _svc;
    public InventoryController(IInventoryService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InventoryZoneDto>>> GetByZone([FromQuery] string? search)
        => Ok(await _svc.GetByZoneAsync(search));

    [HttpGet("locations")]
    public async Task<ActionResult<IReadOnlyList<string>>> Locations()
        => Ok(await _svc.GetLocationsAsync());

    [HttpPost]
    public async Task<ActionResult<InventoryItemDto>> Create([FromBody] InventoryUpsertRequest req)
        => Ok(await _svc.CreateAsync(req));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<InventoryItemDto>> Update(int id, [FromBody] InventoryUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPatch("{id:int}/ordered")]
    public async Task<ActionResult<InventoryItemDto>> SetOrdered(int id, [FromBody] InventoryOrderedRequest req)
    {
        var dto = await _svc.SetOrderedAsync(id, req.IsOrdered);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();

    /// <summary>주간 마감 — 현재 재고를 스냅샷으로 저장.</summary>
    [HttpPost("snapshot")]
    public async Task<ActionResult<object>> Snapshot([FromBody] InventorySnapshotRequest req)
        => Ok(new { count = await _svc.CreateSnapshotAsync(req.Date) });

    /// <summary>엑셀 실사 확정 — 스냅샷 후 스테이징 재고 반영(증감 소비 반영).</summary>
    [HttpPost("import/confirm")]
    public async Task<ActionResult<object>> ConfirmImport([FromBody] InventoryImportConfirmRequest req)
        => Ok(new { count = await _svc.ConfirmImportAsync(req.Items) });
}
