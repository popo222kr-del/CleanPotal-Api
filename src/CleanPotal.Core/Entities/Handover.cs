namespace CleanPotal.Core.Entities;

/// <summary>인수인계 (기존 WPF HandoverItem / handover 테이블).</summary>
public class Handover
{
    public int Id { get; set; }
    public string Vendor { get; set; } = "";
    public string Category { get; set; } = "QTZ";   // QTZ / SEMES / 삼성
    public string Owner { get; set; } = "";
    public string Content { get; set; } = "";
    public DateOnly? InDate { get; set; }
    public DateOnly? OutDate { get; set; }
    public string Status { get; set; } = "진행";     // 진행 / 포장 / 완료
    public string DeliveryMethod { get; set; } = "미정";
    public string Memo { get; set; } = "";

    public string CreatorName { get; set; } = "";
    public DateTime CreateDate { get; set; } = DateTime.Now;
    public string ModifierName { get; set; } = "";
    public DateTime? ModifyDate { get; set; }
}
