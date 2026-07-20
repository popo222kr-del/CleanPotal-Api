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
    [Authorize(Policy = "ViewRoster")]
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
    [Authorize(Policy = "EditRoster")]
    [HttpPost("stamp")]
    public async Task<ActionResult<IReadOnlyList<StampedCellDto>>> Stamp([FromBody] StampShiftRequest req)
    {
        if (req.Members is null || req.Members.Count == 0)
            return BadRequest(new { error = "대상자를 선택하세요." });

        var cells = await _schedule.StampAsync(req, Actor);
        return Ok(cells);
    }

    private string Actor => User.Identity?.Name ?? "system";

    /// <summary>월간 달력. GET /api/schedule/calendar?year=2026&month=6&predict=true</summary>
    [Authorize(Policy = "ViewSchedule")]
    [HttpGet("calendar")]
    public async Task<ActionResult<CalendarMonthDto>> GetCalendar(
        [FromQuery] int? year, [FromQuery] int? month, [FromQuery] bool predict = true)
    {
        var now = DateTime.Today;
        return Ok(await _schedule.GetCalendarAsync(year ?? now.Year, month ?? now.Month, predict));
    }

    /// <summary>인수인계 대시보드 오늘 현황. GET /api/schedule/today-status</summary>
    [HttpGet("today-status")]
    public async Task<ActionResult<TodayStatusDto>> GetTodayStatus()
        => Ok(await _schedule.GetTodayStatusAsync());

    /// <summary>특정 날짜의 주간/야간 근무 팀. GET /api/schedule/shift-teams?date=2026-07-14</summary>
    [HttpGet("shift-teams")]
    public async Task<ActionResult<ShiftTeamsDto>> GetShiftTeams([FromQuery] DateOnly date)
        => Ok(await _schedule.GetShiftTeamsAsync(date));

    // ── 팀 일정 ──

    /// <summary>월간 팀 일정 조회. GET /api/schedule/events?year=2026&month=6</summary>
    [Authorize(Policy = "ViewSchedule")]
    [HttpGet("events")]
    public async Task<ActionResult<IReadOnlyList<TeamEventDto>>> GetEvents(
        [FromQuery] int? year, [FromQuery] int? month)
    {
        var now = DateTime.Today;
        return Ok(await _schedule.GetTeamEventsAsync(year ?? now.Year, month ?? now.Month));
    }

    [HttpPost("events")]
    [Authorize(Policy = "EditSchedule")]
    public async Task<ActionResult<TeamEventDto>> AddEvent([FromBody] TeamEventRequest req)
        => Ok(await _schedule.AddTeamEventAsync(req, Actor));

    [HttpPut("events/{id:int}")]
    [Authorize(Policy = "EditSchedule")]
    public async Task<ActionResult<TeamEventDto>> UpdateEvent(int id, [FromBody] TeamEventRequest req)
    {
        var dto = await _schedule.UpdateTeamEventAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("events/{id:int}")]
    [Authorize(Policy = "EditSchedule")]
    public async Task<IActionResult> DeleteEvent(int id)
        => await _schedule.DeleteTeamEventAsync(id) ? NoContent() : NotFound();
}
