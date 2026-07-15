namespace CleanPotal.Core.Entities;

/// <summary>현장 재고 품목 (WPF FieldInventory 이식). 재고는 "600매 이상" 같은 문구도 허용하므로 문자열.</summary>
public class InventoryItem
{
    public int Id { get; set; }
    public int OrderNo { get; set; }                     // 표시 순서
    public string ItemCode { get; set; } = "";           // 품목코드
    public string Category { get; set; } = "";           // 카테고리
    public string Unit { get; set; } = "";               // 단위 (숫자만 입력 시 표시에 자동 부착)
    public string RegisteredDate { get; set; } = "";     // 등록일자 yyyy-MM-dd
    public string StorageLocation { get; set; } = "";    // 구역 (메탈 반입구 / 논메탈 반입구 / OFFICE 보관 / 세정랩 …)
    public string ItemName { get; set; } = "";           // 품목명
    public string CurrentStock { get; set; } = "";       // 현재 재고 (문구 허용)
    public string AppropriateStock { get; set; } = "";   // 안전재고
    public string MinOrderQty { get; set; } = "";        // 최소 발주량
    public string Supplier { get; set; } = "";           // 발주 회사
    public string OrderDate { get; set; } = "";          // 발주일
    public string OrderQty { get; set; } = "";           // 발주 수량
    public string ExpectedReceipt { get; set; } = "";    // 입고 예정
    public string Memo { get; set; } = "";
    public bool IsOrdered { get; set; }                  // 발주 완료
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>주간 마감 시점의 재고 스냅샷 (전주 대비 증감 계산용).</summary>
public class InventorySnapshot
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string SnapshotDate { get; set; } = "";   // yyyy-MM-dd
    public string Stock { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
