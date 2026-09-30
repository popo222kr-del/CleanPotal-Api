namespace CleanPotal.Core.DTOs;

// ── 체크시트 (설비) — 설비 점검표 AQ-C-13 Rev.7 ──

/// <summary>점검하는 사람. 월간은 설비팀(설정의 '월간 점검 부서·팀')만, NG 조치는 편집 등급·설비팀.</summary>
public record EqCheckActor(string Username, string RealName, bool IsAdmin, bool CanEdit, string Department, string TeamName);

public record EqCheckItemDto(
    int Id, int TemplateId, string Cycle, int SortOrder, string Category, string Name, string Point, string Spec,
    string InputType, string Options, string Fields, string Unit, decimal? Min, decimal? Max, bool RunOnly, bool IsActive);

public record EqCheckTemplateDto(
    int Id, string Code, string Name, string Note, int SortOrder, bool IsActive,
    IReadOnlyList<EqCheckItemDto> Items, IReadOnlyList<string> Units);

public record EqCheckTemplateSaveRequest(int Id, string Code, string Name, string Note, bool IsActive);

/// <summary>점검 호기. InList=false 면 설비 목록(스케줄 보드 설비)에 없는 호기 코드.</summary>
public record EqCheckUnitDto(
    int Id, string Code, int TemplateId, string TemplateName, bool IsActive, string Note,
    string Line, string Process, bool InList);

public record EqCheckUnitSaveRequest(string Code, int TemplateId, bool IsActive, string? Note);

/// <summary>결과. Nums 는 칸 이름 → 값(NUM 은 "" 한 칸). ValueText 는 화면·출력용 한 줄.</summary>
public record EqCheckResultDto(
    int Id, int ItemId, string Value, IReadOnlyDictionary<string, decimal?> Nums, bool NotRunning,
    string Judge, string Memo, string ValueText, string CheckedByName, DateTime CheckedAt,
    string NgStatus, string NgCloseNote, string NgClosedBy, DateTime? NgClosedAt);

/// <summary>한 주기(일상·주간·월간)의 점검 한 번. State: done / partial / due(오늘 할 차례) / late(기한 지남) / todo(아직).</summary>
public record EqCheckPeriodDto(
    string Cycle, string PeriodKey, string Label, DateOnly DueDate, string State, bool CanEdit, string Reason,
    IReadOnlyList<EqCheckItemDto> Items, IReadOnlyList<EqCheckResultDto> Results, string Note,
    int Done, int Total, int Ng);

public record EqCheckSheetDto(EqCheckUnitDto Unit, DateOnly Today, IReadOnlyList<EqCheckPeriodDto> Periods, bool IsMonthlyTeam);

/// <summary>ViaQr — 설비 QR 로 들어온 화면인지. 조회 등급은 QR 로만 점검한다(편집 등급·설비팀·관리자는 목록·PC 에서도).</summary>
public record EqCheckSaveRequest(
    string Cycle, string PeriodKey, string? Value, Dictionary<string, decimal?>? Nums, bool NotRunning, string? Memo, bool ViaQr = false);

public record EqCheckNoteRequest(string Cycle, string PeriodKey, string? Note, bool ViaQr = false);

public record EqCheckCellDto(string State, int Done, int Total, int Ng, string By);

public record EqCheckStatusRowDto(
    string UnitCode, string Line, string Process, string TemplateName,
    EqCheckCellDto Daily, EqCheckCellDto Weekly, EqCheckCellDto Monthly, int DailyDoneDays, int OpenNg);

/// <summary>CanOpenOffQr — 이 사람이 현황 목록(QR 없이)에서 점검 화면을 열어 입력할 수 있는지(편집 등급·설비팀·관리자).</summary>
public record EqCheckStatusDto(
    DateOnly Date, string WeekKey, DateOnly WeekDue, string MonthKey, DateOnly MonthDue, int DaysElapsed,
    IReadOnlyList<EqCheckStatusRowDto> Rows, bool CanOpenOffQr = false);

public record EqCheckNgDto(
    int Id, string UnitCode, string Line, string Cycle, string PeriodKey, string Category, string Name, string Point,
    string Spec, string ValueText, string Memo, string CheckedByName, DateTime CheckedAt,
    string NgStatus, string NgCloseNote, string NgClosedBy, DateTime? NgClosedAt);

public record EqCheckNgCloseRequest(string Note);

/// <summary>점검과 따로 적는 고장·부적합(점검표 아래 칸).</summary>
public record EqCheckFaultRequest(string UnitCode, DateOnly Date, string Text, string? Memo);

/// <summary>월간 점검표(종이 양식 모양) — 일상 1~31일, 주간(그 달 금요일), 월간 1칸.</summary>
public record EqCheckMonthDto(
    EqCheckUnitDto Unit, int Year, int Month, IReadOnlyList<EqCheckItemDto> Items,
    IReadOnlyList<string> WeekKeys, string MonthKey, DateOnly MonthDue,
    IReadOnlyList<EqCheckMonthCellDto> Cells, IReadOnlyList<EqCheckMonthNoteDto> Notes, IReadOnlyList<EqCheckNgDto> Faults,
    IReadOnlyList<EqCheckHolidayDto> Holidays);

public record EqCheckHolidayDto(string Date, string Name);

public record EqCheckMonthCellDto(int ItemId, string Cycle, string PeriodKey, string ValueText, string Judge, string By);

public record EqCheckMonthNoteDto(string Cycle, string PeriodKey, string Note, string By);

public record EqCheckQrDto(string Code, string Name, string Url, string Svg);
