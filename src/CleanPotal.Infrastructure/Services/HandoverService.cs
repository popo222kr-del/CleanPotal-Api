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

    /// <summary>현재 사용자 기준 미확인 여부 (WPF IsNewUpdate).
    /// 24h 내 타인이 등록/수정했고, 현재 사용자가 ReadBy에 없으면 true.</summary>
    private static bool CalcNewUpdate(Handover h, string actor)
    {
        if (string.IsNullOrEmpty(actor)) return false;
        var readers = (h.ReadBy ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (readers.Contains(actor)) return false;
        var now = DateTime.Now;
        bool createdByOther = h.CreatorName != actor && (now - h.CreateDate).TotalHours <= 24;
        bool modifiedByOther = h.ModifyDate is { } md && h.ModifierName != actor && (now - md).TotalHours <= 24;
        return createdByOther || modifiedByOther;
    }

    private static HandoverDto ToDto(Handover h, string actor = "") => new(
        h.Id, h.Vendor, h.Category, h.Owner, h.Content, h.InDate, h.OutDate, h.Status,
        h.DeliveryMethod, h.Memo, h.IsWeekly, CalcProgress(h), h.CreatorName, h.CreateDate, h.ModifierName, h.ModifyDate,
        CalcNewUpdate(h, actor));

    public async Task<IReadOnlyList<HandoverDto>> GetAllAsync(string? status, string? category, string? search, bool weekly, string actor = "")
    {
        var q = _db.Handovers.Where(h => h.IsWeekly == weekly);
        if (!string.IsNullOrEmpty(status) && status != "전체") q = q.Where(h => h.Status == status);
        if (!string.IsNullOrEmpty(category) && category != "전체") q = q.Where(h => h.Category == category);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(h => h.Vendor.Contains(search) || h.Content.Contains(search) || h.Owner.Contains(search));
        var items = await q.OrderByDescending(h => h.CreateDate).ToListAsync();
        return items.Select(h => ToDto(h, actor)).ToList();
    }

    public async Task<bool> MarkReadAsync(int id, string actor)
    {
        if (string.IsNullOrEmpty(actor)) return false;
        var h = await _db.Handovers.FindAsync(id);
        if (h is null) return false;
        var readers = (h.ReadBy ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (readers.Contains(actor)) return true;
        readers.Add(actor);
        h.ReadBy = string.Join(",", readers);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<IReadOnlyDictionary<string, int>> GetStatusCountsAsync(bool weekly)
    {
        var dict = new Dictionary<string, int>();
        foreach (var s in Statuses)
            dict[s] = await _db.Handovers.CountAsync(h => h.Status == s && h.IsWeekly == weekly);
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
            Status = string.IsNullOrEmpty(req.Status) ? "진행" : req.Status,
            DeliveryMethod = string.IsNullOrEmpty(req.DeliveryMethod) ? "미정" : req.DeliveryMethod,
            Memo = req.Memo,
            IsWeekly = req.IsWeekly,
            CreatorName = actor,
            CreateDate = DateTime.Now,
            ReadBy = actor,   // 등록자는 자동 읽음
        };
        _db.Handovers.Add(h);
        await _db.SaveChangesAsync();
        return ToDto(h, actor);
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
        h.IsWeekly = req.IsWeekly;
        if (!string.IsNullOrEmpty(req.Status)) h.Status = req.Status;
        h.ModifierName = actor;
        h.ModifyDate = DateTime.Now;
        h.ReadBy = actor;   // 수정 시 읽음 초기화 (수정자만 읽음) → 타인에게 빨간 점
        await _db.SaveChangesAsync();
        return ToDto(h, actor);
    }

    public async Task<HandoverDto?> ChangeStatusAsync(int id, string status, string actor)
    {
        var h = await _db.Handovers.FindAsync(id);
        if (h is null) return null;
        h.Status = status;
        h.ModifierName = actor;
        h.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return ToDto(h, actor);
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
