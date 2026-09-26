using CleanPotal.Core;
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
        v.BasePath, v.LinkUrl, v.Addresses, v.Managers, v.MesCustomerId);

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

    /// <summary>
    /// 업체명은 겹치지 않게 — 기타세정/주간세정 현황은 업체를 이름으로 찾아 나누므로,
    /// 같은 이름이 둘이면 어느 쪽 "주간세정" 표시를 따를지 정해지지 않는다.
    /// </summary>
    private async Task EnsureNameFreeAsync(string name, int? selfId)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) throw new BusinessRuleException("업체명을 입력하세요.");
        if (await _db.Vendors.AnyAsync(v => v.Id != (selfId ?? 0) && v.VendorName.Trim() == n))
            throw new BusinessRuleException($"'{n}' 업체가 이미 있습니다. 기존 업체를 고쳐 주세요.");
    }

    public async Task<VendorDto> CreateAsync(VendorUpsertRequest r)
    {
        await EnsureNameFreeAsync(r.VendorName, null);
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
        await EnsureNameFreeAsync(r.VendorName, id);
        var oldName = v.VendorName.Trim();
        Apply(v, r);
        await using var tx = await _db.Database.BeginTransactionAsync();
        await _db.SaveChangesAsync();
        // 이름을 바꾸면 그 업체의 세정 현황도 새 이름으로 — 현황은 업체를 이름으로 찾아 주간세정/기타세정을
        // 나누므로, 예전에는 주간세정 업체 이름을 바꾸는 순간 진행 중 항목이 기타세정 목록으로 떨어졌다.
        // 버전을 올려 그 항목을 열어 둔 사람이 저장하면 "다른 사람이 먼저 고쳤다" 로 알게 한다.
        if (oldName.Length > 0 && oldName != v.VendorName)
            await _db.Handovers.Where(h => h.Vendor.Trim() == oldName)
                .ExecuteUpdateAsync(u => u.SetProperty(h => h.Vendor, v.VendorName).SetProperty(h => h.RowVersion, h => h.RowVersion + 1));
        await tx.CommitAsync();
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
        v.MesCustomerId = r.MesCustomerId;
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
