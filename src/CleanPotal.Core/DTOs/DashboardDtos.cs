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
    DateTime At,
    DashEqCheckDto? EqCheck = null,
    DashBoardDto? Board = null,
    DashChemicalDto? Chemical = null,
    DashWasteDto? Waste = null,
    DashBakeDto? Bake = null);

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

/// <summary>BROKEN — 이번 달·올해(발생일 기준) 건수와 올해 공식 건수.</summary>
public record DashBrokenDto(int ThisMonth, int ThisYear, int OfficialThisYear);

/// <summary>체크시트(설비) — 설비(점검 대상) 수 기준 매일·주간·월간 완료, 주간 지연, 미조치 NG.</summary>
public record DashEqCheckDto(
    DateOnly Date, int DailyDone, int DailyUnits, int WeeklyDone, int WeeklyUnits, int WeeklyLate,
    int MonthlyDone, int MonthlyUnits, DateOnly WeekDue, DateOnly MonthDue, int OpenNg);

/// <summary>스케줄 보드 — 근무일(07시 기준) 보드에 작업이 잡힌 설비 수 / 보드 설비 수, 비가동 설비 수, 작업 블록 수.</summary>
public record DashBoardDto(DateOnly Date, int Running, int Equipments, int Idle, int Blocks);

/// <summary>약액 교체 — 근무일에 교체한 세정 설비 수와 설비명(앞 몇 대).</summary>
public record DashChemicalDto(DateOnly Date, int Count, IReadOnlyList<string> Codes);

/// <summary>KOH·폐액 — 근무일(주·야 합)과 전날의 KOH 사용량·폐액 증가량. 적은 게 없으면 null.</summary>
public record DashWasteDto(DateOnly Date, decimal? CausticUsed, decimal? WasteIncrease,
    decimal? PrevCausticUsed, decimal? PrevWasteIncrease, int Shifts);

/// <summary>BAKE 그을음 — 근무일에 가동한 오븐 수 / 오븐 수, 그을음·Q'TZ 이상 건수.</summary>
public record DashBakeDto(DateOnly Date, int Running, int Ovens, int Soot, int Quartz);
