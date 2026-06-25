namespace CleanPotal.Core.Entities;

/// <summary>현장 점검 체크 항목 (구역별, 관리자 관리). 기존 5S CHECK SHEET 항목.</summary>
public class InspectionItem
{
    public int Id { get; set; }
    public string Zone { get; set; } = "";   // metal_in / metal_out / nonmetal_in / nonmetal_out
    public int SortOrder { get; set; }
    public string Text { get; set; } = "";
}
