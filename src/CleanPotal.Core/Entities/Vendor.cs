namespace CleanPotal.Core.Entities;

/// <summary>업체 마스터 (기존 WPF VendorModel). IsWeekly로 주간세정 대상 분류.</summary>
public class Vendor
{
    public int Id { get; set; }
    public string VendorName { get; set; } = "";
    public string Category { get; set; } = "일반";
    public bool IsWeekly { get; set; }
    public string Contact { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Note { get; set; } = "";
}
