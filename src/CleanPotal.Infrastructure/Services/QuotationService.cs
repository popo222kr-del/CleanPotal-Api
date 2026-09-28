using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

/// <summary>업체 견적서. 부서마다 따로 관리한다 — 다른 부서 견적서는 보지도 고치지도 못한다(DeptScope). 견적 설정은 공용.</summary>
public class QuotationService : IQuotationService
{
    private const string What = "견적서";
    private readonly CleanPotalDbContext _db;
    private readonly DeptScope _dept;

    /// <summary>로그인 사용자 없이(테스트·가져오기) — 부서 범위를 두지 않는다.</summary>
    public QuotationService(CleanPotalDbContext db) : this(db, null) { }

    public QuotationService(CleanPotalDbContext db, ICurrentUser? me)
    {
        _db = db;
        _dept = new DeptScope(db, me);
    }

    private static QuotationItemDto ItemDto(QuotationItem i) =>
        new(i.Id, i.No, i.Description, i.PartCode, i.StandardSpec, i.ListPrice, i.Qty, i.ListPrice * i.Qty);

    private QuotationDto ToDto(Quotation q)
    {
        var items = q.Items.OrderBy(i => i.No).Select(ItemDto).ToList();
        return new(q.Id, q.QuoteNo, q.RfqNo, q.Company, q.Attention, q.Email, q.Phone,
            q.QuoteDate, q.Validity, q.AetsManager, q.AetsPhone, q.AetsEmail, q.BusinessNo,
            q.Remarks, q.Memo, q.SourceFileName, q.CreatedBy, q.CreatedAt, q.LastModifiedBy, q.LastModifiedAt,
            items.Sum(i => i.Amount), items, q.DeptId, _dept.NameOf(q.DeptId));
    }

    public async Task<IReadOnlyList<QuotationSummaryDto>> GetAllAsync(string? vendor, string? search)
    {
        var q = await _dept.FilterAsync(_db.Quotations.Include(x => x.Items).AsQueryable(), x => x.DeptId);
        if (!string.IsNullOrEmpty(vendor) && vendor != "전체") q = q.Where(x => x.Company == vendor);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(x => x.QuoteNo.Contains(search) || x.Company.Contains(search) || x.RfqNo.Contains(search));
        var list = await q.OrderByDescending(x => x.CreatedAt).ToListAsync();
        await _dept.NamesAsync();
        return list.Select(x => new QuotationSummaryDto(
            x.Id, x.QuoteNo, x.RfqNo, x.Company, x.QuoteDate, x.Validity,
            x.Items.Sum(i => i.ListPrice * i.Qty), x.Items.Count, x.AetsManager, x.CreatedAt,
            x.DeptId, _dept.NameOf(x.DeptId))).ToList();
    }

    public async Task<QuotationDto?> GetAsync(int id)
    {
        var q = await _db.Quotations.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (q is null) return null;
        await _dept.EnsureAsync(q.DeptId, What);
        await _dept.NamesAsync();
        return ToDto(q);
    }

    public async Task<QuotationDto> CreateAsync(QuotationUpsertRequest req, string actor)
    {
        var q = new Quotation { CreatedBy = actor, CreatedAt = DateTime.Now, DeptId = await _dept.ForCreateAsync(req.DeptId) };
        ApplyHead(q, req);
        ApplyItems(q, req);
        _db.Quotations.Add(q);
        await _db.SaveChangesAsync();
        await _dept.NamesAsync();
        return ToDto(q);
    }

    public async Task<QuotationDto?> UpdateAsync(int id, QuotationUpsertRequest req, string actor)
    {
        var q = await _db.Quotations.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (q is null) return null;
        await _dept.EnsureAsync(q.DeptId, What);
        // 등록 부서는 관리자만 옮긴다.
        if (_dept.Unrestricted && req.DeptId is not null) q.DeptId = await _dept.ForCreateAsync(req.DeptId);
        ApplyHead(q, req);
        q.LastModifiedBy = actor;
        q.LastModifiedAt = DateTime.Now;
        _db.QuotationItems.RemoveRange(q.Items);
        q.Items.Clear();
        ApplyItems(q, req);
        await _db.SaveChangesAsync();
        await _dept.NamesAsync();
        return ToDto(q);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var q = await _db.Quotations.FindAsync(id);
        if (q is null) return false;
        await _dept.EnsureAsync(q.DeptId, What);
        _db.Quotations.Remove(q);   // 품목 Cascade 삭제
        await _db.SaveChangesAsync();
        return true;
    }

    private static void ApplyHead(Quotation q, QuotationUpsertRequest r)
    {
        q.QuoteNo = r.QuoteNo;
        q.RfqNo = r.RfqNo;
        q.Company = r.Company;
        q.Attention = r.Attention;
        q.Email = r.Email;
        q.Phone = r.Phone;
        q.QuoteDate = r.QuoteDate;
        q.Validity = r.Validity;
        q.AetsManager = r.AetsManager;
        q.AetsPhone = r.AetsPhone;
        q.AetsEmail = r.AetsEmail;
        q.BusinessNo = r.BusinessNo;
        q.Remarks = r.Remarks;
        q.Memo = r.Memo;
    }

    private static void ApplyItems(Quotation q, QuotationUpsertRequest req)
    {
        int no = 1;
        foreach (var it in req.Items)
        {
            q.Items.Add(new QuotationItem
            {
                No = it.No > 0 ? it.No : no,
                Description = it.Description,
                PartCode = it.PartCode,
                StandardSpec = it.StandardSpec,
                ListPrice = it.ListPrice,
                Qty = it.Qty,
            });
            no++;
        }
    }
}
