namespace CleanPotal.Core.Entities;

/// <summary>견적서 헤더 (WPF quotations.json 구조).</summary>
public class Quotation
{
    public int Id { get; set; }

    public string QuoteNo { get; set; } = "";      // 견적번호 (AETS…)
    public string RfqNo { get; set; } = "";        // RFQ 번호
    public string Company { get; set; } = "";      // 수신 업체명
    public string Attention { get; set; } = "";    // 수신 담당자
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public DateOnly? QuoteDate { get; set; }        // 견적일 (Date)
    public string Validity { get; set; } = "";      // 유효기간 (자유 문자열)

    public string AetsManager { get; set; } = "";   // 당사 담당자
    public string AetsPhone { get; set; } = "";     // 당사 연락처
    public string AetsEmail { get; set; } = "";     // 작성자(당사) 이메일
    public string BusinessNo { get; set; } = "";    // 사업자번호

    public string Remarks { get; set; } = "";
    public string Memo { get; set; } = "";
    public string SourceFileName { get; set; } = "";

    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string LastModifiedBy { get; set; } = "";
    public DateTime? LastModifiedAt { get; set; }

    public List<QuotationItem> Items { get; set; } = new();
}

/// <summary>견적 품목 (WPF LineItems).</summary>
public class QuotationItem
{
    public int Id { get; set; }
    public int QuotationId { get; set; }
    public Quotation? Quotation { get; set; }

    public int No { get; set; }                     // 라인 번호
    public string Description { get; set; } = "";    // 품명
    public string PartCode { get; set; } = "";       // 품번
    public string StandardSpec { get; set; } = "";   // 규격
    public decimal ListPrice { get; set; }           // 단가
    public decimal Qty { get; set; }                 // 수량
}
