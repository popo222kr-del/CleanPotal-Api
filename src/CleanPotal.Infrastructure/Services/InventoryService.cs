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

    // 구역 4개 고정 + 위치명 문자열 휴리스틱 (WPF ClassifyZone). 순서: 메탈 → 논메탈 → OFFICE → 세정랩
    private static readonly (string Key, string Name)[] ZoneMeta =
    {
        ("metal", "METAL 반입구"), ("nonmetal", "N-METAL 출고실"), ("office", "Office 보관"), ("cleaning", "세정랩"),
    };
    private static string ClassifyZone(string loc)
    {
        var l = loc ?? "";
        if (l.Contains("논메탈")) return "nonmetal";                 // 논메탈이 메탈을 포함하므로 먼저 검사
        if (l.Contains("메탈") || l.Contains("반입구")) return "metal";
        if (l.ToUpperInvariant().Contains("OFFICE")) return "office";
        return "cleaning";                                          // 그 외 전부 세정랩으로 흡수
    }

    public async Task<IReadOnlyList<InventoryZoneDto>> GetByZoneAsync(string? search)
    {
        var q = _db.InventoryItems.AsQueryable();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(x => x.ItemName.Contains(search) || x.ItemCode.Contains(search)
                || x.Category.Contains(search) || x.Supplier.Contains(search));
        var items = await q.OrderBy(x => x.StorageLocation).ThenBy(x => x.OrderNo).ThenBy(x => x.Id).ToListAsync();
        var snap = await LatestSnapshotAsync();
        var byZone = items.GroupBy(x => ClassifyZone(x.StorageLocation))
            .ToDictionary(g => g.Key, g => g.ToList());
        // 4구역 고정 순서로 항상 반환 (빈 구역 포함)
        return ZoneMeta.Select(z =>
        {
            var list = byZone.GetValueOrDefault(z.Key, new());
            var locs = string.Join(" / ", list.Select(x => x.StorageLocation).Where(s => !string.IsNullOrEmpty(s)).Distinct());
            return new InventoryZoneDto(z.Key, z.Name, locs,
                list.Select(x => ToDto(x, snap.GetValueOrDefault(x.Id, ""))).ToList());
        }).ToList();
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
        var oldStock = x.CurrentStock;
        Apply(x, r);
        x.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        // 리베이스라인: 수동으로 현재고를 바꾸면(입고 등) "소비"가 아니므로 최신 스냅샷을 새 값으로 이동 → 증감 0
        if (x.CurrentStock != oldStock) await RebaselineAsync(x.Id, x.CurrentStock);
        var snap = await LatestSnapshotAsync();
        return ToDto(x, snap.GetValueOrDefault(x.Id, ""));
    }

    /// <summary>최신 스냅샷이 있으면 해당 품목 스냅샷 값을 새 현재고로 이동(없으면 무동작 = 최초 마감 전엔 증감 미산정).</summary>
    private async Task RebaselineAsync(int itemId, string newStock)
    {
        var latest = await _db.InventorySnapshots.OrderByDescending(s => s.SnapshotDate)
            .Select(s => s.SnapshotDate).FirstOrDefaultAsync();
        if (string.IsNullOrEmpty(latest)) return;
        var row = await _db.InventorySnapshots.FirstOrDefaultAsync(s => s.SnapshotDate == latest && s.ItemId == itemId);
        if (row is null) _db.InventorySnapshots.Add(new InventorySnapshot { ItemId = itemId, SnapshotDate = latest, Stock = newStock });
        else row.Stock = newStock;
        await _db.SaveChangesAsync();
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

    public async Task<int> ConfirmImportAsync(IReadOnlyList<InventoryImportRow> items)
    {
        if (items.Count == 0) return 0;
        // ① 오늘 스냅샷: 반영 전 현재고가 previous가 된다
        await CreateSnapshotAsync(null);
        // ② 스테이징 재고 반영 (UpdateAsync를 거치지 않으므로 리베이스라인 없음 → 증감이 소비로 잡힘)
        var ids = items.Select(i => i.Id).ToList();
        var rows = await _db.InventoryItems.Where(x => ids.Contains(x.Id)).ToListAsync();
        var now = DateTime.Now;
        int changed = 0;
        foreach (var st in items)
        {
            var x = rows.FirstOrDefault(r => r.Id == st.Id);
            if (x is null) continue;
            var nv = st.Stock ?? "";
            if (x.CurrentStock != nv) { x.CurrentStock = nv; x.UpdatedAt = now; changed++; }
        }
        await _db.SaveChangesAsync();
        return changed;
    }

    public async Task<IReadOnlyList<InventorySnapshotDto>> GetSnapshotsAsync(string? from, string? to)
    {
        // 스냅샷 수가 많지 않고 날짜는 yyyy-MM-dd(사전순=시간순) → 메모리에서 안전하게 필터
        var rows = await _db.InventorySnapshots.OrderBy(s => s.SnapshotDate).ToListAsync();
        IEnumerable<InventorySnapshot> q = rows;
        if (!string.IsNullOrEmpty(from)) q = q.Where(s => string.CompareOrdinal(s.SnapshotDate, from) >= 0);
        if (!string.IsNullOrEmpty(to)) q = q.Where(s => string.CompareOrdinal(s.SnapshotDate, to) <= 0);
        return q.Select(s => new InventorySnapshotDto(s.SnapshotDate, s.ItemId, s.Stock)).ToList();
    }

    public async Task<int> RenameLocationAsync(string oldName, string newName)
    {
        newName = (newName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(newName)) return 0;
        var items = await _db.InventoryItems.Where(x => x.StorageLocation == oldName).ToListAsync();
        foreach (var x in items) { x.StorageLocation = newName; x.UpdatedAt = DateTime.Now; }
        await _db.SaveChangesAsync();
        return items.Count;
    }

    public async Task<int> BulkUpdateAsync(InventoryBulkRequest req)
    {
        if (req.Ids is null || req.Ids.Count == 0) return 0;
        var items = await _db.InventoryItems.Where(x => req.Ids.Contains(x.Id)).ToListAsync();
        var now = DateTime.Now;
        var rebaseline = new List<int>();
        foreach (var x in items)
        {
            if (req.CurrentStock != null && x.CurrentStock != req.CurrentStock) { x.CurrentStock = req.CurrentStock; rebaseline.Add(x.Id); }
            if (req.AppropriateStock != null) x.AppropriateStock = req.AppropriateStock;
            if (req.Unit != null) x.Unit = req.Unit;
            if (req.Category != null) x.Category = req.Category;
            if (req.IsOrdered.HasValue) x.IsOrdered = req.IsOrdered.Value;
            x.UpdatedAt = now;
        }
        await _db.SaveChangesAsync();
        // 현재고 수동 일괄변경 → 리베이스라인(증감 0)
        foreach (var id in rebaseline) await RebaselineAsync(id, items.First(i => i.Id == id).CurrentStock);
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
