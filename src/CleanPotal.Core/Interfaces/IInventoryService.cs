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
}
