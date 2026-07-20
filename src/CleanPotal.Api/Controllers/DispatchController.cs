using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>배차 관리 API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ViewHandover")]
public class DispatchController : ControllerBase
{
    private readonly IDispatchService _svc;
    public DispatchController(IDispatchService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DispatchDto>>> GetAll([FromQuery] string? search)
        => Ok(await _svc.GetAllAsync(search));

    /// <summary>날짜별 배차표 조회. GET /api/dispatch/day?date=2026-07-14</summary>
    [HttpGet("day")]
    public async Task<ActionResult<IReadOnlyList<DispatchDto>>> GetDay([FromQuery] DateOnly date)
        => Ok(await _svc.GetByDateAsync(date));

    /// <summary>날짜별 배차표 일괄 저장(추가/수정/삭제 동기화). PUT /api/dispatch/day?date=...</summary>
    [Authorize(Policy = "EditHandover")]
    [HttpPut("day")]
    public async Task<ActionResult<IReadOnlyList<DispatchDto>>> SaveDay([FromQuery] DateOnly date, [FromBody] DispatchDayRequest req)
        => Ok(await _svc.SaveDayAsync(date, req.Rows));

    /// <summary>배차 항목 이월(다른 날짜로 이동). PATCH /api/dispatch/{id}/move</summary>
    [Authorize(Policy = "EditHandover")]
    [HttpPatch("{id:int}/move")]
    public async Task<ActionResult<DispatchDto>> Move(int id, [FromBody] DispatchMoveRequest req)
    {
        var dto = await _svc.MoveAsync(id, req.TargetDate);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditHandover")]
    [HttpPost]
    public async Task<ActionResult<DispatchDto>> Create([FromBody] DispatchUpsertRequest req)
        => Ok(await _svc.CreateAsync(req));

    [Authorize(Policy = "EditHandover")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<DispatchDto>> Update(int id, [FromBody] DispatchUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditHandover")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();
}
