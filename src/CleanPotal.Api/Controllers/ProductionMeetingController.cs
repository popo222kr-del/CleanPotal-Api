using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>생산 미팅 기록 API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ViewHandover")]
public class ProductionMeetingController : ControllerBase
{
    private readonly IProductionMeetingService _svc;
    public ProductionMeetingController(IProductionMeetingService svc) => _svc = svc;

    private string Actor => User.Identity?.Name ?? "system";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductionMeetingGroupDto>>> GetGrouped()
        => Ok(await _svc.GetGroupedAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductionMeetingDto>> Get(int id)
    {
        var dto = await _svc.GetAsync(id);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditHandover")]
    [HttpPost]
    public async Task<ActionResult<ProductionMeetingDto>> Create([FromBody] ProductionMeetingUpsertRequest req)
        => Ok(await _svc.CreateAsync(req, Actor));

    [Authorize(Policy = "EditHandover")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProductionMeetingDto>> Update(int id, [FromBody] ProductionMeetingUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditHandover")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();
}
