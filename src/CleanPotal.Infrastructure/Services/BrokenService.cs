using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class BrokenService : IBrokenService
{
    private readonly CleanPotalDbContext _db;
    public BrokenService(CleanPotalDbContext db) => _db = db;

    public async Task<IReadOnlyList<BrokenRecordDto>> GetAllAsync(
        int? year, string? team, string? productType, string? official, string? search)
    {
        var q = _db.BrokenRecords.AsQueryable();
        if (year is not null) q = q.Where(b => b.OccurDate != null && b.OccurDate.Value.Year == year);
        if (!string.IsNullOrEmpty(team) && team != "전체") q = q.Where(b => b.Team == team);
        if (!string.IsNullOrEmpty(productType) && productType != "전체") q = q.Where(b => b.ProductType == productType);
        if (official == "공식") q = q.Where(b => b.IsOfficial);
        else if (official == "비공식") q = q.Where(b => !b.IsOfficial);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(b => b.ProductName.Contains(search) || b.Causer.Contains(search) ||
                             b.SN.Contains(search) || b.Description.Contains(search) || b.Line.Contains(search));

        var items = await q
            .OrderByDescending(b => b.OccurDate)
            .ThenByDescending(b => b.CreatedAt)
            .ToListAsync();

        // 필터 결과 내 순번 (최신이 큰 번호)
        int n = items.Count;
        return items.Select(b => new BrokenRecordDto(
            n--, b.Id, b.OccurDate, b.Line, b.ProductName, b.ProductType, b.SN, b.Team,
            b.Causer, b.JobTitle, b.Career, b.OccurStage, b.Description, b.Status, b.IsOfficial, b.CreatedAt
        )).ToList();
    }

    public async Task<BrokenFilterOptionsDto> GetFilterOptionsAsync()
    {
        var all = await _db.BrokenRecords.ToListAsync();
        var years = all.Where(b => b.OccurDate != null).Select(b => b.OccurDate!.Value.Year).Distinct().OrderByDescending(y => y).ToList();
        var teams = all.Select(b => b.Team).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();
        var types = all.Select(b => b.ProductType).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();
        return new BrokenFilterOptionsDto(years, teams, types);
    }

    private static BrokenRecordDto ToDto(BrokenRecord b) => new(
        0, b.Id, b.OccurDate, b.Line, b.ProductName, b.ProductType, b.SN, b.Team,
        b.Causer, b.JobTitle, b.Career, b.OccurStage, b.Description, b.Status, b.IsOfficial, b.CreatedAt);

    public async Task<BrokenRecordDto> CreateAsync(BrokenUpsertRequest r)
    {
        var b = new BrokenRecord();
        Apply(b, r);
        b.CreatedAt = DateTime.Now;
        _db.BrokenRecords.Add(b);
        await _db.SaveChangesAsync();
        return ToDto(b);
    }

    public async Task<BrokenRecordDto?> UpdateAsync(int id, BrokenUpsertRequest r)
    {
        var b = await _db.BrokenRecords.FindAsync(id);
        if (b is null) return null;
        Apply(b, r);
        await _db.SaveChangesAsync();
        return ToDto(b);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var b = await _db.BrokenRecords.FindAsync(id);
        if (b is null) return false;
        _db.BrokenRecords.Remove(b);
        await _db.SaveChangesAsync();
        return true;
    }

    private static void Apply(BrokenRecord b, BrokenUpsertRequest r)
    {
        b.OccurDate = r.OccurDate;
        b.Line = r.Line;
        b.ProductName = r.ProductName;
        b.ProductType = r.ProductType;
        b.SN = r.SN;
        b.Team = r.Team;
        b.Causer = r.Causer;
        b.JobTitle = r.JobTitle;
        b.Career = r.Career;
        b.OccurStage = r.OccurStage;
        b.Description = r.Description;
        b.Status = string.IsNullOrEmpty(r.Status) ? "접수" : r.Status;
        b.IsOfficial = r.IsOfficial;
    }
}
