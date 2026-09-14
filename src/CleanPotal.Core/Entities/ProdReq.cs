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

    /// <summary>작성자 계정 PK. 이름 대신 이 값으로 작성자를 판정한다.
    /// (동명이인·개명에도 흔들리지 않음) 과거 데이터는 비어 있을 수 있어 nullable 이며,
    /// `backfill-authors` 명령으로 이름이 유일하게 일치하는 행만 채운다.</summary>
    public int? CreatorUserId { get; set; }

    /// <summary>동시 수정 감지용 버전. 저장할 때마다 1 씩 올라간다.
    /// 클라이언트가 불러올 때 받은 값과 다르면 그 사이 누군가 먼저 저장한 것이다.</summary>
    public int RowVersion { get; set; }
}

/// <summary>생산팀 요청사항 읽음 상태 (WPF ProdReqReadState). 미확인 뱃지용 사용자별 워터마크.</summary>
public class ProdReqRead
{
    public string Username { get; set; } = "";     // PK
    public DateTime LastReadTime { get; set; } = DateTime.Now;
}
