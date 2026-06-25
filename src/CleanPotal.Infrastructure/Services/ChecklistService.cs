using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class ChecklistService : IChecklistService
{
    private readonly CleanPotalDbContext _db;
    public ChecklistService(CleanPotalDbContext db) => _db = db;

    private static readonly ZoneDto[] Zones =
    {
        new("metal_in", "입고검사실", "METAL"),
        new("metal_out", "출고검사실", "METAL"),
        new("nonmetal_in", "입고검사실", "NON-METAL"),
        new("nonmetal_out", "출고검사실", "NON-METAL"),
    };

    public IReadOnlyList<ZoneDto> GetZones() => Zones;

    private static InspectionItemDto ToDto(InspectionItem i) => new(i.Id, i.Zone, i.SortOrder, i.Text);
    private static InspectionRecordDto ToDto(InspectionRecord r) =>
        new(r.Id, r.Zone, r.Date, r.Shift, r.Worker, r.CheckedCount, r.TotalCount, r.SubmittedAt);

    public async Task<IReadOnlyList<InspectionItemDto>> GetItemsAsync(string zone)
    {
        var items = await _db.InspectionItems.Where(i => i.Zone == zone)
            .OrderBy(i => i.SortOrder).ToListAsync();
        return items.Select(ToDto).ToList();
    }

    public async Task<InspectionItemDto> AddItemAsync(InspectionItemRequest req)
    {
        int max = await _db.InspectionItems.Where(i => i.Zone == req.Zone).AnyAsync()
            ? await _db.InspectionItems.Where(i => i.Zone == req.Zone).MaxAsync(i => i.SortOrder) : 0;
        var item = new InspectionItem { Zone = req.Zone, Text = req.Text, SortOrder = max + 1 };
        _db.InspectionItems.Add(item);
        await _db.SaveChangesAsync();
        return ToDto(item);
    }

    public async Task<bool> DeleteItemAsync(int id)
    {
        var item = await _db.InspectionItems.FindAsync(id);
        if (item is null) return false;
        _db.InspectionItems.Remove(item);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<InspectionRecordDto> SubmitAsync(SubmitInspectionRequest req, string worker)
    {
        // 같은 구역·날짜·교대 기존 제출은 덮어쓰기
        var existing = await _db.InspectionRecords
            .FirstOrDefaultAsync(r => r.Zone == req.Zone && r.Date == req.Date && r.Shift == req.Shift);
        if (existing is not null) _db.InspectionRecords.Remove(existing);

        var rec = new InspectionRecord
        {
            Zone = req.Zone, Date = req.Date, Shift = req.Shift, Worker = worker,
            CheckedCount = req.CheckedCount, TotalCount = req.TotalCount, SubmittedAt = DateTime.Now,
        };
        _db.InspectionRecords.Add(rec);
        await _db.SaveChangesAsync();
        return ToDto(rec);
    }

    public async Task<IReadOnlyList<InspectionRecordDto>> GetRecordsAsync(DateOnly date, string? zone)
    {
        var q = _db.InspectionRecords.Where(r => r.Date == date);
        if (!string.IsNullOrEmpty(zone)) q = q.Where(r => r.Zone == zone);
        var recs = await q.ToListAsync();
        return recs.Select(ToDto).ToList();
    }
}
