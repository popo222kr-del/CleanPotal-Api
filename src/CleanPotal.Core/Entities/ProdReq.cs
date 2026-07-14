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

    /// <summary>요청 첨부 이미지 (base64 data URL 배열 JSON). WPF [[PRODREQ_IMAGES]] 대응.</summary>
    public string RequestImages { get; set; } = "";
    /// <summary>조치 결과 이미지 (base64 data URL 배열 JSON).</summary>
    public string ActionImages { get; set; } = "";
}

/// <summary>생산팀 요청사항 읽음 상태 (WPF ProdReqReadState). 미확인 뱃지용 사용자별 워터마크.</summary>
public class ProdReqRead
{
    public string Username { get; set; } = "";     // PK
    public DateTime LastReadTime { get; set; } = DateTime.Now;
}
