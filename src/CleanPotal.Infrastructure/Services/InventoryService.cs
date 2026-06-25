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

    private static InventoryItemDto ToDto(InventoryItem x) => new(
        x.Id, x.ItemCode, x.ItemName, x.Category, x.Unit, x.StorageLocation,
        x.CurrentStock, x.AppropriateStock, x.PreviousStock, x.OrderDate, x.ExpectedDate, x.Note,
        x.AppropriateStock > 0 && x.CurrentStock < x.AppropriateStock, x.UpdatedAt);

    public async Task<IReadOnlyList<InventoryZoneDto>> GetByZoneAsync(string? search)
    {
        var q = _db.InventoryItems.AsQueryable();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(x => x.ItemName.Contains(search) || x.ItemCode.Contains(search) || x.Category.Contains(search));
        var items = await q.OrderBy(x => x.StorageLocation).ThenBy(x => x.ItemName).ToListAsync();
        return items
            .GroupBy(x => string.IsNullOrEmpty(x.StorageLocation) ? "미지정" : x.StorageLocation)
            .Select(g => new InventoryZoneDto(g.Key, g.Select(ToDto).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetLocationsAsync()
        => await _db.InventoryItems.Select(x => x.StorageLocation)
            .Where(l => l != "").Distinct().OrderBy(l => l).ToListAsync();

    public async Task<InventoryItemDto> CreateAsync(InventoryUpsertRequest r)
    {
        var x = new InventoryItem();
        Apply(x, r);
        x.UpdatedAt = DateTime.Now;
        _db.InventoryItems.Add(x);
        await _db.SaveChangesAsync();
        return ToDto(x);
    }

    public async Task<InventoryItemDto?> UpdateAsync(int id, InventoryUpsertRequest r)
    {
        var x = await _db.InventoryItems.FindAsync(id);
        if (x is null) return null;
        Apply(x, r);
        x.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return ToDto(x);
    }

    public async Task<InventoryItemDto?> SetStockAsync(int id, decimal current)
    {
        var x = await _db.InventoryItems.FindAsync(id);
        if (x is null) return null;
        x.CurrentStock = current;
        x.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return ToDto(x);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var x = await _db.InventoryItems.FindAsync(id);
        if (x is null) return false;
        _db.InventoryItems.Remove(x);
        await _db.SaveChangesAsync();
        return true;
    }

    private static void Apply(InventoryItem x, InventoryUpsertRequest r)
    {
        x.ItemCode = r.ItemCode;
        x.ItemName = r.ItemName;
        x.Category = r.Category;
        x.Unit = string.IsNullOrEmpty(r.Unit) ? "EA" : r.Unit;
        x.StorageLocation = r.StorageLocation;
        x.CurrentStock = r.CurrentStock;
        x.AppropriateStock = r.AppropriateStock;
        x.PreviousStock = r.PreviousStock;
        x.OrderDate = r.OrderDate;
        x.ExpectedDate = r.ExpectedDate;
        x.Note = r.Note;
    }
}
