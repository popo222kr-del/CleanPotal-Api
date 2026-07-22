using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class ReportService : IReportService
{
    private readonly CleanPotalDbContext _db;
    public ReportService(CleanPotalDbContext db) => _db = db;

    private static ReportBlockDto BlockDto(ReportBlock b) =>
        new(b.Id, b.Number, b.Category, b.Status, b.Content, b.ContentRich, b.FollowUp, b.FollowUpRich,
            b.Kind, b.Heading, b.IsCollapsed, b.ProgressPercent, b.Importance, b.FollowUpAttachments);

    private static ReportDto ToDto(Report r) =>
        new(r.Id, r.ReportType, r.MonthTitle, r.Title, r.ShortTitle, r.DateRange,
            r.Memo, r.MemoRich, r.MainContent, r.MainContentRich, r.NightContent, r.NightContentRich,
            r.Attendees, r.Summary, r.MemoAttachments, r.MainAttachments, r.CreatedAt, r.UpdatedAt,
            r.Blocks.OrderBy(b => b.Number).ThenBy(b => b.Id).Select(BlockDto).ToList());

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

    public async Task<ReportDto> CreateAsync(ReportUpsertRequest req)
    {
        var maxOrder = await _db.Reports.Where(r => r.ReportType == req.ReportType)
            .Select(r => (int?)r.SortOrder).MaxAsync() ?? 0;
        var r = new Report { CreatedAt = DateTime.Now, SortOrder = maxOrder + 1 };
        ApplyHead(r, req);
        ApplyBlocks(r, req);
        _db.Reports.Add(r);
        await _db.SaveChangesAsync();
        return ToDto(r);
    }

    public async Task<ReportDto?> UpdateAsync(int id, ReportUpsertRequest req)
    {
        var r = await _db.Reports.Include(x => x.Blocks).FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return null;
        ApplyHead(r, req);
        r.UpdatedAt = DateTime.Now;
        _db.ReportBlocks.RemoveRange(r.Blocks);
        r.Blocks.Clear();
        ApplyBlocks(r, req);
        await _db.SaveChangesAsync();
        return ToDto(r);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var r = await _db.Reports.FindAsync(id);
        if (r is null) return false;
        _db.Reports.Remove(r);   // 블록 Cascade 삭제
        await _db.SaveChangesAsync();
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

    private static void ApplyHead(Report r, ReportUpsertRequest q)
    {
        r.ReportType = string.IsNullOrWhiteSpace(q.ReportType) ? "meeting" : q.ReportType;
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
