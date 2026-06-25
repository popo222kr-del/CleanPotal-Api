using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 근무 일정 API. 웹(React/Vue)과 모바일(MAUI)이 동일하게 호출한다.
/// 순수 JSON만 반환 (SSR 없음).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ScheduleController : ControllerBase
{
    private readonly IScheduleService _schedule;
    public ScheduleController(IScheduleService schedule) => _schedule = schedule;

    /// <summary>
    /// 월간 근무표 조회.
    /// GET /api/schedule/roster?year=2026&month=6&team=전체&predict=false
    /// </summary>
    [HttpGet("roster")]
    public async Task<ActionResult<RosterMonthDto>> GetRoster(
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] string team = "전체",
        [FromQuery] bool predict = false)
    {
        var now = DateTime.Today;
        var dto = await _schedule.GetRosterAsync(year ?? now.Year, month ?? now.Month, team, predict);
        return Ok(dto);
    }

    /// <summary>
    /// 근무 도장 일괄 적용 (또는 비우기).
    /// POST /api/schedule/stamp  { members:[...], startDate:"2026-06-10", shiftType:"야간", days:3, clear:false }
    /// </summary>
    [HttpPost("stamp")]
    public async Task<ActionResult<IReadOnlyList<StampedCellDto>>> Stamp([FromBody] StampShiftRequest req)
    {
        if (req.Members is null || req.Members.Count == 0)
            return BadRequest(new { error = "대상자를 선택하세요." });

        var cells = await _schedule.StampAsync(req, Actor);
        return Ok(cells);
    }

    private string Actor => User.Identity?.Name ?? "system";

    // ── 팀 일정 ──

    /// <summary>월간 팀 일정 조회. GET /api/schedule/events?year=2026&month=6</summary>
    [HttpGet("events")]
    public async Task<ActionResult<IReadOnlyList<TeamEventDto>>> GetEvents(
        [FromQuery] int? year, [FromQuery] int? month)
    {
        var now = DateTime.Today;
        return Ok(await _schedule.GetTeamEventsAsync(year ?? now.Year, month ?? now.Month));
    }

    [HttpPost("events")]
    [Authorize(Policy = "CanManageSchedule")]
    public async Task<ActionResult<TeamEventDto>> AddEvent([FromBody] TeamEventRequest req)
        => Ok(await _schedule.AddTeamEventAsync(req, Actor));

    [HttpPut("events/{id:int}")]
    [Authorize(Policy = "CanManageSchedule")]
    public async Task<ActionResult<TeamEventDto>> UpdateEvent(int id, [FromBody] TeamEventRequest req)
    {
        var dto = await _schedule.UpdateTeamEventAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("events/{id:int}")]
    [Authorize(Policy = "CanManageSchedule")]
    public async Task<IActionResult> DeleteEvent(int id)
        => await _schedule.DeleteTeamEventAsync(id) ? NoContent() : NotFound();
}
