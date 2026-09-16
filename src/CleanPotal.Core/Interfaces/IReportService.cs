using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

/// <summary>회의록/보고서 (생산미팅 · 주간보고).</summary>
public interface IReportService
{
    Task<IReadOnlyList<ReportGroupDto>> GetGroupedAsync(string type);
    Task<ReportDto?> GetAsync(int id);
    Task<ReportDto> CreateAsync(ReportUpsertRequest req);
    Task<ReportDto?> UpdateAsync(int id, ReportUpsertRequest req);
    Task<bool> DeleteAsync(int id);
    Task<IReadOnlyList<ReportSearchHitDto>> SearchBlocksAsync(string type, string q);

    /// <summary>생산팀 인수인계(회의록) 전체 검색 — 주간/야간/Office 메모 텍스트를 관통.</summary>
    Task<IReadOnlyList<MeetingSearchHitDto>> SearchMeetingAsync(string q);
}
