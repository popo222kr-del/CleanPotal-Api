namespace CleanPotal.Core.DTOs;

public record ProductionMeetingDto(
    int Id,
    string Title,
    DateOnly MeetingDate,
    string DayContent,
    string NightContent,
    string OfficeMemo,
    string DayTeam,      // 해당 날짜 주간 팀 (예측)
    string NightTeam,    // 해당 날짜 야간 팀 (예측)
    string CreatorName,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record ProductionMeetingUpsertRequest(
    string Title,
    DateOnly MeetingDate,
    string DayContent,
    string NightContent,
    string OfficeMemo
);

/// <summary>월별 그룹 (좌측 목록용).</summary>
public record ProductionMeetingGroupDto(string MonthTitle, IReadOnlyList<ProductionMeetingDto> Reports);
