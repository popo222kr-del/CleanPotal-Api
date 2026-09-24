namespace CleanPotal.Core.DTOs;

public record ScheduleBlockDto(
    int Id, string BoardDate, int EquipmentIndex, int StartMinute,
    int S2Minutes, int HFMinutes, int DIMinutes, int? S2Temperature, string RecipeText);

/// <summary>한 날짜의 블록 목록 (일괄 저장).</summary>
public record ScheduleBlockRow(
    int EquipmentIndex, int StartMinute,
    int S2Minutes, int HFMinutes, int DIMinutes, int? S2Temperature, string RecipeText);

/// <param name="KnownIds">
/// 이 화면이 불러온(또는 마지막으로 저장한) 그날 블록의 번호. 저장할 때마다 블록을 새로 만들므로 이 번호 묶음이
/// 곧 그날의 버전이다. 서버의 현재 묶음과 다르면 그 사이 다른 사람이 저장한 것이라 409 로 알린다.
/// 비어 있으면(옛 화면) 검사하지 않는다.
/// </param>
public record ScheduleDaySaveRequest(List<ScheduleBlockRow> Blocks, List<int>? KnownIds = null);

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

/// <summary>설비 묶음. <c>EquipCount</c> 는 이 묶음을 쓰는 설비 수 — 0 이어야 지울 수 있다.</summary>
public record ScheduleGroupDto(int Id, string Name, int OrderIndex, int EquipCount);

public record ScheduleGroupRequest(string Name);
