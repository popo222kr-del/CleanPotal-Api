using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IBrokenService
{
    // 파손 기록
    Task<IReadOnlyList<BrokenRecordDto>> GetAllAsync(int? year, string? team, string? productType, string? official, string? search);
    Task<BrokenFilterOptionsDto> GetFilterOptionsAsync();
    Task<BrokenRecordDto> CreateAsync(BrokenUpsertRequest req);
    Task<BrokenRecordDto?> UpdateAsync(int id, BrokenUpsertRequest req);
    Task<bool> DeleteAsync(int id);

    // 교육 기록
    Task<IReadOnlyList<BrokenTrainingDto>> GetTrainingsAsync(string? type);
    Task<BrokenTrainingDto> CreateTrainingAsync(BrokenTrainingUpsertRequest req);
    Task<BrokenTrainingDto?> UpdateTrainingAsync(int id, BrokenTrainingUpsertRequest req);
    Task<bool> DeleteTrainingAsync(int id);

    // 교육 목표 / 메모
    Task<IReadOnlyList<BrokenGoalDto>> GetGoalsAsync();
    Task<IReadOnlyList<BrokenGoalDto>> SaveGoalsAsync(IReadOnlyList<BrokenGoalInput> goals);
    Task<BrokenMemoDto> GetMemoAsync();
    Task<BrokenMemoDto> SaveMemoAsync(BrokenMemoDto req);
}
