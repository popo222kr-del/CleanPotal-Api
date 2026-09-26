namespace CleanPotal.Core.DTOs;

/// <summary>
/// 대시보드 요약 한 번에 — 카드마다 따로 부르지 않게. 볼 권한이 없거나 숨긴 메뉴의 카드는 null 로 온다.
/// Alerts 는 "이상 알림" 한 줄(없으면 비어 있다).
/// </summary>
public record PortalDashboardDto(
    IReadOnlyList<DashAlertDto> Alerts,
    DashChecklistDto? Checklist,
    DashHandoverDto? Handover,
    DashHandoverDto? Weekly,
    DashProdReqDto? ProdReq,
    DashDispatchDto? Dispatch,
    DashBrokenDto? Broken,
    DateTime At);

/// <param name="Level">bad(빨강) | warn(주황)</param>
/// <param name="Link">누르면 갈 화면</param>
public record DashAlertDto(string Level, string Text, string Link);

/// <summary>체크시트 — 지금 교대 제출 구역 수 등.</summary>
public record DashChecklistDto(
    DateOnly WorkDate, string Shift, int Submitted, int InProgress, int Zones, int OpenNg, int WeeklyOverdue, int WeeklyDueToday);

/// <summary>기타세정 현황 또는 주간세정 현황(같은 모양) — 진행·포장 중인 건. 둘은 업체 마스터의 주간세정 표시로 나뉜다.</summary>
public record DashHandoverDto(int Open, int DueToday, int DueTomorrow, int Overdue);

/// <summary>생산팀 요청사항 — 진행 중, 마감 지남, 내가 아직 안 본 건.</summary>
public record DashProdReqDto(int Open, int Overdue, int Unread);

/// <summary>오늘 배차 — 건수와 업체(앞 몇 곳).</summary>
public record DashDispatchDto(int Count, IReadOnlyList<string> Vendors);

/// <summary>
/// BROKEN — 이번 달·올해(발생일 기준) 건수, 올해 공식 건수, 아직 완료되지 않은(접수·조치중) 건수, 가장 최근 건.
/// </summary>
public record DashBrokenDto(int ThisMonth, int ThisYear, int OfficialThisYear, int Open, DashBrokenRecentDto? Recent);

public record DashBrokenRecentDto(DateOnly? OccurDate, string Line, string ProductName, string Status);
