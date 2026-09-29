namespace CleanPotal.Core.DTOs;

// ── 데일리 업무보고 ──
// 하루 = 그날 주간 + 그날 야간(다음 날 아침까지). 각 메뉴의 기록을 날짜로 모아 읽기 전용으로 보여 준다.
// 섹션마다 그 메뉴를 볼 수 있는 사람에게만 채우고, 못 보면 null 이다(화면은 그 섹션을 빼고 그린다).

/// <summary>근무 — 팀(또는 부서 줄) 하나의 주간·야간·휴무·교육 이름.</summary>
public record DailyCrewTeamDto(string Team, string Dept, bool Production,
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

/// <summary>그날 입고·출고, 출고일 지남(아직 완료 안 됨 — 지금 기준), 다음 날 출고 예정.</summary>
public record DailyHandoverDto(IReadOnlyList<DailyHandoverItemDto> In, IReadOnlyList<DailyHandoverItemDto> Out,
    IReadOnlyList<DailyHandoverItemDto> Overdue, IReadOnlyList<DailyHandoverItemDto> Tomorrow, int Open);

public record DailyProdReqItemDto(string Category, string Location, string RequestDetail, string Requester,
    DateOnly? DueDate, string Status, string ActionDetail, string Assignee);

/// <summary>그날 들어온 요청, 그날 처리한 요청, 마감 지난 진행 중 요청(지금 기준).</summary>
public record DailyProdReqDto(IReadOnlyList<DailyProdReqItemDto> New, IReadOnlyList<DailyProdReqItemDto> Done,
    IReadOnlyList<DailyProdReqItemDto> Overdue);

/// <summary>스케줄 보드 한 블록 — 시작·끝은 "07:30" 형태(끝이 다음 날로 넘어가면 NextDay).</summary>
public record DailyBoardBlockDto(string Start, string End, bool NextDay, string Recipe, int Minutes);

/// <summary>스케줄 보드 설비 한 줄(그날 블록이 있는 설비만).</summary>
public record DailyBoardEqDto(string Name, string Group, string Process, IReadOnlyList<DailyBoardBlockDto> Blocks);

public record DailyBoardDto(IReadOnlyList<DailyBoardEqDto> Equipment, int TotalEquipment, int IdleEquipment);

public record DailyBrokenDto(string Line, string ProductName, string SN, string Team, string Causer, string OccurStage,
    string Description, string Status, bool IsOfficial);

public record DailyScrapDto(string Title, int Items, int Loaded, bool IsClosed, IReadOnlyList<string> Lines);

public record DailyIcpmsDto(string EqId, string Process, bool Measured, string TopElement, double TopValue, string Note);

public record DailyDispatchDto(string VendorName, string Outgoing, string Incoming);

/// <summary>다음 날 — 교육 가는 사람, 팀 일정.</summary>
public record DailyTomorrowDto(IReadOnlyList<string> Edu, IReadOnlyList<string> Events);

public record DailyReportDto(
    DateOnly Date,
    IReadOnlyList<string>? DayTeams,
    IReadOnlyList<string>? NightTeams,
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
    IReadOnlyList<DailyBrokenDto>? Broken,
    IReadOnlyList<DailyScrapDto>? Scrap,
    IReadOnlyList<DailyIcpmsDto>? Icpms,
    IReadOnlyList<DailyDispatchDto>? Dispatch,
    DailyTomorrowDto? Tomorrow);

/// <summary>섹션별 조회 권한 — 컨트롤러가 사용자 등급·숨긴 메뉴로 따져 넘긴다.</summary>
public record DailyReportAccess(bool Checklist, bool Meeting, bool Handover, bool Weekly, bool ProdReq, bool Board,
    bool Waste, bool Bake, bool Broken, bool Scrap, bool Icpms, bool Dispatch)
{
    public static readonly DailyReportAccess All = new(true, true, true, true, true, true, true, true, true, true, true, true);
}
