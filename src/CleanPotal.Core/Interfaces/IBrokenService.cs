using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IBrokenService
{
    Task<IReadOnlyList<BrokenRecordDto>> GetAllAsync(int? year, string? team, string? productType, string? official, string? search);
    Task<BrokenFilterOptionsDto> GetFilterOptionsAsync();
    Task<BrokenRecordDto> CreateAsync(BrokenUpsertRequest req);
    Task<BrokenRecordDto?> UpdateAsync(int id, BrokenUpsertRequest req);
    Task<bool> DeleteAsync(int id);
}
