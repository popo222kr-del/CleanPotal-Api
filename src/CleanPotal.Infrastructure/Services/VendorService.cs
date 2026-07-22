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

    private static VendorDto ToDto(Vendor v) => new(
        v.Id, v.VendorName, v.Category, v.IsWeekly, v.IsFavorite,
        v.BasePath, v.LinkUrl, v.Addresses, v.Managers);

    public async Task<IReadOnlyList<VendorDto>> GetAllAsync(string? search)
    {
        var q = _db.Vendors.AsQueryable();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(v => v.VendorName.Contains(search) || v.Category.Contains(search) ||
                             v.Managers.Contains(search) || v.Addresses.Contains(search));
        // 즐겨찾기 먼저, 그다음 이름순
        var list = await q.OrderByDescending(v => v.IsFavorite).ThenBy(v => v.VendorName).ToListAsync();
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
        // 업체명·분류는 trim — 공백 차이로 분류 필터에 중복 칩이 생기는 것 방지
        v.VendorName = (r.VendorName ?? "").Trim();
        var cat = (r.Category ?? "").Trim();
        v.Category = cat.Length == 0 ? "일반" : cat;
        v.IsWeekly = r.IsWeekly;
        v.IsFavorite = r.IsFavorite;
        v.BasePath = (r.BasePath ?? "").Trim();
        v.LinkUrl = (r.LinkUrl ?? "").Trim();
        v.Addresses = r.Addresses ?? "";
        v.Managers = r.Managers ?? "";
    }

    public async Task<bool?> ToggleFavoriteAsync(int id)
    {
        var v = await _db.Vendors.FindAsync(id);
        if (v is null) return null;
        v.IsFavorite = !v.IsFavorite;
        await _db.SaveChangesAsync();
        return v.IsFavorite;
    }
}
