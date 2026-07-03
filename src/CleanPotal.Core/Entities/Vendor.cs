namespace CleanPotal.Core.Entities;

/// <summary>업체 마스터 (기존 WPF VendorModel). IsWeekly로 주간세정 대상 분류.</summary>
public class Vendor
{
    public int Id { get; set; }
    public string VendorName { get; set; } = "";
    public string Category { get; set; } = "일반";
    public bool IsWeekly { get; set; }
    public bool IsFavorite { get; set; }          // 즐겨찾기
    public string BasePath { get; set; } = "";    // 기본 경로
    public string Addresses { get; set; } = "";   // 주소 여러 개 (JSON)
    public string Managers { get; set; } = "";    // 담당자 여러 개 (JSON)

    // (레거시 — 실제 데이터엔 없음, 호환용)
    public string Contact { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Note { get; set; } = "";
}
