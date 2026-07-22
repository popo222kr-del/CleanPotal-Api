namespace CleanPotal.Core.DTOs;

// ── 품목 단가표 ──
public record ProductMasterDto(
    int Id, string ProductName, string PartCode, string Spec, decimal UnitPrice,
    string VendorName, string Unit, string UpdatedBy, DateTime UpdatedAt);

public record ProductMasterUpsertRequest(
    string ProductName, string PartCode, string Spec, decimal UnitPrice, string VendorName, string Unit);

// ── 전역 품목 템플릿 ──
public record GlobalTemplateDto(int Id, string ProductCode, string ProductName, string TemplatePath);

public record GlobalTemplateUpsertRequest(string ProductCode, string ProductName, string TemplatePath);

// ── 견적 설정 ──
public record QuotationConfigDto(string BusinessNo, string Address, string Tel, string Fax, string Signer, string CompanyName);
