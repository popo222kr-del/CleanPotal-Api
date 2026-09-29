namespace CleanPotal.Core.DTOs;

// ── 업무 파일 통합 관리 ──

public record WorkEquipmentDto(int Id, string Code, string Line, string Kind, string Process, int SortOrder, bool IsActive);

/// <summary>설비 목록 저장 — 보낸 목록이 곧 최종 순서. 목록에 없는 기존 설비는 끈다(지우지 않는다 — 지난 기록이 가리킨다).</summary>
public record WorkEquipmentSaveRequest(IReadOnlyList<WorkEquipmentDto> Items);

/// <summary>약액 교체 한 칸(날짜·설비).</summary>
public record ChemicalChangeDto(DateOnly Date, string EqCode, string Content, string Note, string UpdatedBy, DateTime UpdatedAt);

/// <summary>약액 교체 한 칸 저장. 내용·메모가 모두 비면 그 칸을 지운다.</summary>
public record ChemicalSaveRequest(DateOnly Date, string EqCode, string? Content, string? Note);

/// <summary>한 달 치 약액 교체 — 설비 열 + 채워진 칸.</summary>
public record ChemicalMonthDto(int Year, int Month, IReadOnlyList<WorkEquipmentDto> Equipment, IReadOnlyList<ChemicalChangeDto> Cells);

/// <summary>엑셀 가져오기 한 칸. 화면이 엑셀을 읽어 보낸다.</summary>
public record ChemicalImportCell(DateOnly Date, string EqCode, string? Content, string? Note);
public record ChemicalImportRequest(IReadOnlyList<ChemicalImportCell> Cells, bool Overwrite);
public record ChemicalImportResultDto(int Added, int Updated, int Skipped, IReadOnlyList<string> NewEquipment);

/// <summary>업무보고(세정/BAKE) 한 줄 — 설비·공정과 그날 약액 교체 현황.</summary>
public record WorkReportRowDto(string Line, string Kind, string Code, string Process, string Content, string Note,
                               DateOnly? LastChangeDate, string LastChangeContent);
/// <summary>업무보고 — 세정 설비는 약액 교체, BAKE 오븐은 그날 BAKE 그을음 기록(Bake, 교대·회차 순)을 붙인다.</summary>
public record WorkReportDto(DateOnly Date, IReadOnlyList<WorkReportRowDto> Rows, int ChangedCount,
    IReadOnlyList<BakeLogDto>? Bake = null);

// ── 가성소다·폐액 ──

/// <summary>가성소다·폐액 한 줄(날짜·교대). 감소량·증가량은 서버가 계산해 준다(값이 빠지면 null).</summary>
public record WasteLogDto(DateOnly Date, string Shift,
    decimal? CausticBefore, decimal? CausticAfter, decimal? CausticUsed,
    decimal? WasteBefore, decimal? WasteAfter, decimal? WasteIncrease,
    string DipEquipment, string SprayEquipment, decimal? DailyChange, string Note, string UpdatedBy);

/// <summary>한 달 치 + 달 시작 직전 값(첫 줄의 前 값을 채우는 데 쓴다) + 날짜별 약액 교체 설비(Dip/Spray 칸 채우기용).</summary>
public record WasteMonthDto(int Year, int Month, IReadOnlyList<WasteLogDto> Rows,
    decimal? PrevCausticAfter, decimal? PrevWasteAfter,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ChemicalByDate);

public record WasteSaveRequest(DateOnly Date, string Shift,
    decimal? CausticBefore, decimal? CausticAfter, decimal? WasteBefore, decimal? WasteAfter,
    string? DipEquipment, string? SprayEquipment, decimal? DailyChange, string? Note);

public record WasteImportRequest(IReadOnlyList<WasteSaveRequest> Rows, bool Overwrite);
public record WasteImportResultDto(int Added, int Updated, int Skipped, DateOnly? From, DateOnly? To);

/// <summary>
/// 월별 추이 한 칸. KOH 는 줄어든 만큼이 사용, 늘어난 만큼이 보충이고, 폐액은 늘어난 만큼이 발생, 줄어든 만큼이 수거다 —
/// 둘을 더하면 서로 지워져 의미가 없으므로 따로 센다. Days = 기록한 날, Changes = 약액 교체(Dip+Spray) 설비 수.
/// </summary>
public record WasteTrendPointDto(int Year, int Month, decimal CausticUsed, decimal WasteIncrease, int Days, int Changes,
                                 decimal CausticRefill = 0, decimal WasteRemoved = 0);

// ── BAKE OVEN 그을음 ──

/// <summary>그을음 기록 한 칸. HasSoot·HasQuartz 는 서버가 판정해 준다(X·無 가 아닌 값).</summary>
public record BakeLogDto(DateOnly Date, string Shift, int Round, string EqCode, string Status,
    DateTime? TrackIn, DateTime? TrackOut, string Item, string SerialNo,
    string Soot, string TempUp, string TempDown, string TempDown2, string Quartz, string Note,
    bool HasSoot, bool HasQuartz, string UpdatedBy);

