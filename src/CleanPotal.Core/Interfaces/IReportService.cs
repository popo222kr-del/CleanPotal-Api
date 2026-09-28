using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

/// <summary>회의록/보고서 (생산미팅 · 주간보고).</summary>
public interface IReportService
{
    /// <summary>월별 목록. 주간보고는 부서별 — 관리자가 아니면 본인 부서, 관리자는 <paramref name="dept"/>(없으면 본인 부서).</summary>
    Task<IReadOnlyList<ReportGroupDto>> GetGroupedAsync(string type, int? dept = null);
    Task<ReportDto?> GetAsync(int id);
    /// <summary>보고서 종류(meeting|weekly). 없으면 null — 권한 확인용(종류마다 영역이 다르다).</summary>
    Task<string?> GetTypeAsync(int id);
    Task<ReportDto> CreateAsync(ReportUpsertRequest req);
    Task<ReportDto?> UpdateAsync(int id, ReportUpsertRequest req);
    Task<bool> DeleteAsync(int id);
    Task<IReadOnlyList<ReportSearchHitDto>> SearchBlocksAsync(string type, string q, int? dept = null);

    /// <summary>생산팀 인수인계(회의록) 전체 검색 — 주간/야간/Office 메모 텍스트를 관통.</summary>
    Task<IReadOnlyList<MeetingSearchHitDto>> SearchMeetingAsync(string q);
}
