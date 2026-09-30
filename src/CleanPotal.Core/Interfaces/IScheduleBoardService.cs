using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

/// <summary>스케줄보드(생산 라인 간트) 비즈니스 로직.</summary>
public interface IScheduleBoardService
{
    // 설비 묶음(MDC · MSC · NDC …). 표가 비어 있으면 조회할 때 지금 설비들이 쓰는 이름으로 채운다.
    Task<IReadOnlyList<ScheduleGroupDto>> GetGroupsAsync();
    /// <summary>실패 사유. 성공하면 null.</summary>
    Task<string?> AddGroupAsync(string name);
    Task<string?> RenameGroupAsync(int id, string name);
    Task<string?> DeleteGroupAsync(int id);
    Task ReorderGroupsAsync(IReadOnlyList<int> ids);

    // 설비 (DB 마스터) — 모든 화면의 설비 목록. includeHidden=false 면 보드에 줄로 보이는 설비만.
    Task<IReadOnlyList<ScheduleEquipmentDto>> GetEquipmentsAsync(bool includeHidden = false);
    Task<ScheduleEquipmentDto> AddEquipmentAsync(ScheduleEquipmentUpsertRequest req);
    Task<ScheduleEquipmentDto?> UpdateEquipmentAsync(int id, ScheduleEquipmentUpsertRequest req);
    Task<bool> DeleteEquipmentAsync(int id);
    Task ReorderEquipmentsAsync(IReadOnlyList<int> ids);

    Task<IReadOnlyList<ScheduleBlockDto>> GetDayAsync(string boardDate);
    Task<IReadOnlyList<ScheduleBlockDto>> SaveDayAsync(string boardDate, IReadOnlyList<ScheduleBlockRow> blocks, IReadOnlyCollection<int>? knownIds = null);

    Task<IReadOnlyList<ScheduleRecipeDto>> GetRecipesAsync();
    Task<(bool ok, string message, ScheduleRecipeDto? recipe)> AddRecipeAsync(string text);
    Task<(bool ok, string message, ScheduleRecipeDto? recipe)> UpdateRecipeAsync(int id, int s2, int hf, int di, int? temp);
    Task<bool> DeleteRecipeAsync(int id);
    Task<ScheduleRecipeDto?> SetFavoriteAsync(int id, bool favorite);
}
