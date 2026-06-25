using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class QuotationService : IQuotationService
{
    private readonly CleanPotalDbContext _db;
    public QuotationService(CleanPotalDbContext db) => _db = db;

    private static QuotationItemDto ItemDto(QuotationItem i) =>
        new(i.Id, i.SortOrder, i.ItemName, i.Spec, i.Unit, i.Quantity, i.UnitPrice, i.Quantity * i.UnitPrice, i.Remarks);

    private static QuotationDto ToDto(Quotation q)
    {
        var items = q.Items.OrderBy(i => i.SortOrder).Select(ItemDto).ToList();
        return new(q.Id, q.QuoteNo, q.VendorName, q.QuoteDate, q.ValidUntil, q.Status, q.Remarks,
            q.CreatedBy, q.CreatedAt, q.UpdatedAt, items.Sum(i => i.Amount), items);
    }

    public async Task<IReadOnlyList<QuotationSummaryDto>> GetAllAsync(string? vendor, string? search)
    {
        var q = _db.Quotations.Include(x => x.Items).AsQueryable();
        if (!string.IsNullOrEmpty(vendor) && vendor != "전체") q = q.Where(x => x.VendorName == vendor);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(x => x.QuoteNo.Contains(search) || x.VendorName.Contains(search));
        var list = await q.OrderByDescending(x => x.CreatedAt).ToListAsync();
        return list.Select(x => new QuotationSummaryDto(
            x.Id, x.QuoteNo, x.VendorName, x.QuoteDate, x.ValidUntil, x.Status,
            x.Items.Sum(i => i.Quantity * i.UnitPrice), x.Items.Count, x.CreatedBy)).ToList();
    }

    public async Task<QuotationDto?> GetAsync(int id)
    {
        var q = await _db.Quotations.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        return q is null ? null : ToDto(q);
    }

    public async Task<QuotationDto> CreateAsync(QuotationUpsertRequest req, string actor)
    {
        var q = new Quotation
        {
            QuoteNo = req.QuoteNo, VendorName = req.VendorName, QuoteDate = req.QuoteDate,
            ValidUntil = req.ValidUntil, Status = string.IsNullOrEmpty(req.Status) ? "작성중" : req.Status,
            Remarks = req.Remarks, CreatedBy = actor, CreatedAt = DateTime.Now,
        };
        ApplyItems(q, req);
        _db.Quotations.Add(q);
        await _db.SaveChangesAsync();
        return ToDto(q);
    }

    public async Task<QuotationDto?> UpdateAsync(int id, QuotationUpsertRequest req, string actor)
    {
        var q = await _db.Quotations.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (q is null) return null;
        q.QuoteNo = req.QuoteNo;
        q.VendorName = req.VendorName;
        q.QuoteDate = req.QuoteDate;
        q.ValidUntil = req.ValidUntil;
        q.Status = req.Status;
        q.Remarks = req.Remarks;
        q.UpdatedAt = DateTime.Now;
        _db.QuotationItems.RemoveRange(q.Items);
        q.Items.Clear();
        ApplyItems(q, req);
        await _db.SaveChangesAsync();
        return ToDto(q);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var q = await _db.Quotations.FindAsync(id);
        if (q is null) return false;
        _db.Quotations.Remove(q);   // 품목 Cascade 삭제
        await _db.SaveChangesAsync();
        return true;
    }

    private static void ApplyItems(Quotation q, QuotationUpsertRequest req)
    {
        int order = 1;
        foreach (var it in req.Items)
        {
            q.Items.Add(new QuotationItem
            {
                SortOrder = order++, ItemName = it.ItemName, Spec = it.Spec,
                Unit = string.IsNullOrEmpty(it.Unit) ? "EA" : it.Unit,
                Quantity = it.Quantity, UnitPrice = it.UnitPrice, Remarks = it.Remarks,
            });
        }
    }
}
