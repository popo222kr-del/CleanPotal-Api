using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Core.Security;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class ProductionMeetingService : IProductionMeetingService
{
    private const string What = "생산미팅";

    private readonly CleanPotalDbContext _db;
    private readonly ICurrentUser _me;
    public ProductionMeetingService(CleanPotalDbContext db, ICurrentUser me) { _db = db; _me = me; }

    // 해당 날짜에 어느 팀이 주간/야간인지 예측 (2주 교대).
    // 팀 이름은 조직도에서 읽으므로 이름을 바꿔도 라벨이 따라온다.
    private static (string day, string night) PredictTeams(ProductionTeams pt, DateOnly date)
    {
        string day = "", night = "";
        foreach (var team in pt.Names)
        {
            var st = pt.PredictShift(team, date);
            if (st == "주간") day = team;
            else if (st == "야간") night = team;
        }
        return (day, night);
    }

    private ProductionMeetingDto ToDto(ProductionMeeting m, ProductionTeams pt)
    {
        var (day, night) = PredictTeams(pt, m.MeetingDate);
        return new(m.Id, m.Title, m.MeetingDate, m.DayContent, m.NightContent, m.OfficeMemo,
            day, night, m.CreatorName, m.CreatedAt, m.UpdatedAt,
            m.RowVersion,
            ContentOwnership.IsOwnerOrAdmin(_me, m.CreatorUserId, m.CreatorName));
    }

    public async Task<IReadOnlyList<ProductionMeetingGroupDto>> GetGroupedAsync()
    {
        var items = await _db.ProductionMeetings.OrderByDescending(m => m.MeetingDate).ToListAsync();
        var pt = await ProductionTeams.LoadAsync(_db);
        return items
            .GroupBy(m => $"{m.MeetingDate.Year}년 {m.MeetingDate.Month}월")
            .Select(g => new ProductionMeetingGroupDto(g.Key, g.Select(m => ToDto(m, pt)).ToList()))
            .ToList();
    }

    public async Task<ProductionMeetingDto?> GetAsync(int id)
    {
        var m = await _db.ProductionMeetings.FindAsync(id);
        return m is null ? null : ToDto(m, await ProductionTeams.LoadAsync(_db));
    }

    public async Task<ProductionMeetingDto> CreateAsync(ProductionMeetingUpsertRequest req, string actor)
    {
        var m = new ProductionMeeting
        {
            Title = string.IsNullOrWhiteSpace(req.Title) ? $"{req.MeetingDate:MM-dd} 생산미팅" : req.Title,
            MeetingDate = req.MeetingDate,
            DayContent = req.DayContent,
            NightContent = req.NightContent,
            OfficeMemo = req.OfficeMemo,
            CreatorName = actor,
            CreatorUserId = _me.Id,   // 작성자는 이름이 아니라 계정 ID 로 기록
            CreatedAt = DateTime.Now,
        };
        _db.ProductionMeetings.Add(m);
        await _db.SaveChangesAsync();
        ContentAuditWriter.Add(_db, _me, What, m.Id, "생성", m.Title);
        await _db.SaveChangesAsync();
        return ToDto(m, await ProductionTeams.LoadAsync(_db));
    }

    public async Task<ProductionMeetingDto?> UpdateAsync(int id, ProductionMeetingUpsertRequest req)
    {
        var m = await _db.ProductionMeetings.FindAsync(id);
        if (m is null) return null;
        // 생산미팅 기록은 주간·야간 팀이 각자 칸을 채우는 공동 업무 → 등급 2 면 수정 가능.
        ContentAuditWriter.EnsureNotStale(req.RowVersion, m.RowVersion, What);
        var detail = ContentAuditWriter.Describe(
            ("제목", m.Title, req.Title),
            ("주간", m.DayContent, req.DayContent),
            ("야간", m.NightContent, req.NightContent),
            ("Office 메모", m.OfficeMemo, req.OfficeMemo));

        m.Title = req.Title;
        m.MeetingDate = req.MeetingDate;
        m.DayContent = req.DayContent;
        m.NightContent = req.NightContent;
        m.OfficeMemo = req.OfficeMemo;
        m.UpdatedAt = DateTime.Now;
        m.RowVersion++;
        ContentAuditWriter.Add(_db, _me, What, m.Id, "수정", detail);
        await ContentAuditWriter.SaveAsync(_db, What);
        return ToDto(m, await ProductionTeams.LoadAsync(_db));
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var m = await _db.ProductionMeetings.FindAsync(id);
        if (m is null) return false;
        // 삭제는 수정과 별도 정책 — 작성자 본인 또는 관리자만.
        ContentOwnership.EnsureOwnerOrAdmin(_me, m.CreatorUserId, m.CreatorName, What, "삭제");

        ContentAuditWriter.Add(_db, _me, What, m.Id, "삭제", m.Title);
        _db.ProductionMeetings.Remove(m);
        await ContentAuditWriter.SaveAsync(_db, What);
        return true;
    }
}
