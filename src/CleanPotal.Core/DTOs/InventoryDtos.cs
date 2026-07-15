namespace CleanPotal.Core.DTOs;

public record InventoryItemDto(
    int Id, int OrderNo, string ItemCode, string Category, string Unit, string RegisteredDate,
    string StorageLocation, string ItemName,
    string CurrentStock, string CurrentStockDisplay,
    string PreviousStock, string PreviousStockDisplay, string WeeklyDeltaText, bool WeeklyDeltaIsDecrease,
    string AppropriateStock, string MinOrderQty, string Supplier,
    string OrderDate, string OrderQty, string ExpectedReceipt, string Memo,
    bool IsOrdered, bool IsLow, DateTime UpdatedAt);

/// <summary>구역별 그룹 (메탈/논메탈/OFFICE/세정랩 …).</summary>
public record InventoryZoneDto(string Location, IReadOnlyList<InventoryItemDto> Items);

public record InventoryUpsertRequest(
    string ItemCode, string Category, string Unit, string StorageLocation, string ItemName,
    string CurrentStock, string AppropriateStock, string MinOrderQty, string Supplier,
    string OrderDate, string OrderQty, string ExpectedReceipt, string Memo, bool IsOrdered);

public record InventoryOrderedRequest(bool IsOrdered);
public record InventorySnapshotRequest(string? Date);
