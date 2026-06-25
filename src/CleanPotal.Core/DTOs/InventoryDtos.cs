namespace CleanPotal.Core.DTOs;

public record InventoryItemDto(
    int Id, string ItemCode, string ItemName, string Category, string Unit, string StorageLocation,
    decimal CurrentStock, decimal AppropriateStock, decimal PreviousStock,
    DateOnly? OrderDate, DateOnly? ExpectedDate, string Note, bool NeedsOrder, DateTime UpdatedAt);

/// <summary>구역별 그룹 (4구역 레이아웃).</summary>
public record InventoryZoneDto(string Location, IReadOnlyList<InventoryItemDto> Items);

public record InventoryUpsertRequest(
    string ItemCode, string ItemName, string Category, string Unit, string StorageLocation,
    decimal CurrentStock, decimal AppropriateStock, decimal PreviousStock,
    DateOnly? OrderDate, DateOnly? ExpectedDate, string Note);

public record InventoryStockRequest(decimal CurrentStock);
