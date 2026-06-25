using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IQuotationService
{
    Task<IReadOnlyList<QuotationSummaryDto>> GetAllAsync(string? vendor, string? search);
    Task<QuotationDto?> GetAsync(int id);
    Task<QuotationDto> CreateAsync(QuotationUpsertRequest req, string actor);
    Task<QuotationDto?> UpdateAsync(int id, QuotationUpsertRequest req, string actor);
    Task<bool> DeleteAsync(int id);
}
