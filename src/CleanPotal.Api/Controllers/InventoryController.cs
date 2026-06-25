using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>현장 재고관리 API.</summary>
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

    [HttpPatch("{id:int}/stock")]
    public async Task<ActionResult<InventoryItemDto>> SetStock(int id, [FromBody] InventoryStockRequest req)
    {
        var dto = await _svc.SetStockAsync(id, req.CurrentStock);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();
}
