namespace CleanPotal.Core.Entities;

/// <summary>스케줄보드 배치 블록 (WPF CleanPotal.db ScheduleBlocks). 날짜별 설비 간트.</summary>
public class ScheduleBlock
{
    public int Id { get; set; }
    public string BoardDate { get; set; } = "";       // yyyy-MM-dd
    public int EquipmentIndex { get; set; }           // 설비 행 (0~18)
    public int StartMinute { get; set; }              // 07:00 기준 분 오프셋 (0~1439)
    public int S2Minutes { get; set; }
    public int HFMinutes { get; set; }
    public int DIMinutes { get; set; }
    public int? S2Temperature { get; set; }           // Hot Chemical 온도(℃)
    public string RecipeText { get; set; } = "";      // "30-10-100" 또는 "120-30-100@60"
    public DateTime CreatedTime { get; set; } = DateTime.Now;
}

/// <summary>스케줄보드 레시피 마스터 (WPF recipes.json → DB로 이관).</summary>
public class ScheduleRecipe
{
    public int Id { get; set; }
    public string Text { get; set; } = "";            // 정규화된 이름 (S2-HF-DI[@온도])
    public int S2Minutes { get; set; }
    public int HFMinutes { get; set; }
    public int DIMinutes { get; set; }
    public int? S2Temperature { get; set; }
    public bool IsFavorite { get; set; }
    public int OrderIndex { get; set; }
}
