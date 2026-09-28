namespace CleanPotal.Core.DTOs;

// ── 품목 단가표 ──
public record ProductMasterDto(
    int Id, string ProductName, string PartCode, string Spec, decimal UnitPrice,
    string VendorName, string Unit, string UpdatedBy, DateTime UpdatedAt,
    int? DeptId = null, string DeptName = "");

public record ProductMasterUpsertRequest(
    string ProductName, string PartCode, string Spec, decimal UnitPrice, string VendorName, string Unit,
    // 등록 부서 — 관리자만 고른다(그 밖에는 본인 부서).
    int? DeptId = null);

// ── 전역 품목 템플릿 ──
public record GlobalTemplateDto(int Id, string ProductCode, string ProductName, string TemplatePath);

public record GlobalTemplateUpsertRequest(string ProductCode, string ProductName, string TemplatePath);

// ── 견적 설정 ──
public record QuotationConfigDto(string BusinessNo, string Address, string Tel, string Fax, string Signer, string CompanyName);
