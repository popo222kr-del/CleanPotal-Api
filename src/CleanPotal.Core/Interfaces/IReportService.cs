using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

/// <summary>회의록/보고서 (생산미팅 · 주간보고).</summary>
public interface IReportService
{
    Task<IReadOnlyList<ReportGroupDto>> GetGroupedAsync(string type);
    Task<ReportDto?> GetAsync(int id);
    Task<ReportDto> CreateAsync(ReportUpsertRequest req);
    Task<ReportDto?> UpdateAsync(int id, ReportUpsertRequest req);
    Task<bool> DeleteAsync(int id);
}
