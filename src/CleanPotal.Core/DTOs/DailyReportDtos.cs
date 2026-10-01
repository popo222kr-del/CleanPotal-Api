namespace CleanPotal.Core.DTOs;

// ── 데일리 업무보고 ──
// 하루 = 그날 주간 + 그날 야간(다음 날 아침까지). 각 메뉴의 기록을 날짜로 모아 읽기 전용으로 보여 준다.
// 섹션마다 그 메뉴를 볼 수 있는 사람에게만 채우고, 못 보면 null 이다(화면은 그 섹션을 빼고 그린다).

/// <summary>팀원 한 명 — 근속(입사일부터 개월 수, 입사일을 읽을 수 없으면 null).</summary>
/// <param name="Title">직위(맡은 일 — 세정팀장·조장 등). 비어 있으면 직급(사원·주임…)을 쓴다.</param>
public record DailyMemberDto(string Name, int? TenureMonths, string Title = "");

/// <summary>
/// 근무 — 팀(또는 부서 줄) 하나. Shift 는 그날 팀의 근무(주간/야간/주·야/휴무, 알 수 없으면 빈 칸),
/// HasShift 는 주·야 교대 팀(1팀·2팀)인지. Members 는 팀 전체 인원, 나머지는 그날 근무별 이름.
/// </summary>
public record DailyCrewTeamDto(string Team, string Dept, bool Production, bool HasShift, string Shift,
    IReadOnlyList<DailyMemberDto> Members,
    IReadOnlyList<string> Day, IReadOnlyList<string> Night, IReadOnlyList<string> Off, IReadOnlyList<string> Edu);

/// <summary>체크시트 구역 한 줄 — 주간·야간 제출 상태(none/progress/submitted/na)와 NG 수.</summary>
public record DailyCheckZoneDto(string Line, string Code, string Name,
    string DayState, int DayNg, string DayBy, string NightState, int NightNg, string NightBy, int WeeklyOverdue);

public record DailyCheckNgDto(string ZoneName, string Shift, string ItemText, string Memo, string CheckedBy,
    string NgStatus, string NgCloseNote);

public record DailyChecklistDto(IReadOnlyList<DailyCheckZoneDto> Zones, IReadOnlyList<DailyCheckNgDto> Ngs, int OpenNgAll);

/// <summary>생산팀 인수인계 한 건 — 주간·야간 내용과 사무실 메모 원문.</summary>
public record DailyMeetingDto(string Title, string DayContent, string NightContent, string OfficeMemo, string CreatorName);

/// <summary>기타세정·주간세정 한 건(사진·읽음 표시는 뺀다).</summary>
public record DailyHandoverItemDto(string Vendor, string Content, string Owner, DateOnly? InDate, DateOnly? OutDate, string Status);

/// <summary>그날 입고·출고, 출고일 지남(아직 완료 안 됨 — 지금 기준), 진행 중 건수.</summary>
public record DailyHandoverDto(IReadOnlyList<DailyHandoverItemDto> In, IReadOnlyList<DailyHandoverItemDto> Out,
    IReadOnlyList<DailyHandoverItemDto> Overdue, int Open);

public record DailyProdReqItemDto(string Category, string Location, string RequestDetail, string Requester,
    DateOnly? DueDate, string Status, string ActionDetail, string Assignee);

/// <summary>그날 들어온 요청, 그날 처리한 요청, 마감 지난 진행 중 요청(지금 기준).</summary>
public record DailyProdReqDto(IReadOnlyList<DailyProdReqItemDto> New, IReadOnlyList<DailyProdReqItemDto> Done,
    IReadOnlyList<DailyProdReqItemDto> Overdue);

/// <summary>
/// 스케줄 보드 한 블록 — 시작·끝은 "07:30" 형태(끝이 다음 날로 넘어가면 NextDay).
/// StartMinute 는 07:00 기준 분, S2·HF·DI 는 구간 길이(분) — 화면이 보드와 같은 그림을 그릴 때 쓴다.
/// </summary>
public record DailyBoardBlockDto(string Start, string End, bool NextDay, string Recipe, int Minutes,
    int StartMinute = 0, int S2 = 0, int HF = 0, int DI = 0);

/// <summary>스케줄 보드 설비 한 줄 — 모든 설비(블록이 없으면 대기, IsIdle 이면 유휴).</summary>
public record DailyBoardEqDto(string Name, string Group, string Process, bool IsIdle, IReadOnlyList<DailyBoardBlockDto> Blocks);

public record DailyBoardDto(IReadOnlyList<DailyBoardEqDto> Equipment, int TotalEquipment, int IdleEquipment);

public record DailyReportDto(
    DateOnly Date,
    IReadOnlyList<DailyCrewTeamDto>? Crew,
    DailyChecklistDto? Checklist,
    IReadOnlyList<DailyMeetingDto>? Meetings,
    DailyHandoverDto? Handover,
    DailyHandoverDto? Weekly,
    DailyProdReqDto? ProdReq,
    DailyBoardDto? Board,
    WorkReportDto? Chemical,
    WasteMonthDto? Waste,
    IReadOnlyList<BakeLogDto>? Bake,
    DailyEqCheckDto? EqCheck = null,
    IReadOnlyList<string>? Order = null);

/// <summary>섹션 순서 저장(관리자) — 섹션 키(crew·meeting·check·eqcheck·board·bake·handover·weekly·chemical·waste) 순서.</summary>
public record DailyOrderRequest(IReadOnlyList<string> Order);

/// <summary>체크시트(설비) — 그날 설비별 매일·주간·월간 진행(현황과 같은 값)과 그날 나온 NG·고장.</summary>
public record DailyEqCheckDto(EqCheckStatusDto Status, IReadOnlyList<EqCheckNgDto> Ngs);

/// <summary>섹션별 조회 권한 — 컨트롤러가 사용자 등급·숨긴 메뉴로 따져 넘긴다.</summary>
public record DailyReportAccess(bool Checklist, bool Meeting, bool Handover, bool Weekly, bool ProdReq, bool Board,
    bool Waste, bool Bake, bool EqCheck = false)
{
    public static readonly DailyReportAccess All = new(true, true, true, true, true, true, true, true, true);
}
