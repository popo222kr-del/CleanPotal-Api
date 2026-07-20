using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>인수인계 현황 API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ViewHandover")]
public class HandoverController : ControllerBase
{
    private readonly IHandoverService _svc;
    public HandoverController(IHandoverService svc) => _svc = svc;

    private string Actor => User.Identity?.Name ?? "system";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<HandoverDto>>> GetAll(
        [FromQuery] string? status, [FromQuery] string? category, [FromQuery] string? search, [FromQuery] bool weekly = false)
        => Ok(await _svc.GetAllAsync(status, category, search, weekly, Actor));

    [HttpGet("counts")]
    public async Task<ActionResult<IReadOnlyDictionary<string, int>>> Counts([FromQuery] bool weekly = false)
        => Ok(await _svc.GetStatusCountsAsync(weekly));

    [Authorize(Policy = "EditHandover")]
    [HttpPost]
    public async Task<ActionResult<HandoverDto>> Create([FromBody] HandoverUpsertRequest req)
        => Ok(await _svc.CreateAsync(req, Actor));

    [Authorize(Policy = "EditHandover")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<HandoverDto>> Update(int id, [FromBody] HandoverUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req, Actor);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditHandover")]
    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<HandoverDto>> ChangeStatus(int id, [FromBody] HandoverStatusRequest req)
    {
        var dto = await _svc.ChangeStatusAsync(id, req.Status, Actor);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditHandover")]
    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
        => await _svc.MarkReadAsync(id, Actor) ? NoContent() : NotFound();

    [Authorize(Policy = "EditHandover")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();
}
