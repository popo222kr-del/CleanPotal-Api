using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>BROKEN(파손/불량) 관리 API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BrokenController : ControllerBase
{
    private readonly IBrokenService _svc;
    public BrokenController(IBrokenService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BrokenRecordDto>>> GetAll(
        [FromQuery] int? year, [FromQuery] string? team, [FromQuery] string? productType,
        [FromQuery] string? official, [FromQuery] string? search)
        => Ok(await _svc.GetAllAsync(year, team, productType, official, search));

    [HttpGet("filters")]
    public async Task<ActionResult<BrokenFilterOptionsDto>> Filters()
        => Ok(await _svc.GetFilterOptionsAsync());

    [HttpPost]
    public async Task<ActionResult<BrokenRecordDto>> Create([FromBody] BrokenUpsertRequest req)
        => Ok(await _svc.CreateAsync(req));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<BrokenRecordDto>> Update(int id, [FromBody] BrokenUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();
}
