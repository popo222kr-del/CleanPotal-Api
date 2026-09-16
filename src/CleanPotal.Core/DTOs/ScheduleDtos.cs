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

/// <summary>근태/휴가 기간 등록 요청 (WPF 일정 등록 창 — 주말·공휴일 자동 제외).</summary>
public record AttendanceRequest(string MemberName, DateOnly StartDate, DateOnly EndDate, string ShiftType);

/// <summary>근태 등록용 직원 목록 항목.</summary>
/// <summary>근태 등록 대상 직원. 인원이 많아 부서·팀으로 걸러 찾을 수 있게 둘 다 싣는다.</summary>
public record ScheduleMemberDto(string RealName, string TeamName, string Department);

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

/// <summary>달력에서 쓰는 부서 한 개. 색·약칭은 서버가 정해 내려준다(화면마다 달라지지 않게).</summary>
public record CalendarDeptDto(int Id, string Name, string ShortName, string Color);

/// <summary>달력 셀 뱃지 (주간/야간/주간휴무/야간휴무/교육 등).</summary>
public record CalendarBadgeDto(string Text, string Kind, IReadOnlyList<string> Names);

// ── 오늘의 세정팀 현황 (인수인계 대시보드) ──
/// <summary>현황 한 줄. Team 은 교대 생산팀이면 팀 이름, 그 외에는 조직도에 등록된 부서 이름.
/// <c>Division</c>: 소속 본부(사업본부). 지정하지 않았으면 빈 문자열 — 화면에서 묶음 제목으로 쓴다.
/// <c>Production</c>: 교대 생산팀이면 true(주/야 예측 대상), 부서 줄이면 false.</summary>
public record TeamTodayDto(string Team, IReadOnlyList<CalendarBadgeDto> Badges, string Division, bool Production);

public record UpcomingEduDto(
    string MemberName, string CourseName, DateOnly? StartDate, DateOnly? EndDate, string EduMethod);

/// <summary>인수인계 대시보드용 오늘 현황 묶음.</summary>
public record TodayStatusDto(
    DateOnly Date,
    IReadOnlyList<TeamTodayDto> Teams,           // 교대 생산팀 + 등록 부서별 오늘 주·야·휴무·교육
    IReadOnlyList<TeamEventDto> UpcomingEvents,   // 오늘 이후 팀 일정 (D-day용)
    IReadOnlyList<UpcomingEduDto> UpcomingEdu     // D-7 이내 교육 일정
);

/// <summary>특정 날짜의 주간/야간 근무 팀 (생산미팅 라벨용 — WPF UpdateShiftTeamLabels).</summary>
public record ShiftTeamsDto(IReadOnlyList<string> DayTeams, IReadOnlyList<string> NightTeams);
