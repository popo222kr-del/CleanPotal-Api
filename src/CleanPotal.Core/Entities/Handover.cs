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
    public bool IsWeekly { get; set; }               // true = 주간세정 현황, false = 일반 인수인계

    public string CreatorName { get; set; } = "";
    public DateTime CreateDate { get; set; } = DateTime.Now;
    public string ModifierName { get; set; } = "";
    public DateTime? ModifyDate { get; set; }

    /// <summary>읽은 사용자 이름 CSV (WPF ReadBy). 빨간 점(미확인) 표시용.</summary>
    public string ReadBy { get; set; } = "";

    /// <summary>첨부 이미지 목록. base64 data URL 문자열 배열의 JSON. (WPF HANDOVER_IMAGES)</summary>
    public string Images { get; set; } = "";
}
