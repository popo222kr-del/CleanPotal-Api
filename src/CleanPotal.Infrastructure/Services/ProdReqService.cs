using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class ProdReqService : IProdReqService
{
    private readonly CleanPotalDbContext _db;
    public ProdReqService(CleanPotalDbContext db) => _db = db;

    private static ProdReqDto ToDto(ProdReq p) => new(
        p.Id, p.RequestDate, p.DueDate, p.Status, p.Category, p.Location,
        p.RequestDetail, p.Requester, p.ActionDate, p.ActionDetail, p.Assignee, p.CreatedAt);

    public async Task<IReadOnlyList<ProdReqDto>> GetAllAsync(string? status, string? search)
    {
        var q = _db.ProdReqs.AsQueryable();
        if (!string.IsNullOrEmpty(status) && status != "전체") q = q.Where(p => p.Status == status);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(p => p.RequestDetail.Contains(search) || p.Location.Contains(search) ||
                             p.Requester.Contains(search) || p.Category.Contains(search));
        var items = await q.OrderByDescending(p => p.CreatedAt).ToListAsync();
        return items.Select(ToDto).ToList();
    }

    public async Task<ProdReqDto> CreateAsync(ProdReqUpsertRequest req, string requester)
    {
        var p = new ProdReq
        {
            RequestDate = req.RequestDate ?? DateOnly.FromDateTime(DateTime.Today),
            DueDate = req.DueDate,
            Status = "진행",
            Category = req.Category,
            Location = req.Location,
            RequestDetail = req.RequestDetail,
            Requester = requester,
            ActionDate = req.ActionDate,
            ActionDetail = req.ActionDetail,
            Assignee = req.Assignee,
            CreatedAt = DateTime.Now,
        };
        _db.ProdReqs.Add(p);
        await _db.SaveChangesAsync();
        return ToDto(p);
    }

    public async Task<ProdReqDto?> UpdateAsync(int id, ProdReqUpsertRequest req)
    {
        var p = await _db.ProdReqs.FindAsync(id);
        if (p is null) return null;
        p.RequestDate = req.RequestDate;
        p.DueDate = req.DueDate;
        p.Category = req.Category;
        p.Location = req.Location;
        p.RequestDetail = req.RequestDetail;
        p.ActionDate = req.ActionDate;
        p.ActionDetail = req.ActionDetail;
        p.Assignee = req.Assignee;
        await _db.SaveChangesAsync();
        return ToDto(p);
    }

    public async Task<ProdReqDto?> ChangeStatusAsync(int id, string status)
    {
        var p = await _db.ProdReqs.FindAsync(id);
        if (p is null) return null;
        p.Status = status;
        if (status == "완료" && p.ActionDate is null) p.ActionDate = DateOnly.FromDateTime(DateTime.Today);
        await _db.SaveChangesAsync();
        return ToDto(p);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var p = await _db.ProdReqs.FindAsync(id);
        if (p is null) return false;
        _db.ProdReqs.Remove(p);
        await _db.SaveChangesAsync();
        return true;
    }
}
