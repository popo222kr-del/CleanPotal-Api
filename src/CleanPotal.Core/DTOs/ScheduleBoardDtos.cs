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
/// <summary>레시피 수정 (구조화 입력). 값으로 이름을 재계산한다.</summary>
public record ScheduleRecipeUpdateRequest(int S2Minutes, int HFMinutes, int DIMinutes, int? S2Temperature);

/// <summary>설비. Index=Slot(블록이 참조하는 안정 번호), DisplayName=이름+공정+특이사항 조합.</summary>
public record ScheduleEquipmentDto(
    int Index, string DisplayName, int Id, string GroupName, int OrderIndex,
    string Name, string Process, string Note, bool IsIdle);

public record ScheduleEquipmentUpsertRequest(
    string Name, string GroupName, string Process, string Note, bool IsIdle);
/// <summary>순서 재정렬 (Id를 원하는 순서로 나열).</summary>
public record ScheduleReorderRequest(List<int> Ids);
