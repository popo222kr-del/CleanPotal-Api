namespace CleanPotal.Core.DTOs;

/// <summary>
/// 대시보드 요약 한 번에 — 카드마다 따로 부르지 않게. 볼 권한이 없거나 숨긴 메뉴의 카드는 null 로 온다.
/// Alerts 는 맨 위 "이상 알림" 띠(없으면 비어 있다).
/// </summary>
public record PortalDashboardDto(
    IReadOnlyList<DashAlertDto> Alerts,
    DashChecklistDto? Checklist,
    DashHandoverDto? Handover,
    DashProdReqDto? ProdReq,
    DateTime At,
    DashMesDto? Mes = null,
    DashDispatchDto? Dispatch = null,
    DashIcpmsDto? Icpms = null,
    DashReportsDto? Reports = null,
    DashHandoverDto? Weekly = null,
    DashInventoryDto? Inventory = null);

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

/// <summary>
/// 재고 — 현재 재고가 안전재고 이하인 품목(재고관리 화면의 "재고 부족"과 같은 기준).
/// 발주 완료 표시한 품목은 따로 센다. Names 는 발주 전 부족 품목(앞 몇 개).
/// </summary>
public record DashInventoryDto(int Low, int LowNotOrdered, int LowOrdered, IReadOnlyList<string> Names);

public record DashMesStageDto(string Name, int Count, bool IsBottleneck);

/// <summary>MES — 재공·오늘 입고/출하·보류·장기 대기와 공정별 재공(전산등록·출하 완료 제외).</summary>
public record DashMesDto(int InProgress, int TodayReceived, int TodayShipped, int Hold, int Rework, int ShippingWaiting, int LongWait,
    IReadOnlyList<DashMesStageDto> Stages);

/// <summary>오늘 배차 — 건수와 업체(앞 몇 곳).</summary>
public record DashDispatchDto(int Count, IReadOnlyList<string> Vendors);

/// <summary>ICP-MS — 가장 최근 측정일 기준 측정 설비 수와 최고값.</summary>
public record DashIcpmsDto(string LatestDate, int Measured, int Total, double MaxValue, string MaxEqId, string MaxElement, string Unit);

/// <summary>
/// 작성 여부 — 오늘 생산팀 인수인계(생산미팅), 이번 주(월~일) 주간보고. 볼 권한이 없는 쪽은 Visible=false.
/// </summary>
public record DashReportsDto(
    bool MeetingVisible, bool MeetingToday, string MeetingBy, DateTime? MeetingAt,
    bool WeeklyVisible, bool WeeklyThisWeek, string WeeklyBy, DateTime? WeeklyAt);
