namespace CleanPotal.Core.Entities;

/// <summary>
/// 자재물류 일정 현황 — 고정 담당자 로스터(순서 관리). 기존 WPF의 담당자 로스터 대응.
/// </summary>
public class MaterialRosterMember
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

/// <summary>
/// 자재물류 일정 — 하루의 (담당자 × 오전/오후) 한 칸.
/// Period: "AM" | "PM". Destination = 목적지 &amp; 근무(업체명/내근/휴무 등).
/// Vehicles = 배정 차량 키 CSV (예: "v1,v3"). 5t(2255)=v1 … 1t(4795)=v5.
/// </summary>
public class MaterialScheduleEntry
{
    public int Id { get; set; }
    public DateOnly TargetDate { get; set; }
    public string PersonName { get; set; } = "";
    public string Period { get; set; } = "AM";
    public string Destination { get; set; } = "";
    public string Vehicles { get; set; } = "";
}

/// <summary>자재물류 일정 — 하루의 특이사항(오전/오후 분리).</summary>
public class MaterialDayNote
{
    public int Id { get; set; }
    public DateOnly TargetDate { get; set; }
    public string NoteAm { get; set; } = "";
    public string NotePm { get; set; } = "";
}
