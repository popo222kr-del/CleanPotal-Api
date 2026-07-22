namespace CleanPotal.Core.DTOs;

public record ReportBlockDto(
    int Id, int Number, string Category, string Status,
    string Content, string ContentRich, string FollowUp, string FollowUpRich,
    string Kind, string Heading, bool IsCollapsed, int ProgressPercent, string Importance,
    string FollowUpAttachments);

public record ReportDto(
    int Id, string ReportType, string MonthTitle, string Title, string ShortTitle, string DateRange,
    string Memo, string MemoRich, string MainContent, string MainContentRich,
    string NightContent, string NightContentRich, string Attendees, string Summary,
    string MemoAttachments, string MainAttachments,
    DateTime CreatedAt, DateTime? UpdatedAt,
    IReadOnlyList<ReportBlockDto> Blocks);

/// <summary>목록용 요약 (블록 제외). HasMemo: Office 메모 존재 여부(목록 마커용), HasContent: 주간/야간 내용 존재 여부(빈 날짜 흐림 표시용).</summary>
public record ReportSummaryDto(int Id, string Title, string ShortTitle, string DateRange, int BlockCount, bool HasMemo, bool HasContent);

/// <summary>월별 그룹 (좌측 목록용).</summary>
public record ReportGroupDto(string MonthTitle, IReadOnlyList<ReportSummaryDto> Reports);

public record ReportBlockInput(
    int Number, string Category, string Status,
    string Content, string ContentRich, string FollowUp, string FollowUpRich,
    string Kind, string Heading, bool IsCollapsed, int ProgressPercent, string Importance,
    string FollowUpAttachments);

public record ReportUpsertRequest(
    string ReportType, string MonthTitle, string Title, string ShortTitle, string DateRange,
    string Memo, string MemoRich, string MainContent, string MainContentRich,
    string NightContent, string NightContentRich, string Attendees, string Summary,
    string MemoAttachments, string MainAttachments,
    IReadOnlyList<ReportBlockInput> Blocks);

/// <summary>전역 블록 검색 결과 — 어느 주차의 블록인지 포함.</summary>
public record ReportSearchHitDto(int ReportId, string ReportShortTitle, string ReportTitle, string DateRange, ReportBlockDto Block);
