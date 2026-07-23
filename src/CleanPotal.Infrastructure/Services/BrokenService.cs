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

    private static BrokenRecordDto ToDto(BrokenRecord b, int no) => new(
        no, b.Id, b.OccurDate, b.Line, b.ProductName, b.ProductType, b.SN, b.Team,
        b.Causer, b.JobTitle, b.Career, b.OccurStage, b.Description, b.Status, b.IsOfficial,
        b.PositionFrozen, b.IncidentReports, b.CountermeasureReports, b.TrainingDocs, b.TrainingImages,
        b.CreatedAt);

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

        int n = items.Count;
        return items.Select(b => ToDto(b, n--)).ToList();
    }

    public async Task<BrokenFilterOptionsDto> GetFilterOptionsAsync()
    {
        var all = await _db.BrokenRecords.ToListAsync();
        var years = all.Where(b => b.OccurDate != null).Select(b => b.OccurDate!.Value.Year).Distinct().OrderByDescending(y => y).ToList();
        var teams = all.Select(b => b.Team).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();
        var types = all.Select(b => b.ProductType).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();
        return new BrokenFilterOptionsDto(years, teams, types);
    }

    public async Task<BrokenRecordDto> CreateAsync(BrokenUpsertRequest r)
    {
        var b = new BrokenRecord();
        Apply(b, r);
        b.CreatedAt = DateTime.Now;
        _db.BrokenRecords.Add(b);
        await _db.SaveChangesAsync();
        return ToDto(b, 0);
    }

    public async Task<BrokenRecordDto?> UpdateAsync(int id, BrokenUpsertRequest r)
    {
        var b = await _db.BrokenRecords.FindAsync(id);
        if (b is null) return null;
        Apply(b, r);
        await _db.SaveChangesAsync();
        return ToDto(b, 0);
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
        b.PositionFrozen = r.PositionFrozen;
        b.IncidentReports = r.IncidentReports ?? "";
        b.CountermeasureReports = r.CountermeasureReports ?? "";
        b.TrainingDocs = r.TrainingDocs ?? "";
        b.TrainingImages = r.TrainingImages ?? "";
    }

    // ── 교육 기록 ──
    private static BrokenTrainingDto ToDto(BrokenTraining t) =>
        new(t.Id, t.TrainingType, t.TrainingDate, t.Content, t.Documents, t.Images);

    public async Task<IReadOnlyList<BrokenTrainingDto>> GetTrainingsAsync(string? type)
    {
        var q = _db.BrokenTrainings.AsQueryable();
        if (!string.IsNullOrEmpty(type) && type != "전체") q = q.Where(t => t.TrainingType == type);
        var list = await q.OrderBy(t => t.SortOrder).ThenByDescending(t => t.TrainingDate).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<BrokenTrainingDto> CreateTrainingAsync(BrokenTrainingUpsertRequest r)
    {
        var t = new BrokenTraining
        {
            TrainingType = string.IsNullOrEmpty(r.TrainingType) ? "production" : r.TrainingType,
            TrainingDate = r.TrainingDate, Content = r.Content,
            Documents = r.Documents ?? "", Images = r.Images ?? "",
        };
        _db.BrokenTrainings.Add(t);
        await _db.SaveChangesAsync();
        return ToDto(t);
    }

    public async Task<BrokenTrainingDto?> UpdateTrainingAsync(int id, BrokenTrainingUpsertRequest r)
    {
        var t = await _db.BrokenTrainings.FindAsync(id);
        if (t is null) return null;
        t.TrainingType = string.IsNullOrEmpty(r.TrainingType) ? "production" : r.TrainingType;
        t.TrainingDate = r.TrainingDate;
        t.Content = r.Content;
        t.Documents = r.Documents ?? "";
        t.Images = r.Images ?? "";
        await _db.SaveChangesAsync();
        return ToDto(t);
    }

    public async Task<bool> DeleteTrainingAsync(int id)
    {
        var t = await _db.BrokenTrainings.FindAsync(id);
        if (t is null) return false;
        _db.BrokenTrainings.Remove(t);
        await _db.SaveChangesAsync();
        return true;
    }

    // ── 교육 목표 ──
    public async Task<IReadOnlyList<BrokenGoalDto>> GetGoalsAsync()
    {
        var list = await _db.BrokenGoals.OrderBy(g => g.Category).ThenBy(g => g.Year).ToListAsync();
        return list.Select(g => new BrokenGoalDto(g.Id, g.Category, g.Year, g.Target)).ToList();
    }

    public async Task<IReadOnlyList<BrokenGoalDto>> SaveGoalsAsync(IReadOnlyList<BrokenGoalInput> goals)
    {
        _db.BrokenGoals.RemoveRange(_db.BrokenGoals);
        await _db.SaveChangesAsync();
        foreach (var g in goals ?? new List<BrokenGoalInput>())
            _db.BrokenGoals.Add(new BrokenGoal { Category = g.Category, Year = g.Year, Target = g.Target ?? "" });
        await _db.SaveChangesAsync();
        return await GetGoalsAsync();
    }

    // ── 메모 ──
    public async Task<BrokenMemoDto> GetMemoAsync()
    {
        var m = await _db.BrokenMetas.FirstOrDefaultAsync();
        return new BrokenMemoDto(m?.Memo ?? "");
    }

    public async Task<BrokenMemoDto> SaveMemoAsync(BrokenMemoDto req)
    {
        var m = await _db.BrokenMetas.FirstOrDefaultAsync();
        if (m is null) { m = new BrokenMeta(); _db.BrokenMetas.Add(m); }
        m.Memo = req.Memo ?? "";
        await _db.SaveChangesAsync();
        return new BrokenMemoDto(m.Memo);
    }

    /// <summary>재직 중 사용자 이름→직위/입사일 (유발자 자동 완성·경력 계산용).</summary>
    public async Task<IReadOnlyList<BrokenUserDto>> GetUserDirectoryAsync()
        => await _db.Users
            .Where(u => !u.IsResigned && u.RealName != "")
            .OrderBy(u => u.RealName)
            .Select(u => new BrokenUserDto(u.RealName, u.JobTitle, u.HireDate))
            .ToListAsync();
}
