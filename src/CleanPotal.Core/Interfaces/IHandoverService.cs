using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IHandoverService
{
    Task<IReadOnlyList<HandoverDto>> GetAllAsync(string? status, string? category, string? search, bool weekly, string actor = "");
    Task<IReadOnlyDictionary<string, int>> GetStatusCountsAsync(bool weekly);
    Task<HandoverDto> CreateAsync(HandoverUpsertRequest req, string actor);
    Task<HandoverDto?> UpdateAsync(int id, HandoverUpsertRequest req, string actor, bool isAdmin);
    Task<HandoverDto?> ChangeStatusAsync(int id, string status, string actor, bool isAdmin);
    Task<bool> MarkReadAsync(int id, string actor);
    Task<bool> DeleteAsync(int id, bool isAdmin);
}
