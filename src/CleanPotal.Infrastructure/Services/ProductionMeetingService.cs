using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class ProductionMeetingService : IProductionMeetingService
{
    private readonly CleanPotalDbContext _db;
    public ProductionMeetingService(CleanPotalDbContext db) => _db = db;

    // 해당 날짜에 어느 팀이 주간/야간인지 예측 (김팀/장팀 2주 교대)
    private static (string day, string night) PredictTeams(DateOnly date)
    {
        var kim = ShiftPredictor.Predict("김팀", date);
        return kim == "주간" ? ("김팀", "장팀") : ("장팀", "김팀");
    }

    private static ProductionMeetingDto ToDto(ProductionMeeting m)
    {
        var (day, night) = PredictTeams(m.MeetingDate);
        return new(m.Id, m.Title, m.MeetingDate, m.DayContent, m.NightContent, m.OfficeMemo,
            day, night, m.CreatorName, m.CreatedAt, m.UpdatedAt);
    }

    public async Task<IReadOnlyList<ProductionMeetingGroupDto>> GetGroupedAsync()
    {
        var items = await _db.ProductionMeetings.OrderByDescending(m => m.MeetingDate).ToListAsync();
        return items
            .GroupBy(m => $"{m.MeetingDate.Year}년 {m.MeetingDate.Month}월")
            .Select(g => new ProductionMeetingGroupDto(g.Key, g.Select(ToDto).ToList()))
            .ToList();
    }

    public async Task<ProductionMeetingDto?> GetAsync(int id)
    {
        var m = await _db.ProductionMeetings.FindAsync(id);
        return m is null ? null : ToDto(m);
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
            CreatedAt = DateTime.Now,
        };
        _db.ProductionMeetings.Add(m);
        await _db.SaveChangesAsync();
        return ToDto(m);
    }

    public async Task<ProductionMeetingDto?> UpdateAsync(int id, ProductionMeetingUpsertRequest req)
    {
        var m = await _db.ProductionMeetings.FindAsync(id);
        if (m is null) return null;
        m.Title = req.Title;
        m.MeetingDate = req.MeetingDate;
        m.DayContent = req.DayContent;
        m.NightContent = req.NightContent;
        m.OfficeMemo = req.OfficeMemo;
        m.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return ToDto(m);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var m = await _db.ProductionMeetings.FindAsync(id);
        if (m is null) return false;
        _db.ProductionMeetings.Remove(m);
        await _db.SaveChangesAsync();
        return true;
    }
}
