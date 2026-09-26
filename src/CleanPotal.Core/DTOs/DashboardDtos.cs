namespace CleanPotal.Core.DTOs;

/// <summary>
/// 대시보드 요약 한 번에 — 카드마다 따로 부르지 않게. 볼 권한이 없거나 숨긴 메뉴의 카드는 null 로 온다.
/// Alerts 는 맨 위 "이상 알림" 띠(없으면 비어 있다).
/// </summary>
public record DashboardSummaryDto(
    IReadOnlyList<DashAlertDto> Alerts,
    DashChecklistDto? Checklist,
    DashSensorsDto? Sensors,
    DashHandoverDto? Handover,
    DashProdReqDto? ProdReq,
    DateTime At);

/// <param name="Level">bad(빨강) | warn(주황)</param>
/// <param name="Link">누르면 갈 화면</param>
public record DashAlertDto(string Level, string Text, string Link);

/// <summary>체크시트 — 지금 교대 제출 구역 수 등.</summary>
public record DashChecklistDto(
    DateOnly WorkDate, string Shift, int Submitted, int InProgress, int Zones, int OpenNg, int WeeklyOverdue, int WeeklyDueToday);

public record DashSensorDto(string Name, double? Temperature, double? Humidity, string Status, string? Reason);

/// <param name="Collecting">MQTT·Zigbee2MQTT 가 모두 살아 있는가</param>
public record DashSensorsDto(bool Collecting, int Online, int Total, DateTime? LastReceivedAt, IReadOnlyList<DashSensorDto> Sensors);

/// <summary>기타세정 현황(주간세정 제외) — 진행·포장 중인 건.</summary>
public record DashHandoverDto(int Open, int DueToday, int DueTomorrow, int Overdue);

/// <summary>생산팀 요청사항 — 진행 중, 마감 지남, 내가 아직 안 본 건.</summary>
public record DashProdReqDto(int Open, int Overdue, int Unread);
