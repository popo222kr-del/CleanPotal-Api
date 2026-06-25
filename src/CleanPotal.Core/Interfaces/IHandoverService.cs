using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IHandoverService
{
    Task<IReadOnlyList<HandoverDto>> GetAllAsync(string? status, string? category, string? search);
    Task<IReadOnlyDictionary<string, int>> GetStatusCountsAsync();
    Task<HandoverDto> CreateAsync(HandoverUpsertRequest req, string actor);
    Task<HandoverDto?> UpdateAsync(int id, HandoverUpsertRequest req, string actor);
    Task<HandoverDto?> ChangeStatusAsync(int id, string status, string actor);
    Task<bool> DeleteAsync(int id);
}
