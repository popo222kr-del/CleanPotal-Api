namespace CleanPotal.Core.Entities;

/// <summary>현장 점검 제출 기록 (구역·날짜·교대별 1건).</summary>
public class InspectionRecord
{
    public int Id { get; set; }
    public string Zone { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Shift { get; set; } = "";   // 주간 / 야간
    public string Worker { get; set; } = "";
    public int CheckedCount { get; set; }
    public int TotalCount { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.Now;
}
