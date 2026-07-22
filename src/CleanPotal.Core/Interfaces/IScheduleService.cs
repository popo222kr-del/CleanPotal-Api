using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

/// <summary>
/// 근무 일정 비즈니스 로직 추상화.
/// 웹/모바일 컨트롤러는 이 인터페이스에만 의존한다 (DI로 주입).
/// </summary>
public interface IScheduleService
{
    /// <summary>월간 근무표(팀/직위/인원 그리드) 조회. predict=true면 빈 칸을 교대 예측으로 채운다.</summary>
    Task<RosterMonthDto> GetRosterAsync(int year, int month, string teamFilter, bool predict);

    /// <summary>근무 도장 일괄 적용 (또는 clear=true 시 비우기).</summary>
    Task<IReadOnlyList<StampedCellDto>> StampAsync(StampShiftRequest request, string actorName);

    /// <summary>근태/휴가 기간 등록 — 주말·공휴일 제외하고 반영, 등록된 일수 반환.</summary>
    Task<int> RegisterAttendanceAsync(AttendanceRequest request, string actorName);

    /// <summary>근태 등록용 재직 직원 목록 (팀 순서: Office→주간팀→장팀→김팀).</summary>
    Task<IReadOnlyList<ScheduleMemberDto>> GetMembersAsync();

    /// <summary>해당 연도 공휴일 날짜 목록 (yyyy-MM-dd) — 등록 미리보기용.</summary>
    IReadOnlyList<string> GetHolidays(int year);

    /// <summary>월간 달력 — 일별 주/야/휴무 인원 + 팀 일정 + 공휴일.</summary>
    Task<CalendarMonthDto> GetCalendarAsync(int year, int month, bool predict);

    /// <summary>인수인계 대시보드 — 오늘 팀별 근무 현황 + 다가오는 팀 일정/교육.</summary>
    Task<TodayStatusDto> GetTodayStatusAsync();

    /// <summary>특정 날짜의 주간/야간 근무 팀 (생산미팅 '주간 (김팀)' 라벨용).</summary>
    Task<ShiftTeamsDto> GetShiftTeamsAsync(DateOnly date);

    // ── 팀 일정 ──
    Task<IReadOnlyList<TeamEventDto>> GetTeamEventsAsync(int year, int month);
    Task<TeamEventDto> AddTeamEventAsync(TeamEventRequest req, string actor);
    Task<TeamEventDto?> UpdateTeamEventAsync(int id, TeamEventRequest req);
    Task<bool> DeleteTeamEventAsync(int id);
}
