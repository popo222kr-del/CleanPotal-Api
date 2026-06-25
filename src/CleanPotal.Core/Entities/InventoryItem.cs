namespace CleanPotal.Core.Entities;

/// <summary>현장 재고 품목 (기존 WPF FieldInventory).</summary>
public class InventoryItem
{
    public int Id { get; set; }
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Category { get; set; } = "";
    public string Unit { get; set; } = "EA";
    public string StorageLocation { get; set; } = "";   // 보관 구역
    public decimal CurrentStock { get; set; }
    public decimal AppropriateStock { get; set; }       // 적정 재고
    public decimal PreviousStock { get; set; }          // 이전(주간 마감) 재고
    public DateOnly? OrderDate { get; set; }
    public DateOnly? ExpectedDate { get; set; }          // 입고 예정일
    public string Note { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
