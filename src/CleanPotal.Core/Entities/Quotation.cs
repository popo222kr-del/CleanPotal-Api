namespace CleanPotal.Core.Entities;

/// <summary>견적서 헤더 (기존 WPF Quotation).</summary>
public class Quotation
{
    public int Id { get; set; }
    public string QuoteNo { get; set; } = "";
    public string VendorName { get; set; } = "";
    public DateOnly? QuoteDate { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public string Status { get; set; } = "작성중";   // 작성중 / 발송 / 확정
    public string Remarks { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    public List<QuotationItem> Items { get; set; } = new();
}

/// <summary>견적 품목 (기존 WPF QuotationLineItem).</summary>
public class QuotationItem
{
    public int Id { get; set; }
    public int QuotationId { get; set; }
    public Quotation? Quotation { get; set; }

    public int SortOrder { get; set; }
    public string ItemName { get; set; } = "";
    public string Spec { get; set; } = "";       // 규격
    public string Unit { get; set; } = "EA";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string Remarks { get; set; } = "";
}
