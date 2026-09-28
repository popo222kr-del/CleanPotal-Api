using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// 견적 기준 자료. 품목 단가표는 부서마다 따로 둔다(DeptScope) — 다른 부서 단가는 보지도 고치지도 못한다.
/// 전역 품목 템플릿·견적 설정(회사 정보)은 공용.
/// </summary>
public class QuotationMasterService : IQuotationMasterService
{
    private const string What = "단가표 품목";
    private readonly CleanPotalDbContext _db;
    private readonly DeptScope _dept;

    /// <summary>로그인 사용자 없이(테스트·가져오기) — 부서 범위를 두지 않는다.</summary>
    public QuotationMasterService(CleanPotalDbContext db) : this(db, null) { }

    public QuotationMasterService(CleanPotalDbContext db, ICurrentUser? me)
    {
        _db = db;
        _dept = new DeptScope(db, me);
    }

    // ── 품목 단가표 ──
    private ProductMasterDto ToDto(ProductMaster p) =>
        new(p.Id, p.ProductName, p.PartCode, p.Spec, p.UnitPrice, p.VendorName, p.Unit, p.UpdatedBy, p.UpdatedAt,
            p.DeptId, _dept.NameOf(p.DeptId));

    public async Task<IReadOnlyList<ProductMasterDto>> GetProductsAsync(string? search)
    {
        var q = await _dept.FilterAsync(_db.ProductMasters.AsQueryable(), p => p.DeptId);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(p => p.ProductName.Contains(search) || p.PartCode.Contains(search) || p.VendorName.Contains(search));
        var list = await q.OrderBy(p => p.ProductName).ThenBy(p => p.PartCode).ToListAsync();
        await _dept.NamesAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<ProductMasterDto> CreateProductAsync(ProductMasterUpsertRequest r, string actor)
    {
        var p = new ProductMaster { DeptId = await _dept.ForCreateAsync(r.DeptId) };
        ApplyProduct(p, r, actor);
        _db.ProductMasters.Add(p);
        await _db.SaveChangesAsync();
        await _dept.NamesAsync();
        return ToDto(p);
    }

    public async Task<ProductMasterDto?> UpdateProductAsync(int id, ProductMasterUpsertRequest r, string actor)
    {
        var p = await _db.ProductMasters.FindAsync(id);
        if (p is null) return null;
        await _dept.EnsureAsync(p.DeptId, What);
        if (_dept.Unrestricted && r.DeptId is not null) p.DeptId = await _dept.ForCreateAsync(r.DeptId);
        ApplyProduct(p, r, actor);
        await _db.SaveChangesAsync();
        await _dept.NamesAsync();
        return ToDto(p);
    }

    public async Task<bool> DeleteProductAsync(int id)
    {
        var p = await _db.ProductMasters.FindAsync(id);
        if (p is null) return false;
        await _dept.EnsureAsync(p.DeptId, What);
        _db.ProductMasters.Remove(p);
        await _db.SaveChangesAsync();
        return true;
    }

    private static void ApplyProduct(ProductMaster p, ProductMasterUpsertRequest r, string actor)
    {
        p.ProductName = r.ProductName;
        p.PartCode = r.PartCode;
        p.Spec = r.Spec;
        p.UnitPrice = r.UnitPrice;
        p.VendorName = r.VendorName;
        p.Unit = r.Unit;
        p.UpdatedBy = actor;
        p.UpdatedAt = DateTime.Now;
    }

    // ── 전역 품목 템플릿 ──
    private static GlobalTemplateDto ToDto(GlobalTemplate t) =>
        new(t.Id, t.ProductCode, t.ProductName, t.TemplatePath);

    public async Task<IReadOnlyList<GlobalTemplateDto>> GetTemplatesAsync()
    {
        var list = await _db.GlobalTemplates.OrderBy(t => t.ProductCode).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<GlobalTemplateDto> CreateTemplateAsync(GlobalTemplateUpsertRequest r)
    {
        var t = new GlobalTemplate { ProductCode = r.ProductCode, ProductName = r.ProductName, TemplatePath = r.TemplatePath };
        _db.GlobalTemplates.Add(t);
        await _db.SaveChangesAsync();
        return ToDto(t);
    }

    public async Task<GlobalTemplateDto?> UpdateTemplateAsync(int id, GlobalTemplateUpsertRequest r)
    {
        var t = await _db.GlobalTemplates.FindAsync(id);
        if (t is null) return null;
        t.ProductCode = r.ProductCode;
        t.ProductName = r.ProductName;
        t.TemplatePath = r.TemplatePath;
        await _db.SaveChangesAsync();
        return ToDto(t);
    }

    public async Task<bool> DeleteTemplateAsync(int id)
    {
        var t = await _db.GlobalTemplates.FindAsync(id);
        if (t is null) return false;
        _db.GlobalTemplates.Remove(t);
        await _db.SaveChangesAsync();
        return true;
    }

    // ── 견적 설정 (단일 행) ──
    public async Task<QuotationConfigDto> GetConfigAsync()
    {
        var c = await _db.QuotationConfigs.FirstOrDefaultAsync();
        return new QuotationConfigDto(c?.BusinessNo ?? "", c?.Address ?? "", c?.Tel ?? "", c?.Fax ?? "", c?.Signer ?? "", c?.CompanyName ?? "");
    }

    public async Task<QuotationConfigDto> SaveConfigAsync(QuotationConfigDto req)
    {
        var c = await _db.QuotationConfigs.FirstOrDefaultAsync();
        if (c is null)
        {
            c = new QuotationConfig();
            _db.QuotationConfigs.Add(c);
        }
        c.BusinessNo = req.BusinessNo ?? "";
        c.Address = req.Address ?? "";
        c.Tel = req.Tel ?? "";
        c.Fax = req.Fax ?? "";
        c.Signer = req.Signer ?? "";
        c.CompanyName = req.CompanyName ?? "";
        await _db.SaveChangesAsync();
        return new QuotationConfigDto(c.BusinessNo, c.Address, c.Tel, c.Fax, c.Signer, c.CompanyName);
    }
}
