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

    // ── 팀 일정 ──
    Task<IReadOnlyList<TeamEventDto>> GetTeamEventsAsync(int year, int month);
    Task<TeamEventDto> AddTeamEventAsync(TeamEventRequest req, string actor);
    Task<TeamEventDto?> UpdateTeamEventAsync(int id, TeamEventRequest req);
    Task<bool> DeleteTeamEventAsync(int id);
}
