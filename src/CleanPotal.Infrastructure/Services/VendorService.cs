using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// 업체 관리. 업체는 부서마다 따로 등록한다(같은 업체라도 세정·연구소가 각자) — 다른 부서 업체는 보지도 고치지도 못한다(DeptScope).
/// </summary>
public class VendorService : IVendorService
{
    private const string What = "업체";
    private readonly CleanPotalDbContext _db;
    private readonly DeptScope _dept;

    /// <summary>로그인 사용자 없이(테스트·가져오기) — 부서 범위를 두지 않는다.</summary>
    public VendorService(CleanPotalDbContext db) : this(db, null) { }

    public VendorService(CleanPotalDbContext db, ICurrentUser? me)
    {
        _db = db;
        _dept = new DeptScope(db, me);
    }

    private VendorDto ToDto(Vendor v) => new(
        v.Id, v.VendorName, v.Category, v.IsWeekly, v.IsFavorite,
        v.BasePath, v.LinkUrl, v.Addresses, v.Managers, v.MesCustomerId,
        v.DeptId, _dept.NameOf(v.DeptId));

    public async Task<IReadOnlyList<VendorDto>> GetAllAsync(string? search, bool mineOnly = false)
    {
        var q = await _dept.FilterAsync(_db.Vendors.AsQueryable(), v => v.DeptId, mineOnly);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(v => v.VendorName.Contains(search) || v.Category.Contains(search) ||
                             v.Managers.Contains(search) || v.Addresses.Contains(search));
        // 즐겨찾기 먼저, 그다음 이름순
        var list = await q.OrderByDescending(v => v.IsFavorite).ThenBy(v => v.VendorName).ToListAsync();
        await _dept.NamesAsync();
        return list.Select(ToDto).ToList();
    }

    /// <summary>
    /// 업체명은 한 부서 안에서 겹치지 않게 — 기타세정/주간세정 현황은 업체를 이름으로 찾아 나누므로,
    /// 같은 이름이 둘이면 어느 쪽 "주간세정" 표시를 따를지 정해지지 않는다. 다른 부서는 같은 업체를 따로 등록한다.
    /// </summary>
    private async Task EnsureNameFreeAsync(string name, int? selfId, int? dept)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) throw new BusinessRuleException("업체명을 입력하세요.");
        if (await _db.Vendors.AnyAsync(v => v.Id != (selfId ?? 0) && v.DeptId == dept && v.VendorName.Trim() == n))
            throw new BusinessRuleException($"'{n}' 업체가 이미 있습니다. 기존 업체를 고쳐 주세요.");
    }

    public async Task<VendorDto> CreateAsync(VendorUpsertRequest r)
    {
        var dept = await _dept.ForCreateAsync(r.DeptId);
        await EnsureNameFreeAsync(r.VendorName, null, dept);
        var v = new Vendor { DeptId = dept };
        Apply(v, r);
        _db.Vendors.Add(v);
        await _db.SaveChangesAsync();
        await _dept.NamesAsync();
        return ToDto(v);
    }

    public async Task<VendorDto?> UpdateAsync(int id, VendorUpsertRequest r)
    {
        var v = await _db.Vendors.FindAsync(id);
        if (v is null) return null;
        await _dept.EnsureAsync(v.DeptId, What);
        // 등록 부서는 관리자만 옮긴다.
        var dept = _dept.Unrestricted && r.DeptId is not null ? await _dept.ForCreateAsync(r.DeptId) : v.DeptId;
        await EnsureNameFreeAsync(r.VendorName, id, dept);
        var oldName = v.VendorName.Trim();
        Apply(v, r);
        v.DeptId = dept;
        await using var tx = await _db.Database.BeginTransactionAsync();
        await _db.SaveChangesAsync();
        // 이름을 바꾸면 그 업체의 세정 현황도 새 이름으로 — 현황은 업체를 이름으로 찾아 주간세정/기타세정을
        // 나누므로, 예전에는 주간세정 업체 이름을 바꾸는 순간 진행 중 항목이 기타세정 목록으로 떨어졌다.
        // 버전을 올려 그 항목을 열어 둔 사람이 저장하면 "다른 사람이 먼저 고쳤다" 로 알게 한다.
        // 다른 부서에 같은 이름의 업체가 남아 있으면 현황이 어느 쪽 업체인지 알 수 없으므로 바꾸지 않는다.
        if (oldName.Length > 0 && oldName != v.VendorName
            && !await _db.Vendors.AnyAsync(o => o.Id != v.Id && o.VendorName.Trim() == oldName))
            await _db.Handovers.Where(h => h.Vendor.Trim() == oldName)
                .ExecuteUpdateAsync(u => u.SetProperty(h => h.Vendor, v.VendorName).SetProperty(h => h.RowVersion, h => h.RowVersion + 1));
        await tx.CommitAsync();
        await _dept.NamesAsync();
        return ToDto(v);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var v = await _db.Vendors.FindAsync(id);
        if (v is null) return false;
        await _dept.EnsureAsync(v.DeptId, What);
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
        await _dept.EnsureAsync(v.DeptId, What);
        v.IsFavorite = !v.IsFavorite;
        await _db.SaveChangesAsync();
        return v.IsFavorite;
    }
}
