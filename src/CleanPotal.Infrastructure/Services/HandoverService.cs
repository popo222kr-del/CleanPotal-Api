using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class HandoverService : IHandoverService
{
    private readonly CleanPotalDbContext _db;
    public HandoverService(CleanPotalDbContext db) => _db = db;

    private static readonly string[] Statuses = { "진행", "포장", "완료" };

    /// <summary>업체명으로 분류 자동 판별 (WPF detect_category).</summary>
    private static string DetectCategory(string vendor)
    {
        var v = vendor.ToUpperInvariant();
        if (v.Contains("SEMES") || vendor.Contains("세메스")) return "SEMES";
        if (vendor.Contains("삼성") || v.Contains("SAMSUNG")) return "삼성";
        return "QTZ";
    }

    /// <summary>입고~출고 기준 진행률 (WPF CalcProgressPercent).</summary>
    private static int CalcProgress(Handover h)
    {
        if (h.Status == "완료") return 100;
        if (h.InDate is null || h.OutDate is null) return 0;
        var start = h.InDate.Value;
        var end = h.OutDate.Value;
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (end <= start) return today >= end ? 100 : 0;
        var p = (int)Math.Round((today.DayNumber - start.DayNumber) / (double)(end.DayNumber - start.DayNumber) * 100);
        return Math.Clamp(p, 0, 100);
    }

    private static HandoverDto ToDto(Handover h) => new(
        h.Id, h.Vendor, h.Category, h.Owner, h.Content, h.InDate, h.OutDate, h.Status,
        h.DeliveryMethod, h.Memo, CalcProgress(h), h.CreatorName, h.CreateDate, h.ModifierName, h.ModifyDate);

    public async Task<IReadOnlyList<HandoverDto>> GetAllAsync(string? status, string? category, string? search)
    {
        var q = _db.Handovers.AsQueryable();
        if (!string.IsNullOrEmpty(status) && status != "전체") q = q.Where(h => h.Status == status);
        if (!string.IsNullOrEmpty(category) && category != "전체") q = q.Where(h => h.Category == category);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(h => h.Vendor.Contains(search) || h.Content.Contains(search) || h.Owner.Contains(search));
        var items = await q.OrderByDescending(h => h.CreateDate).ToListAsync();
        return items.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyDictionary<string, int>> GetStatusCountsAsync()
    {
        var dict = new Dictionary<string, int>();
        foreach (var s in Statuses)
            dict[s] = await _db.Handovers.CountAsync(h => h.Status == s);
        return dict;
    }

    public async Task<HandoverDto> CreateAsync(HandoverUpsertRequest req, string actor)
    {
        var h = new Handover
        {
            Vendor = req.Vendor,
            Category = DetectCategory(req.Vendor),
            Owner = req.Owner,
            Content = req.Content,
            InDate = req.InDate,
            OutDate = req.OutDate,
            Status = "진행",
            DeliveryMethod = string.IsNullOrEmpty(req.DeliveryMethod) ? "미정" : req.DeliveryMethod,
            Memo = req.Memo,
            CreatorName = actor,
            CreateDate = DateTime.Now,
        };
        _db.Handovers.Add(h);
        await _db.SaveChangesAsync();
        return ToDto(h);
    }

    public async Task<HandoverDto?> UpdateAsync(int id, HandoverUpsertRequest req, string actor)
    {
        var h = await _db.Handovers.FindAsync(id);
        if (h is null) return null;
        h.Vendor = req.Vendor;
        h.Category = DetectCategory(req.Vendor);
        h.Owner = req.Owner;
        h.Content = req.Content;
        h.InDate = req.InDate;
        h.OutDate = req.OutDate;
        h.DeliveryMethod = req.DeliveryMethod;
        h.Memo = req.Memo;
        h.ModifierName = actor;
        h.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return ToDto(h);
    }

    public async Task<HandoverDto?> ChangeStatusAsync(int id, string status, string actor)
    {
        var h = await _db.Handovers.FindAsync(id);
        if (h is null) return null;
        h.Status = status;
        h.ModifierName = actor;
        h.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return ToDto(h);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var h = await _db.Handovers.FindAsync(id);
        if (h is null) return false;
        _db.Handovers.Remove(h);
        await _db.SaveChangesAsync();
        return true;
    }
}
