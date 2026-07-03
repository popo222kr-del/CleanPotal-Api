namespace CleanPotal.Core.Entities;

/// <summary>사무실 공지 (WPF office_notice.json). 인수인계 화면의 공지 등록/삭제.</summary>
public class Notice
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
