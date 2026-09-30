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
/// <summary>
/// 설비 묶음(MDC · MSC · NDC …). 스케줄보드에서 설비를 줄 단위로 나누는 이름이다.
///
/// 지금까지는 화면에 세 개를 박아 두어, 새 묶음이 생기면 코드를 고쳐야 했다. 표로 빼서
/// 화면에서 더하고 지울 수 있게 한다. 설비는 이 이름을 문자열로 들고 있으므로
/// 이름을 바꾸면 그 이름을 쓰던 설비도 같이 바꿔 준다.
/// </summary>
public class ScheduleEquipGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int OrderIndex { get; set; }
}

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
    // ── 2026-09-30 설비 목록 통합: 이 표가 모든 화면(약액·BAKE·KOH·Daily 보고·ICP-MS·MES·설비 체크시트)의 설비 목록이다 ──
    public string Line { get; set; } = "";         // METAL / N-METAL / 공통
    public string Kind { get; set; } = "";         // 세정 / BAKE / DRY / 기타 (EquipKinds)
    public bool ShowOnBoard { get; set; } = true;  // 스케줄 보드에 줄로 보일지 — BAKE·DRY 오븐 등은 목록에만 있다
}

/// <summary>설비 종류 — 세정(약액·KOH·폐액 대상) / BAKE 오븐(그을음 기록) / DRY 오븐 / 기타(진공 오븐·약액 공급장치 등).</summary>
public static class EquipKinds
{
    public const string Clean = "세정";
    public const string Bake = "BAKE";
    public const string Dry = "DRY";
    public const string Etc = "기타";
    public static readonly string[] All = { Clean, Bake, Dry, Etc };
    public static readonly string[] Lines = { "METAL", "N-METAL", "공통" };
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
