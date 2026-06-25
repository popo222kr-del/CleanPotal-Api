namespace CleanPotal.Core.Entities;

/// <summary>팀 일정 (기존 WPF TeamEvent / team_event 테이블).</summary>
public class TeamEvent
{
    public int Id { get; set; }
    public string RegisteredBy { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Content { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTime CreateDate { get; set; } = DateTime.Now;
}
