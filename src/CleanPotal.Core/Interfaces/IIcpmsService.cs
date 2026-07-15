using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

/// <summary>설비 ICP-MS 측정 데이터·설비·점검일지·감사로그.</summary>
public interface IIcpmsService
{
    Task<IReadOnlyList<EquipmentDto>> GetEquipmentAsync();
    Task<IcpmsFiltersDto> GetFiltersAsync(IReadOnlyList<string>? processTypes);
    Task<IReadOnlyList<MeasurementDto>> GetMeasurementsAsync(IReadOnlyList<string>? processTypes, IReadOnlyList<string>? baths, IReadOnlyList<string>? eqIds, IReadOnlyList<string>? dates);
    /// <summary>설비별 대표값(필터 내 최신일, 동일일 다행이면 평균). eqId → (element → value).</summary>
    Task<IReadOnlyList<(string EqId, string Process, IReadOnlyDictionary<string, double> Values)>> GetComparisonAsync(IReadOnlyList<string>? processTypes, IReadOnlyList<string>? baths, IReadOnlyList<string>? eqIds, IReadOnlyList<string>? dates);
    Task<IcpmsSummaryDto> GetSummaryAsync(IReadOnlyList<string>? dates, IReadOnlyList<string>? elements);
    Task<MeasurementBulkResult> BulkInsertAsync(IReadOnlyList<MeasurementUploadRow> rows, string user);

    Task<EquipmentDto?> UpdateEquipmentAsync(string eqId, string? newEqId, string? process, string user);
    Task<EquipmentDto> AddEquipmentAsync(string eqId, string user);
    /// <summary>설비 삭제 — 측정 데이터가 없는 설비만(마스터·점검일지 제거).</summary>
    Task<(bool ok, string? error)> DeleteEquipmentAsync(string eqId, string user);

    Task<IReadOnlyList<CheckNoteItemDto>> GetCheckNotesAsync(string date);
    Task SaveCheckNoteAsync(string eqId, string date, string note, string user);
    Task<IReadOnlyList<CheckNoteHistoryDto>> GetCheckNoteHistoryAsync(string eqId);

    Task<int> DeleteAllAsync(string user);
    Task<IReadOnlyList<ActionLogDto>> GetActionLogAsync();
}
