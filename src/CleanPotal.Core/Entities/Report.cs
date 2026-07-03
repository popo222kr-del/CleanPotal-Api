namespace CleanPotal.Core.Entities;

/// <summary>
/// 회의록/보고서 (WPF production_meetings.json · weekly_reports.json 통합).
/// ReportType 으로 생산미팅(meeting) / 주간보고(weekly) 를 구분한다.
/// 월별 그룹(MonthTitle) → 보고서 → 블록(ReportBlock) 구조.
/// </summary>
public class Report
{
    public int Id { get; set; }
    public string ReportType { get; set; } = "meeting";   // meeting | weekly
    public string MonthTitle { get; set; } = "";

    public string Title { get; set; } = "";
    public string ShortTitle { get; set; } = "";
    public string DateRange { get; set; } = "";

    public string Memo { get; set; } = "";
    public string MemoRich { get; set; } = "";
    public string MainContent { get; set; } = "";        // 생산미팅 전용
    public string MainContentRich { get; set; } = "";
    public string NightContent { get; set; } = "";       // 생산미팅 전용
    public string NightContentRich { get; set; } = "";
    public string Attendees { get; set; } = "";          // 생산미팅 전용
    public string Summary { get; set; } = "";            // 생산미팅 전용

    public string MemoAttachments { get; set; } = "";    // JSON 문자열
    public string MainAttachments { get; set; } = "";    // JSON 문자열

    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    public List<ReportBlock> Blocks { get; set; } = new();
}

/// <summary>보고서 블록 (카테고리·상태·내용·후속·진행률 등).</summary>
public class ReportBlock
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public Report? Report { get; set; }

    public int Number { get; set; }
    public string Category { get; set; } = "";
    public string Status { get; set; } = "";
    public string Content { get; set; } = "";
    public string ContentRich { get; set; } = "";
    public string FollowUp { get; set; } = "";
    public string FollowUpRich { get; set; } = "";
    public string Kind { get; set; } = "";               // 블록 종류(heading 등)
    public string Heading { get; set; } = "";
    public bool IsCollapsed { get; set; }
    public int ProgressPercent { get; set; }
    public string Importance { get; set; } = "";
    public string FollowUpAttachments { get; set; } = ""; // JSON 문자열
}
