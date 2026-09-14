namespace CleanPotal.Core.Entities;

/// <summary>사무실 공지 (WPF office_notice.json). 인수인계 화면의 공지 등록/삭제.</summary>
public class Notice
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>작성자 계정 PK. 이름 대신 이 값으로 작성자를 판정한다.
    /// (동명이인·개명에도 흔들리지 않음) 과거 데이터는 비어 있을 수 있어 nullable 이며,
    /// `backfill-authors` 명령으로 이름이 유일하게 일치하는 행만 채운다.</summary>
    public int? CreatorUserId { get; set; }

    /// <summary>동시 수정 감지용 버전. 저장할 때마다 1 씩 올라간다.
    /// 클라이언트가 불러올 때 받은 값과 다르면 그 사이 누군가 먼저 저장한 것이다.</summary>
    public int RowVersion { get; set; }
}
