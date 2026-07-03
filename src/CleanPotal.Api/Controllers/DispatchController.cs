using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>배차 관리 API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DispatchController : ControllerBase
{
    private readonly IDispatchService _svc;
    public DispatchController(IDispatchService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DispatchDto>>> GetAll([FromQuery] string? search)
        => Ok(await _svc.GetAllAsync(search));

    [HttpPost]
    public async Task<ActionResult<DispatchDto>> Create([FromBody] DispatchUpsertRequest req)
        => Ok(await _svc.CreateAsync(req));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<DispatchDto>> Update(int id, [FromBody] DispatchUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();
}
