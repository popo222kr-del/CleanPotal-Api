using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class QuotationMasterService : IQuotationMasterService
{
    private readonly CleanPotalDbContext _db;
    public QuotationMasterService(CleanPotalDbContext db) => _db = db;

    // ── 품목 단가표 ──
    private static ProductMasterDto ToDto(ProductMaster p) =>
        new(p.Id, p.ProductName, p.PartCode, p.Spec, p.UnitPrice, p.VendorName, p.Unit, p.UpdatedBy, p.UpdatedAt);

    public async Task<IReadOnlyList<ProductMasterDto>> GetProductsAsync(string? search)
    {
        var q = _db.ProductMasters.AsQueryable();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(p => p.ProductName.Contains(search) || p.PartCode.Contains(search) || p.VendorName.Contains(search));
        var list = await q.OrderBy(p => p.ProductName).ThenBy(p => p.PartCode).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<ProductMasterDto> CreateProductAsync(ProductMasterUpsertRequest r, string actor)
    {
        var p = new ProductMaster();
        ApplyProduct(p, r, actor);
        _db.ProductMasters.Add(p);
        await _db.SaveChangesAsync();
        return ToDto(p);
    }

    public async Task<ProductMasterDto?> UpdateProductAsync(int id, ProductMasterUpsertRequest r, string actor)
    {
        var p = await _db.ProductMasters.FindAsync(id);
        if (p is null) return null;
        ApplyProduct(p, r, actor);
        await _db.SaveChangesAsync();
        return ToDto(p);
    }

    public async Task<bool> DeleteProductAsync(int id)
    {
        var p = await _db.ProductMasters.FindAsync(id);
        if (p is null) return false;
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
        return new QuotationConfigDto(c?.BusinessNo ?? "");
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
        await _db.SaveChangesAsync();
        return new QuotationConfigDto(c.BusinessNo);
    }
}
