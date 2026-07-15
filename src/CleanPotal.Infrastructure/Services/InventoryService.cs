using System.Globalization;
using System.Text.RegularExpressions;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class InventoryService : IInventoryService
{
    private readonly CleanPotalDbContext _db;
    public InventoryService(CleanPotalDbContext db) => _db = db;

    // 순수 숫자(콤마 허용, 선행 숫자)면 값 반환 ("600매 이상" → 600), 아니면 null
    private static double? ParseNumber(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = Regex.Match(s.Trim().Replace(",", ""), @"^(\d+(?:\.\d+)?)");
        return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    // 순수 숫자면 단위 부착, 이미 문구/단위가 포함된 특수 표기는 그대로
    private static string WithUnit(string? value, string unit)
    {
        if (string.IsNullOrWhiteSpace(value)) return value ?? "";
        var t = value.Trim();
        return Regex.IsMatch(t.Replace(",", ""), @"^\d+(?:\.\d+)?$") && !string.IsNullOrWhiteSpace(unit) ? t + unit : value;
    }

    private static string FmtNum(double v) => v % 1 == 0 ? ((long)v).ToString() : v.ToString("0.##", CultureInfo.InvariantCulture);

    private static InventoryItemDto ToDto(InventoryItem x, string prevStock)
    {
        double? cur = ParseNumber(x.CurrentStock), prev = ParseNumber(prevStock), apt = ParseNumber(x.AppropriateStock);
        double? delta = (cur.HasValue && prev.HasValue) ? cur.Value - prev.Value : null;
        string deltaText = delta switch
        {
            null => "-",
            0 => "0",
            _ => (delta.Value > 0 ? "+" : "-") + FmtNum(Math.Abs(delta.Value)),
        };
        bool isLow = cur.HasValue && apt.HasValue && cur.Value <= apt.Value;
        return new(
            x.Id, x.OrderNo, x.ItemCode, x.Category, x.Unit, x.RegisteredDate,
            x.StorageLocation, x.ItemName,
            x.CurrentStock, WithUnit(x.CurrentStock, x.Unit),
            prevStock, WithUnit(prevStock, x.Unit), deltaText, delta is double dd && dd < 0,
            x.AppropriateStock, x.MinOrderQty, x.Supplier,
            x.OrderDate, x.OrderQty, x.ExpectedReceipt, x.Memo,
            x.IsOrdered, isLow, x.UpdatedAt);
    }

    // 최신 스냅샷의 품목별 재고
    private async Task<Dictionary<int, string>> LatestSnapshotAsync()
    {
        var latest = await _db.InventorySnapshots.OrderByDescending(s => s.SnapshotDate)
            .Select(s => s.SnapshotDate).FirstOrDefaultAsync();
        if (string.IsNullOrEmpty(latest)) return new();
        return await _db.InventorySnapshots.Where(s => s.SnapshotDate == latest)
            .ToDictionaryAsync(s => s.ItemId, s => s.Stock);
    }

    public async Task<IReadOnlyList<InventoryZoneDto>> GetByZoneAsync(string? search)
    {
        var q = _db.InventoryItems.AsQueryable();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(x => x.ItemName.Contains(search) || x.ItemCode.Contains(search)
                || x.Category.Contains(search) || x.Supplier.Contains(search));
        var items = await q.OrderBy(x => x.OrderNo).ThenBy(x => x.Id).ToListAsync();
        var snap = await LatestSnapshotAsync();
        return items
            .GroupBy(x => string.IsNullOrEmpty(x.StorageLocation) ? "미지정" : x.StorageLocation)
            .Select(g => new InventoryZoneDto(g.Key,
                g.Select(x => ToDto(x, snap.GetValueOrDefault(x.Id, ""))).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetLocationsAsync()
        => await _db.InventoryItems.Select(x => x.StorageLocation)
            .Where(l => l != "").Distinct().OrderBy(l => l).ToListAsync();

    public async Task<InventoryItemDto> CreateAsync(InventoryUpsertRequest r)
    {
        var maxOrder = await _db.InventoryItems.Select(x => (int?)x.OrderNo).MaxAsync() ?? 0;
        var x = new InventoryItem { OrderNo = maxOrder + 1, RegisteredDate = DateTime.Now.ToString("yyyy-MM-dd") };
        Apply(x, r);
        x.UpdatedAt = DateTime.Now;
        _db.InventoryItems.Add(x);
        await _db.SaveChangesAsync();
        return ToDto(x, "");
    }

    public async Task<InventoryItemDto?> UpdateAsync(int id, InventoryUpsertRequest r)
    {
        var x = await _db.InventoryItems.FindAsync(id);
        if (x is null) return null;
        Apply(x, r);
        x.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        var snap = await LatestSnapshotAsync();
        return ToDto(x, snap.GetValueOrDefault(x.Id, ""));
    }

    public async Task<InventoryItemDto?> SetOrderedAsync(int id, bool isOrdered)
    {
        var x = await _db.InventoryItems.FindAsync(id);
        if (x is null) return null;
        x.IsOrdered = isOrdered;
        x.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        var snap = await LatestSnapshotAsync();
        return ToDto(x, snap.GetValueOrDefault(x.Id, ""));
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var x = await _db.InventoryItems.FindAsync(id);
        if (x is null) return false;
        _db.InventoryItems.Remove(x);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<int> CreateSnapshotAsync(string? date)
    {
        var d = string.IsNullOrWhiteSpace(date) ? DateTime.Now.ToString("yyyy-MM-dd") : date.Trim();
        var old = await _db.InventorySnapshots.Where(s => s.SnapshotDate == d).ToListAsync();
        if (old.Count > 0) _db.InventorySnapshots.RemoveRange(old);
        var items = await _db.InventoryItems.ToListAsync();
        foreach (var x in items)
            _db.InventorySnapshots.Add(new InventorySnapshot { ItemId = x.Id, SnapshotDate = d, Stock = x.CurrentStock });
        await _db.SaveChangesAsync();
        return items.Count;
    }

    private static void Apply(InventoryItem x, InventoryUpsertRequest r)
    {
        x.ItemCode = r.ItemCode ?? "";
        x.Category = r.Category ?? "";
        x.Unit = r.Unit ?? "";
        x.StorageLocation = r.StorageLocation ?? "";
        x.ItemName = r.ItemName ?? "";
        x.CurrentStock = r.CurrentStock ?? "";
        x.AppropriateStock = r.AppropriateStock ?? "";
        x.MinOrderQty = r.MinOrderQty ?? "";
        x.Supplier = r.Supplier ?? "";
        x.OrderDate = r.OrderDate ?? "";
        x.OrderQty = r.OrderQty ?? "";
        x.ExpectedReceipt = r.ExpectedReceipt ?? "";
        x.Memo = r.Memo ?? "";
        x.IsOrdered = r.IsOrdered;
    }
}
