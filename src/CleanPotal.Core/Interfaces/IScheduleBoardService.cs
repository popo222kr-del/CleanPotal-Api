using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

/// <summary>스케줄보드(생산 라인 간트) 비즈니스 로직.</summary>
public interface IScheduleBoardService
{
    IReadOnlyList<ScheduleEquipmentDto> GetEquipments();

    Task<IReadOnlyList<ScheduleBlockDto>> GetDayAsync(string boardDate);
    Task<IReadOnlyList<ScheduleBlockDto>> SaveDayAsync(string boardDate, IReadOnlyList<ScheduleBlockRow> blocks);

    Task<IReadOnlyList<ScheduleRecipeDto>> GetRecipesAsync();
    Task<(bool ok, string message, ScheduleRecipeDto? recipe)> AddRecipeAsync(string text);
    Task<bool> DeleteRecipeAsync(int id);
    Task<ScheduleRecipeDto?> SetFavoriteAsync(int id, bool favorite);
}
