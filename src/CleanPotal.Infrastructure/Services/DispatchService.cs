using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class DispatchService : IDispatchService
{
    private readonly CleanPotalDbContext _db;
    public DispatchService(CleanPotalDbContext db) => _db = db;

    private static DispatchDto ToDto(Dispatch d) => new(
        d.Id, d.VendorName, d.OutgoingDetails, d.IncomingDetails, d.ManagerName,
        d.ContactNumber, d.FullAddress, d.Note, d.CreateDate);

    public async Task<IReadOnlyList<DispatchDto>> GetAllAsync(string? search)
    {
        var q = _db.Dispatches.AsQueryable();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(d => d.VendorName.Contains(search) || d.ManagerName.Contains(search) ||
                             d.FullAddress.Contains(search) || d.OutgoingDetails.Contains(search));
        var list = await q.OrderByDescending(d => d.CreateDate).ThenByDescending(d => d.Id).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<DispatchDto> CreateAsync(DispatchUpsertRequest r)
    {
        var d = new Dispatch { CreateDate = DateTime.Now };
        Apply(d, r);
        _db.Dispatches.Add(d);
        await _db.SaveChangesAsync();
        return ToDto(d);
    }

    public async Task<DispatchDto?> UpdateAsync(int id, DispatchUpsertRequest r)
    {
        var d = await _db.Dispatches.FindAsync(id);
        if (d is null) return null;
        Apply(d, r);
        await _db.SaveChangesAsync();
        return ToDto(d);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var d = await _db.Dispatches.FindAsync(id);
        if (d is null) return false;
        _db.Dispatches.Remove(d);
        await _db.SaveChangesAsync();
        return true;
    }

    private static void Apply(Dispatch d, DispatchUpsertRequest r)
    {
        d.VendorName = r.VendorName;
        d.OutgoingDetails = r.OutgoingDetails;
        d.IncomingDetails = r.IncomingDetails;
        d.ManagerName = r.ManagerName;
        d.ContactNumber = r.ContactNumber;
        d.FullAddress = r.FullAddress;
        d.Note = r.Note;
    }
}
