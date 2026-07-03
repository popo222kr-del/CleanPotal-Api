namespace CleanPotal.Core.Entities;

/// <summary>품목 단가표 (WPF product_master.json). 견적 품목 단가 기준.</summary>
public class ProductMaster
{
    public int Id { get; set; }
    public string ProductName { get; set; } = "";   // 품명
    public string PartCode { get; set; } = "";       // 품번
    public string Spec { get; set; } = "";           // 규격
    public decimal UnitPrice { get; set; }           // 단가
    public string VendorName { get; set; } = "";     // 업체
    public string Unit { get; set; } = "";           // 단위
    public string UpdatedBy { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>전역 품목 템플릿 (WPF global_templates.json). U/I/B/S/P/A/D 등.</summary>
public class GlobalTemplate
{
    public int Id { get; set; }
    public string ProductCode { get; set; } = "";    // 품목 코드
    public string ProductName { get; set; } = "";
    public string TemplatePath { get; set; } = "";   // 템플릿 파일 경로
}

/// <summary>견적 설정 (WPF quotation_config.json). 단일 행(Id=1).</summary>
public class QuotationConfig
{
    public int Id { get; set; }
    public string BusinessNo { get; set; } = "";     // 사업자번호
}
