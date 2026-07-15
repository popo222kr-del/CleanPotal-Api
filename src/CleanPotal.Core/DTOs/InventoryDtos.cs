namespace CleanPotal.Core.DTOs;

public record InventoryItemDto(
    int Id, int OrderNo, string ItemCode, string Category, string Unit, string RegisteredDate,
    string StorageLocation, string ItemName,
    string CurrentStock, string CurrentStockDisplay,
    string PreviousStock, string PreviousStockDisplay, string WeeklyDeltaText, bool WeeklyDeltaIsDecrease,
    string AppropriateStock, string MinOrderQty, string Supplier,
    string OrderDate, string OrderQty, string ExpectedReceipt, string Memo,
    bool IsOrdered, bool IsLow, DateTime UpdatedAt);

/// <summary>4구역 고정 그룹. ZoneKey=metal/nonmetal/office/cleaning(색), ZoneName=구역명, Locations=실제 위치명들.</summary>
public record InventoryZoneDto(string ZoneKey, string ZoneName, string Locations, IReadOnlyList<InventoryItemDto> Items);

public record InventoryUpsertRequest(
    string ItemCode, string Category, string Unit, string StorageLocation, string ItemName,
    string CurrentStock, string AppropriateStock, string MinOrderQty, string Supplier,
    string OrderDate, string OrderQty, string ExpectedReceipt, string Memo, bool IsOrdered);

public record InventoryOrderedRequest(bool IsOrdered);
public record InventorySnapshotRequest(string? Date);

/// <summary>엑셀 실사 확정: 각 품목의 새 현재고. 스냅샷(옛 현재고=previous) 후 반영 → 증감이 소비로 잡힘(리베이스라인 안 함).</summary>
public record InventoryImportRow(int Id, string Stock);
public record InventoryImportConfirmRequest(List<InventoryImportRow> Items);
