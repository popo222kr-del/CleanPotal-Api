namespace CleanPotal.Core.DTOs;

public record ScheduleBlockDto(
    int Id, string BoardDate, int EquipmentIndex, int StartMinute,
    int S2Minutes, int HFMinutes, int DIMinutes, int? S2Temperature, string RecipeText);

/// <summary>한 날짜의 블록 목록 (일괄 저장).</summary>
public record ScheduleBlockRow(
    int EquipmentIndex, int StartMinute,
    int S2Minutes, int HFMinutes, int DIMinutes, int? S2Temperature, string RecipeText);

public record ScheduleDaySaveRequest(List<ScheduleBlockRow> Blocks);

public record ScheduleRecipeDto(
    int Id, string Text, int S2Minutes, int HFMinutes, int DIMinutes,
    int? S2Temperature, bool IsFavorite, int OrderIndex, string DisplayText);

public record ScheduleRecipeAddRequest(string Text);
public record ScheduleRecipeFavoriteRequest(bool Favorite);

/// <summary>설비 목록 (고정).</summary>
public record ScheduleEquipmentDto(int Index, string DisplayName);