/// <summary>한 칸 저장. 상태·값이 모두 비면 그 칸을 지운다.</summary>
public record BakeSaveRequest(DateOnly Date, string Shift, int Round, string EqCode, string? Status,
    DateTime? TrackIn, DateTime? TrackOut, string? Item, string? SerialNo,
    string? Soot, string? TempUp, string? TempDown, string? TempDown2, string? Quartz, string? Note);

/// <summary>하루 치 — 오븐 열 + 그날 칸 + 바로 앞 기록(전날 마지막 회차, '앞 회차 이어받기' 용).</summary>
public record BakeDayDto(DateOnly Date, IReadOnlyList<WorkEquipmentDto> Ovens, IReadOnlyList<BakeLogDto> Rows, IReadOnlyList<BakeLogDto> PrevRows);

public record BakeImportRequest(IReadOnlyList<BakeSaveRequest> Rows, bool Overwrite);
public record BakeImportResultDto(int Added, int Updated, int Skipped, IReadOnlyList<string> NewEquipment, DateOnly? From, DateOnly? To);

/// <summary>S/N·품명 검색, 또는 그을음·Q'TZ 가루가 있었던 칸 모음. Total 은 잘리기 전 개수.</summary>
public record BakeSearchDto(IReadOnlyList<BakeLogDto> Rows, int Total);

// ── 폐기품 관리 ──

public record ScrapBatchSummaryDto(int Id, DateOnly Date, string Title, bool IsClosed, int Count, int Matched, int Loaded);
public record ScrapItemDto(int Id, string Line, string MatId, string MatDesc, string SerialNo, string OutNo,
    bool Matched, bool Loaded, string Remark, int SortOrder, string UpdatedBy);
public record ScrapBatchDto(int Id, DateOnly Date, string Title, string Note, bool IsClosed, DateTime? ClosedAt, string ClosedBy,
    string CreatedBy, IReadOnlyList<ScrapItemDto> Items);
/// <summary>LIST 머리 저장(날짜·제목·메모·상차 완료). Id 가 0 이면 새 LIST.</summary>
public record ScrapBatchSaveRequest(DateOnly Date, string? Title, string? Note, bool IsClosed);
public record ScrapItemSaveRequest(string? Line, string? MatId, string? MatDesc, string? SerialNo, string? OutNo,
    bool Matched, bool Loaded, string? Remark);
public record ScrapItemsAddRequest(IReadOnlyList<ScrapItemSaveRequest> Items);
/// <summary>찾기 결과 한 줄 — 어느 LIST 의 줄인지 같이.</summary>
public record ScrapHitDto(int BatchId, DateOnly BatchDate, bool BatchClosed, ScrapItemDto Item);
public record ScrapSearchDto(IReadOnlyList<ScrapHitDto> Items, IReadOnlyList<ScrapTagDto> Tags);
/// <summary>MAT ID → MAT DESC(지난 LIST 에서). 줄 입력 때 품명 채우기용.</summary>
public record ScrapMaterialDto(string MatId, string MatDesc);

public record ScrapTagDto(int Id, DateOnly Date, string Writer, string Item, string SerialNo, string Line, string Circle,
    string Owner, string Note, bool Done, string UpdatedBy);
public record ScrapTagSaveRequest(DateOnly Date, string? Writer, string? Item, string? SerialNo, string? Line, string? Circle,
    string? Owner, string? Note, bool Done);
public record ScrapCircleDto(int Id, string Name, string Line, string Owner);
public record ScrapCirclesSaveRequest(IReadOnlyList<ScrapCircleDto> Items);

/// <summary>엑셀 가져오기 — LIST 들(이력 + 작성 중), 눈관리, 분임조 표.</summary>
public record ScrapImportBatch(DateOnly Date, string? Title, bool IsClosed, IReadOnlyList<ScrapItemSaveRequest> Items);
public record ScrapImportRequest(IReadOnlyList<ScrapImportBatch> Batches, IReadOnlyList<ScrapTagSaveRequest> Tags,
    IReadOnlyList<ScrapCircleDto> Circles);
public record ScrapImportResultDto(int Batches, int Items, int SkippedBatches, int Tags, int SkippedTags, int Circles);

// ── 양식 다운로드 ──

public record WorkFormDto(int Id, string No, string Title, string Description, string FileRef, string UpdatedBy, DateTime UpdatedAt);
/// <summary>양식 한 줄 저장(Id 0 이면 새 양식).</summary>
public record WorkFormSaveItem(int Id, string? No, string? Title, string? Description, string? FileRef);
/// <summary>양식 목록 통째로 저장 — 보낸 순서가 곧 표시 순서, 빠진 양식은 지운다.</summary>
public record WorkFormsSaveRequest(IReadOnlyList<WorkFormSaveItem> Items);
