using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

/// <summary>스케줄보드(생산 라인 간트) 비즈니스 로직.</summary>
public interface IScheduleBoardService
{
    // 설비 (DB 마스터)
    Task<IReadOnlyList<ScheduleEquipmentDto>> GetEquipmentsAsync();
    Task<ScheduleEquipmentDto> AddEquipmentAsync(string name, string groupName);
    Task<ScheduleEquipmentDto?> UpdateEquipmentAsync(int id, string name, string groupName);
    Task<bool> DeleteEquipmentAsync(int id);
    Task ReorderEquipmentsAsync(IReadOnlyList<int> ids);

    Task<IReadOnlyList<ScheduleBlockDto>> GetDayAsync(string boardDate);
    Task<IReadOnlyList<ScheduleBlockDto>> SaveDayAsync(string boardDate, IReadOnlyList<ScheduleBlockRow> blocks);

    Task<IReadOnlyList<ScheduleRecipeDto>> GetRecipesAsync();
    Task<(bool ok, string message, ScheduleRecipeDto? recipe)> AddRecipeAsync(string text);
    Task<(bool ok, string message, ScheduleRecipeDto? recipe)> UpdateRecipeAsync(int id, int s2, int hf, int di, int? temp);
    Task<bool> DeleteRecipeAsync(int id);
    Task<ScheduleRecipeDto?> SetFavoriteAsync(int id, bool favorite);
}
