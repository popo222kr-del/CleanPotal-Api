using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IProductionMeetingService
{
    /// <summary>월별로 묶은 전체 미팅 목록 (최신순).</summary>
    Task<IReadOnlyList<ProductionMeetingGroupDto>> GetGroupedAsync();
    Task<ProductionMeetingDto?> GetAsync(int id);
    Task<ProductionMeetingDto> CreateAsync(ProductionMeetingUpsertRequest req, string actor);
    Task<ProductionMeetingDto?> UpdateAsync(int id, ProductionMeetingUpsertRequest req);
    Task<bool> DeleteAsync(int id);
}
