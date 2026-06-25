namespace CleanPotal.Core.DTOs;

/// <summary>월간 근무표 응답 (웹/모바일 공통).</summary>
public record RosterMonthDto(
    int Year,
    int Month,
    IReadOnlyList<RosterDayHeaderDto> Days,
    IReadOnlyList<RosterTeamDto> Teams
);

public record RosterDayHeaderDto(int Day, string DayOfWeek, bool IsWeekend, bool IsHoliday);

public record RosterTeamDto(
    string Team,
    IReadOnlyList<RosterMemberDto> Members,
    IReadOnlyList<int> DailyCounts,
    int GrandTotal
);

public record RosterMemberDto(
    string Name,
    string JobTitle,
    IReadOnlyList<RosterCellDto> Cells,
    int TotalWorkDays
);

public record RosterCellDto(
    DateOnly Date,
    string ShiftType,   // 빈 문자열 = 미지정
    bool IsPredicted
);

/// <summary>근무표 도장 요청 (여러 인원 × 연속 일수 일괄 적용).</summary>
public record StampShiftRequest(
    IReadOnlyList<string> Members,
    DateOnly StartDate,
    string ShiftType,
    int Days = 1,
    bool Clear = false
);

public record StampedCellDto(string Name, DateOnly Date, string ShiftType);
