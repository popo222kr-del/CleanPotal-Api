using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class VendorService : IVendorService
{
    private readonly CleanPotalDbContext _db;
    public VendorService(CleanPotalDbContext db) => _db = db;

    private static VendorDto ToDto(Vendor v) => new(v.Id, v.VendorName, v.Category, v.IsWeekly, v.Contact, v.Phone, v.Note);

    public async Task<IReadOnlyList<VendorDto>> GetAllAsync(string? search)
    {
        var q = _db.Vendors.AsQueryable();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(v => v.VendorName.Contains(search) || v.Category.Contains(search) || v.Contact.Contains(search));
        var list = await q.OrderBy(v => v.VendorName).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<VendorDto> CreateAsync(VendorUpsertRequest r)
    {
        var v = new Vendor();
        Apply(v, r);
        _db.Vendors.Add(v);
        await _db.SaveChangesAsync();
        return ToDto(v);
    }

    public async Task<VendorDto?> UpdateAsync(int id, VendorUpsertRequest r)
    {
        var v = await _db.Vendors.FindAsync(id);
        if (v is null) return null;
        Apply(v, r);
        await _db.SaveChangesAsync();
        return ToDto(v);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var v = await _db.Vendors.FindAsync(id);
        if (v is null) return false;
        _db.Vendors.Remove(v);
        await _db.SaveChangesAsync();
        return true;
    }

    private static void Apply(Vendor v, VendorUpsertRequest r)
    {
        v.VendorName = r.VendorName;
        v.Category = string.IsNullOrEmpty(r.Category) ? "일반" : r.Category;
        v.IsWeekly = r.IsWeekly;
        v.Contact = r.Contact;
        v.Phone = r.Phone;
        v.Note = r.Note;
    }
}
