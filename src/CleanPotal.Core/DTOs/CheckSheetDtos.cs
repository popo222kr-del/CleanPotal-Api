namespace CleanPotal.Core.DTOs;

// ── QR 체크시트 ──

/// <summary>누가 하는지 — 컨트롤러가 토큰과 DB 권한으로 채운다.</summary>
/// CanCheck: 체크시트 점검·제출(현장 점검 조회 1 이상 — 생산직이 "체크만" 하도록).
/// CanEdit: NG 조치 완료 처리(현장 점검 편집 2 이상). 양식 관리는 IsAdmin.
public record CheckActor(string Username, string Name, bool IsAdmin, bool CanEdit, bool CanCheck = true);

public record CheckPhotoDto(string K, string V);

public record CheckResultDto(
    int Id, string Result, decimal? NumValue, string Memo, IReadOnlyList<CheckPhotoDto> Photos,
    DateTime CheckedAt, string CheckedByName, string NgStatus);

/// <summary>점검 화면의 항목 한 줄. Group: common(공통) / zone(구역) / weekly(주 1회) / event(필요할 때).</summary>
public record CheckSheetItemDto(
    int ItemId, string Code, string Group, string Text, string Detail, string Timing,
    string WeekdayLabel, string DueState, string ResultType, string Unit, decimal? MinValue, decimal? MaxValue,
    string JudgeMode, string SpecText, string PhotoPolicy, bool Required, bool AllowNa, string PaperForm,
    CheckResultDto? Result, string DoneElsewhere);

public record CheckSheetDto(
    string ZoneCode, string ZoneName, string Line, DateOnly WorkDate, string Shift, bool IsCurrent,
    int? RunId, DateTime? SubmittedAt, string SubmittedByName, bool CanEdit, bool NeedsReason,
    IReadOnlyList<CheckSheetItemDto> Items);

public record CheckResultSaveRequest(
    DateOnly Date, string Shift, string? Result, decimal? NumValue, string? Memo,
    IReadOnlyList<CheckPhotoDto>? Photos, bool ViaQr, string? Reason);

public record CheckSubmitRequest(DateOnly Date, string Shift, bool ViaQr);

public record CheckShiftStatusDto(string State, int Done, int Total, int Ng, string SubmittedByName, DateTime? SubmittedAt);

public record CheckZoneStatusDto(
    string Code, string Name, CheckShiftStatusDto Day, CheckShiftStatusDto Night, int WeeklyDue, int WeeklyOverdue);

public record CheckLineStatusDto(string Line, IReadOnlyList<CheckZoneStatusDto> Zones);

public record CheckStatusDto(
    DateOnly WorkDate, string CurrentShift, DateOnly CurrentWorkDate, IReadOnlyList<CheckLineStatusDto> Lines, int OpenNg);

public record CheckNgDto(
    int ResultId, string ZoneCode, string ZoneName, string Line, DateOnly WorkDate, string Shift,
    string ItemCode, string ItemText, string ItemDetail, string SpecText, decimal? NumValue, string Memo,
    IReadOnlyList<CheckPhotoDto> Photos, string CheckedByName, DateTime CheckedAt, string NgDept,
    string NgStatus, DateTime? NgClosedAt, string NgClosedBy, string NgCloseNote);

public record CheckNgCloseRequest(string? Note);

/// <summary>월간 리포트 한 줄. Cells 는 (일-1)*2 + (야간이면 1) 자리에 표시값 — O / X / N/A / 수치 / 미 / 빈칸(해당 없음).</summary>
public record CheckReportRowDto(
    string ZoneCode, string ZoneName, string ItemCode, string Text, string Detail, string Timing,
    string PaperForm, IReadOnlyList<string> Cells);

public record CheckReportZoneSummaryDto(string ZoneCode, string ZoneName, int Due, int Done, int Ng, int Missing);

public record CheckReportDto(
    string Line, int Year, int Month, int Days, string FormName, string Revision, string EffectiveDate,
    IReadOnlyList<CheckReportRowDto> Rows, IReadOnlyList<CheckReportZoneSummaryDto> Zones,
    IReadOnlyList<CheckNgDto> Ngs, DateTime GeneratedAt);

public record CheckZoneDto(
    int Id, string Code, string Name, string Line, int SortOrder, bool IsCommon, bool HasQr,
    string QrLocation, int QrCount, bool IsActive, string Note);

public record CheckItemDto(
    int Id, string Code, string ZoneCode, int SortOrder, string Text, string Detail, string Cycle, string Timing,
    int? Weekday, string ResultType, string Unit, decimal? MinValue, decimal? MaxValue, string JudgeMode,
    string PhotoPolicy, bool Required, bool AllowNa, string PaperForm, string NgDept,
    DateOnly? ValidFrom, DateOnly? ValidTo, string RevisionNote, bool IsActive, string Note,
    DateTime UpdatedAt, string UpdatedBy);

public record CheckImportRequest(IReadOnlyList<CheckZoneDto> Zones, IReadOnlyList<CheckItemDto> Items);

public record CheckImportResultDto(int ZonesAdded, int ZonesUpdated, int ItemsAdded, int ItemsUpdated, IReadOnlyList<string> Warnings);

public record CheckQrDto(string Code, string Name, string Url, string Svg);

/// <summary>QR 라벨 화면. IsLocal 이면 휴대폰이 열 수 없는 주소(localhost 등) — Suggestions 는 서버의 실제 주소 후보.</summary>
/// Overridden 이면 이 서버는 설정 파일·환경변수 주소(테스트 서버)를 쓰고, SavedUrl(DB 저장값)은 운영 서버가 쓴다.
public record CheckQrPageDto(string BaseUrl, bool FromSetting, bool IsLocal, IReadOnlyList<string> Suggestions, IReadOnlyList<CheckQrDto> Labels,
    bool Overridden = false, string SavedUrl = "");
