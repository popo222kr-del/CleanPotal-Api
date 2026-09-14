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

    /// <summary>작성자 계정 PK. 이름 대신 이 값으로 작성자를 판정한다.
    /// (동명이인·개명에도 흔들리지 않음) 과거 데이터는 비어 있을 수 있어 nullable 이며,
    /// `backfill-authors` 명령으로 이름이 유일하게 일치하는 행만 채운다.</summary>
    public int? CreatorUserId { get; set; }

    /// <summary>동시 수정 감지용 버전. 저장할 때마다 1 씩 올라간다.
    /// 클라이언트가 불러올 때 받은 값과 다르면 그 사이 누군가 먼저 저장한 것이다.</summary>
    public int RowVersion { get; set; }
}
