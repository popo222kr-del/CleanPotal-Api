namespace CleanPotal.Core.DTOs;

/// <summary>원소 22종 고정 순서 (엑셀/그리드/차트 공통).</summary>
public static class IcpElements
{
    public static readonly string[] Order =
    {
        "Li","Na","Mg","Al","K","Ca","Ti","Cr","Mn","Fe","Co","Ni","Cu","Zn","Ge","As","Cd","In","Ba","Ta","W","Pb",
    };
}

/// <summary>측정 1행. Values는 원소명(대문자) → 값.</summary>
public record MeasurementDto(
    int Id, string ProcessType, string EqId, string BathGb, string Category, string Unit, string AnalysisDate,
    IReadOnlyDictionary<string, double> Values);

/// <summary>업로드 1행 (클라이언트가 엑셀 파싱 후 전송).</summary>
public record MeasurementUploadRow(
    string ProcessType, string EqId, string BathGb, string Category, string Unit, string AnalysisDate,
    Dictionary<string, double> Values);
public record MeasurementBulkRequest(List<MeasurementUploadRow> Rows);
public record MeasurementBulkResult(int Received, int Inserted, int Skipped);

/// <summary>설비 목록 항목 (측정 이력 ∪ 마스터).</summary>
public record EquipmentDto(string EqId, string Process, bool HasData);
public record EquipmentUpsertRequest(string? NewEqId, string? Process);
public record EquipmentAddRequest(string EqId);

/// <summary>요약 카드.</summary>
public record IcpmsSummaryDto(
    int TotalEquip, int MeasuredEquip, int UnmeasuredEquip,
    string LatestDate, int MeasuredDateCount,
    double MaxValue, string MaxEqId, string MaxElement, string MaxDate,
    double Average, string Unit);

/// <summary>필터 옵션 (종속 필터용).</summary>
public record IcpmsFiltersDto(
    IReadOnlyList<string> ProcessTypes, IReadOnlyList<string> Baths,
    IReadOnlyList<string> EqIds, IReadOnlyList<string> Dates);

/// <summary>점검 일지 항목 (설비 × 선택 날짜).</summary>
public record CheckNoteItemDto(
    string EqId, string Process, bool Measured, string TopElement, double TopValue, string Note);
public record CheckNoteSaveRequest(string EqId, string Date, string Note);
public record CheckNoteHistoryDto(string CheckDate, string Note, string UpdatedAt);

public record ActionLogDto(int Id, string ActionType, string Detail, string UserName, string CreatedAt);
