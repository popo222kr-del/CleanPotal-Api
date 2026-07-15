using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IInventoryService
{
    Task<IReadOnlyList<InventoryZoneDto>> GetByZoneAsync(string? search);
    Task<IReadOnlyList<string>> GetLocationsAsync();
    Task<InventoryItemDto> CreateAsync(InventoryUpsertRequest req);
    Task<InventoryItemDto?> UpdateAsync(int id, InventoryUpsertRequest req);
    Task<InventoryItemDto?> SetOrderedAsync(int id, bool isOrdered);
    Task<bool> DeleteAsync(int id);
    /// <summary>주간 마감: 현재 모든 품목의 현재고를 스냅샷으로 저장(같은 날짜는 덮어쓰기).</summary>
    Task<int> CreateSnapshotAsync(string? date);
    /// <summary>엑셀 실사 확정: 스냅샷(옛 현재고) → 스테이징 재고 반영(리베이스라인 없음 = 소비 반영).</summary>
    Task<int> ConfirmImportAsync(IReadOnlyList<InventoryImportRow> items);
    Task<IReadOnlyList<InventorySnapshotDto>> GetSnapshotsAsync(string? from, string? to);
    Task<int> RenameLocationAsync(string oldName, string newName);
    Task<int> BulkUpdateAsync(InventoryBulkRequest req);
}
