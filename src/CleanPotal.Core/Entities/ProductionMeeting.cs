namespace CleanPotal.Core.Entities;

/// <summary>
/// 생산 미팅 기록 (기존 WPF ProductionMeetingReportModel).
/// 날짜별로 주간/야간 팀 미팅 내용과 Office 메모를 기록한다.
/// 주/야 팀은 근무 교대 예측으로 자동 판별하여 라벨에 표시.
/// </summary>
public class ProductionMeeting
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateOnly MeetingDate { get; set; }

    public string DayContent { get; set; } = "";    // 주간 팀 미팅 내용
    public string NightContent { get; set; } = "";  // 야간 팀 미팅 내용
    public string OfficeMemo { get; set; } = "";     // Office 메모

    public string CreatorName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}
