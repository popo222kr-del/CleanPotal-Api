using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IInventoryService
{
    Task<IReadOnlyList<InventoryZoneDto>> GetByZoneAsync(string? search);
    Task<IReadOnlyList<string>> GetLocationsAsync();
    Task<InventoryItemDto> CreateAsync(InventoryUpsertRequest req);
    Task<InventoryItemDto?> UpdateAsync(int id, InventoryUpsertRequest req);
    Task<InventoryItemDto?> SetStockAsync(int id, decimal current);
    Task<bool> DeleteAsync(int id);
}
