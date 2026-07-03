using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

/// <summary>견적 마스터 데이터 — 품목 단가표 · 전역 템플릿 · 견적 설정.</summary>
public interface IQuotationMasterService
{
    // 품목 단가표
    Task<IReadOnlyList<ProductMasterDto>> GetProductsAsync(string? search);
    Task<ProductMasterDto> CreateProductAsync(ProductMasterUpsertRequest req, string actor);
    Task<ProductMasterDto?> UpdateProductAsync(int id, ProductMasterUpsertRequest req, string actor);
    Task<bool> DeleteProductAsync(int id);

    // 전역 품목 템플릿
    Task<IReadOnlyList<GlobalTemplateDto>> GetTemplatesAsync();
    Task<GlobalTemplateDto> CreateTemplateAsync(GlobalTemplateUpsertRequest req);
    Task<GlobalTemplateDto?> UpdateTemplateAsync(int id, GlobalTemplateUpsertRequest req);
    Task<bool> DeleteTemplateAsync(int id);

    // 견적 설정
    Task<QuotationConfigDto> GetConfigAsync();
    Task<QuotationConfigDto> SaveConfigAsync(QuotationConfigDto req);
}
