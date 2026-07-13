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

// ── 월간 달력 ──
public record CalendarMonthDto(int Year, int Month, IReadOnlyList<CalendarDayDto> Days);

public record CalendarDayDto(
    DateOnly Date,
    int Day,
    string DayOfWeek,
    bool IsWeekend,
    string Holiday,                       // 공휴일명 (없으면 "")
    IReadOnlyList<string> DayShift,       // 주간 인원
    IReadOnlyList<string> NightShift,     // 야간 인원
    IReadOnlyList<string> OffShift,       // 휴무/연차/반차 인원
    IReadOnlyList<CalendarBadgeDto> Badges,   // WPF 스타일 셀 뱃지
    IReadOnlyList<TeamEventDto> Events
);

/// <summary>달력 셀 뱃지 (주간/야간/주간휴무/야간휴무/교육 등).</summary>
public record CalendarBadgeDto(string Text, string Kind, IReadOnlyList<string> Names);

// ── 오늘의 세정팀 현황 (인수인계 대시보드) ──
public record TeamTodayDto(string Team, IReadOnlyList<CalendarBadgeDto> Badges);

public record UpcomingEduDto(
    string MemberName, string CourseName, DateOnly? StartDate, DateOnly? EndDate, string EduMethod);

/// <summary>인수인계 대시보드용 오늘 현황 묶음.</summary>
public record TodayStatusDto(
    DateOnly Date,
    IReadOnlyList<TeamTodayDto> Teams,           // 김팀/장팀/주간팀/Office 오늘 주·야·휴무·교육
    IReadOnlyList<TeamEventDto> UpcomingEvents,   // 오늘 이후 팀 일정 (D-day용)
    IReadOnlyList<UpcomingEduDto> UpcomingEdu     // D-7 이내 교육 일정
);
