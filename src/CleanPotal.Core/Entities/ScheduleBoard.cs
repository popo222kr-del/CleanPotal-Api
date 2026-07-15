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

/// <summary>스케줄보드 설비 마스터. 코드 하드코딩 → DB로 승격(추가·수정·삭제·순서).</summary>
public class ScheduleEquipment
{
    public int Id { get; set; }
    public string Name { get; set; } = "";        // 설비명 (예: "MDC01") — 괄호 없는 순수 이름
    public string Process { get; set; } = "";      // 공정 (예: "POLY") — 없으면 표시 생략
    public string Note { get; set; } = "";         // 특이사항 (예: "Hot Chemical") — 없으면 표시 생략
    public string GroupName { get; set; } = "";    // MDC / MSC / NDC (배경색·헤더 구분)
    public int Slot { get; set; }                  // 블록이 참조하는 안정적 번호(EquipmentIndex). 재정렬해도 불변
    public int OrderIndex { get; set; }            // 화면 표시 순서 (편집 가능)
    public bool IsIdle { get; set; }               // 유휴 설비 (알약 표시)
    public bool IsActive { get; set; } = true;     // 소프트 삭제 (기존 배치 보존)
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
