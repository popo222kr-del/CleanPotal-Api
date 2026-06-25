namespace CleanPotal.Core.Entities;

/// <summary>생산팀 요청사항 (기존 WPF ProdReqItem / ProdReqs 테이블).</summary>
public class ProdReq
{
    public int Id { get; set; }
    public DateOnly? RequestDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Status { get; set; } = "진행";      // 진행 / 완료 / 보류
    public string Category { get; set; } = "";
    public string Location { get; set; } = "";
    public string RequestDetail { get; set; } = "";
    public string Requester { get; set; } = "";
    public DateOnly? ActionDate { get; set; }
    public string ActionDetail { get; set; } = "";
    public string Assignee { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
