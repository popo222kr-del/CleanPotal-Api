using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Core.Security;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class ReportService : IReportService
{
    private readonly CleanPotalDbContext _db;
    private readonly ICurrentUser _me;
    public ReportService(CleanPotalDbContext db, ICurrentUser me) { _db = db; _me = me; }

    private static string What(Report r) => r.ReportType == "weekly" ? "주간보고" : "회의록";

    private static ReportBlockDto BlockDto(ReportBlock b) =>
        new(b.Id, b.Number, b.Category, b.Status, b.Content, b.ContentRich, b.FollowUp, b.FollowUpRich,
            b.Kind, b.Heading, b.IsCollapsed, b.ProgressPercent, b.Importance, b.FollowUpAttachments);

    private ReportDto ToDto(Report r) =>
        new(r.Id, r.ReportType, r.MonthTitle, r.Title, r.ShortTitle, r.DateRange,
            r.Memo, r.MemoRich, r.MainContent, r.MainContentRich, r.NightContent, r.NightContentRich,
            r.Attendees, r.Summary, r.MemoAttachments, r.MainAttachments, r.CreatedAt, r.UpdatedAt,
            r.Blocks.OrderBy(b => b.Number).ThenBy(b => b.Id).Select(BlockDto).ToList(),
            r.CreatorName, r.RowVersion,
            // 수정은 등급 2 면 공동으로 가능(주간·야간 팀이 각자 칸을 채운다), 삭제만 작성자/관리자
            ContentOwnership.IsOwnerOrAdmin(_me, r.CreatorUserId, r.CreatorName));

    public async Task<IReadOnlyList<ReportGroupDto>> GetGroupedAsync(string type)
    {
        type = string.IsNullOrWhiteSpace(type) ? "meeting" : type;
        var reports = await _db.Reports
            .Where(r => r.ReportType == type)
            .Include(r => r.Blocks)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Id)
            .ToListAsync();

        // MonthTitle 그룹, 등장 순서 유지
        var groups = new List<ReportGroupDto>();
        foreach (var r in reports)
        {
            var key = string.IsNullOrEmpty(r.MonthTitle) ? "기타" : r.MonthTitle;
            var g = groups.FirstOrDefault(x => x.MonthTitle == key);
            var summary = new ReportSummaryDto(r.Id, r.Title, r.ShortTitle, r.DateRange, r.Blocks.Count,
                !string.IsNullOrWhiteSpace(r.Memo),
                !string.IsNullOrWhiteSpace(r.MainContent) || !string.IsNullOrWhiteSpace(r.NightContent));
            if (g is null) groups.Add(new ReportGroupDto(key, new List<ReportSummaryDto> { summary }));
            else ((List<ReportSummaryDto>)g.Reports).Add(summary);
        }
        return groups;
    }

    public async Task<ReportDto?> GetAsync(int id)
    {
        var r = await _db.Reports.Include(x => x.Blocks).FirstOrDefaultAsync(x => x.Id == id);
        return r is null ? null : ToDto(r);
    }

    public Task<string?> GetTypeAsync(int id)
        => _db.Reports.Where(x => x.Id == id).Select(x => (string?)x.ReportType).FirstOrDefaultAsync();

    /// <summary>종류는 두 가지뿐 — 모르는 값은 회의록으로 본다(예전 동작).</summary>
    public static string NormalizeType(string? type) => type == "weekly" ? "weekly" : "meeting";

    public async Task<ReportDto> CreateAsync(ReportUpsertRequest req)
    {
        var type = NormalizeType(req.ReportType);
        var maxOrder = await _db.Reports.Where(r => r.ReportType == type)
            .Select(r => (int?)r.SortOrder).MaxAsync() ?? 0;
        var r = new Report
        {
            CreatedAt = DateTime.Now,
            SortOrder = maxOrder + 1,
            CreatorName = _me.RealName,
            CreatorUserId = _me.Id,   // 작성자는 이름이 아니라 계정 ID 로 기록
            ReportType = type,        // 종류는 만들 때만 정한다(수정으로 다른 메뉴로 옮기지 못하게)
        };
        ApplyHead(r, req);
        ApplyBlocks(r, req);
        _db.Reports.Add(r);
        await _db.SaveChangesAsync();
        ContentAuditWriter.Add(_db, _me, What(r), r.Id, "생성", r.Title);
        await _db.SaveChangesAsync();
        return ToDto(r);
    }

    public async Task<ReportDto?> UpdateAsync(int id, ReportUpsertRequest req)
    {
        var r = await _db.Reports.Include(x => x.Blocks).FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return null;
        ContentAuditWriter.EnsureNotStale(req.RowVersion, r.RowVersion, What(r));
        var detail = ContentAuditWriter.Describe(
            ("제목", r.Title, req.Title),
            ("주간", r.MainContent, req.MainContent),
            ("야간", r.NightContent, req.NightContent),
            ("메모", r.Memo, req.Memo),
            ("항목 수", r.Blocks.Count.ToString(), req.Blocks.Count.ToString()));

        ApplyHead(r, req);
        r.UpdatedAt = DateTime.Now;
        r.RowVersion++;
        // 후속조치 첨부 — 새 base64 는 거절. 새 보고서를 만들 때는 앞 주차 블록을 그대로 이월하므로
        // (옛 기록의 base64 가 따라온다) 만들 때는 보지 않고, 고칠 때 전 블록들에 없던 것만 거절한다.
        var oldAtts = string.Join("\n", r.Blocks.Select(b => b.FollowUpAttachments));
        foreach (var b in req.Blocks ?? [])
            InlineDataGuard.EnsureNoNewInline(b.FollowUpAttachments, oldAtts, "후속조치 첨부");
        _db.ReportBlocks.RemoveRange(r.Blocks);
        r.Blocks.Clear();
        ApplyBlocks(r, req);
        ContentAuditWriter.Add(_db, _me, What(r), r.Id, "수정", detail);
        await ContentAuditWriter.SaveAsync(_db, What(r));
        return ToDto(r);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var r = await _db.Reports.FindAsync(id);
        if (r is null) return false;
        // 삭제는 수정과 별도 정책 — 작성자 본인 또는 관리자만.
        // (WPF 에서 넘어온 과거 자료는 작성자가 기록돼 있지 않아 '작성자 미상'으로 통과한다)
        ContentOwnership.EnsureOwnerOrAdmin(_me, r.CreatorUserId, r.CreatorName, What(r), "삭제");

        ContentAuditWriter.Add(_db, _me, What(r), r.Id, "삭제", r.Title);
        _db.Reports.Remove(r);   // 블록 Cascade 삭제
        await ContentAuditWriter.SaveAsync(_db, What(r));
        return true;
    }

    /// <summary>전역 블록 검색 — 모든 주차의 카테고리/내용/팔로업을 관통 (WPF 전체 검색).</summary>
    public async Task<IReadOnlyList<ReportSearchHitDto>> SearchBlocksAsync(string type, string q)
    {
        q = (q ?? "").Trim();
        if (q.Length == 0) return Array.Empty<ReportSearchHitDto>();
        type = string.IsNullOrWhiteSpace(type) ? "weekly" : type;
        var reports = await _db.Reports
            .Where(r => r.ReportType == type)
            .Include(r => r.Blocks)
            .ToListAsync();
        return reports
            .OrderByDescending(r => r.DateRange).ThenByDescending(r => r.Id)
            .SelectMany(r => r.Blocks
                .Where(b => b.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            b.Content.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            b.FollowUp.Contains(q, StringComparison.OrdinalIgnoreCase))
                .OrderBy(b => b.Number)
                .Select(b => new ReportSearchHitDto(r.Id, r.ShortTitle, r.Title, r.DateRange, BlockDto(b))))
            .ToList();
    }

    /// <summary>생산팀 인수인계(회의록) 전체 검색. 블록이 없는 구조라 주간/야간/Office 메모
    /// 텍스트를 직접 관통한다 — 한 보고서에서 여러 칸이 걸리면 칸마다 결과를 하나씩 낸다.</summary>
    public async Task<IReadOnlyList<MeetingSearchHitDto>> SearchMeetingAsync(string q)
    {
        q = (q ?? "").Trim();
        if (q.Length == 0) return Array.Empty<MeetingSearchHitDto>();
        var reports = await _db.Reports.Where(r => r.ReportType == "meeting").ToListAsync();

        bool Hit(string s) => !string.IsNullOrEmpty(s) && s.Contains(q, StringComparison.OrdinalIgnoreCase);

        var hits = new List<MeetingSearchHitDto>();
        foreach (var r in reports.OrderByDescending(r => r.DateRange).ThenByDescending(r => r.Id))
        {
            if (Hit(r.MainContent)) hits.Add(new(r.Id, r.ShortTitle, r.Title, r.DateRange, "주간", r.MainContent));
            if (Hit(r.NightContent)) hits.Add(new(r.Id, r.ShortTitle, r.Title, r.DateRange, "야간", r.NightContent));
            if (Hit(r.Memo)) hits.Add(new(r.Id, r.ShortTitle, r.Title, r.DateRange, "Office 메모", r.Memo));
        }
        return hits;
    }

    private static void ApplyHead(Report r, ReportUpsertRequest q)
    {
        r.MonthTitle = q.MonthTitle;
        r.Title = q.Title;
        r.ShortTitle = q.ShortTitle;
        r.DateRange = q.DateRange;
        r.Memo = q.Memo;
        r.MemoRich = q.MemoRich;
        r.MainContent = q.MainContent;
        r.MainContentRich = q.MainContentRich;
        r.NightContent = q.NightContent;
        r.NightContentRich = q.NightContentRich;
        r.Attendees = q.Attendees;
        r.Summary = q.Summary;
        InlineDataGuard.EnsureNoNewInline(q.MemoAttachments, r.MemoAttachments, "메모 첨부");
        InlineDataGuard.EnsureNoNewInline(q.MainAttachments, r.MainAttachments, "본문 첨부");
        r.MemoAttachments = q.MemoAttachments;
        r.MainAttachments = q.MainAttachments;
    }

    private static void ApplyBlocks(Report r, ReportUpsertRequest q)
    {
        int no = 1;
        foreach (var b in q.Blocks ?? new List<ReportBlockInput>())
        {
            r.Blocks.Add(new ReportBlock
            {
                Number = b.Number > 0 ? b.Number : no,
                Category = b.Category, Status = b.Status,
                Content = b.Content, ContentRich = b.ContentRich,
                FollowUp = b.FollowUp, FollowUpRich = b.FollowUpRich,
                Kind = b.Kind, Heading = b.Heading,
                IsCollapsed = b.IsCollapsed, ProgressPercent = b.ProgressPercent,
                Importance = b.Importance, FollowUpAttachments = b.FollowUpAttachments,
            });
            no++;
        }
    }
}
