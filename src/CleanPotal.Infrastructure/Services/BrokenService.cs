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

    // ── 등록 칸 드롭다운 목록 ────────────────────────────────────────────────
    // 제품군·발생단계는 다른 곳에 마스터가 없어 BrokenOptions 에 둔다. 비어 있으면
    // 지금까지 기록에 적힌 값으로 한 번 채워 넣는다 — 그래야 관리 화면에서 고칠 수 있다.
    // 팀은 조직 관리, 라인은 MES 라인(+기록에 남은 값)에서 읽어 오므로 편집 대상이 아니다.
    public async Task<BrokenOptionsDto> GetOptionsAsync(IReadOnlyList<string>? mesLines = null)
    {
        await SeedOptionsFromRecordsAsync();
        return await BuildOptionsAsync(mesLines);
    }

    public async Task<BrokenOptionsDto> SaveOptionsAsync(BrokenOptionsSaveRequest req, IReadOnlyList<string>? mesLines = null)
    {
        var productTypes = Clean(req.ProductTypes);
        var occurStages = Clean(req.OccurStages);

        _db.BrokenOptions.RemoveRange(_db.BrokenOptions);
        int ord = 0;
        foreach (var v in productTypes) _db.BrokenOptions.Add(new BrokenOption { Kind = "productType", Name = v, OrderIndex = ord++ });
        foreach (var v in occurStages) _db.BrokenOptions.Add(new BrokenOption { Kind = "occurStage", Name = v, OrderIndex = ord++ });
        await _db.SaveChangesAsync();

        return await BuildOptionsAsync(mesLines);
    }

    private static List<string> Clean(IReadOnlyList<string>? values) =>
        (values ?? Array.Empty<string>())
            .Select(v => (v ?? "").Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private async Task SeedOptionsFromRecordsAsync()
    {
        if (await _db.BrokenOptions.AnyAsync()) return;

        var rows = await _db.BrokenRecords
            .Select(b => new { b.ProductType, b.OccurStage })
            .ToListAsync();
        var types = Clean(rows.Select(r => r.ProductType).ToList());
        var stages = Clean(rows.Select(r => r.OccurStage).ToList());
        if (types.Count == 0 && stages.Count == 0) return;

        types.Sort(StringComparer.CurrentCulture);
        stages.Sort(StringComparer.CurrentCulture);
        int ord = 0;
        foreach (var v in types) _db.BrokenOptions.Add(new BrokenOption { Kind = "productType", Name = v, OrderIndex = ord++ });
        foreach (var v in stages) _db.BrokenOptions.Add(new BrokenOption { Kind = "occurStage", Name = v, OrderIndex = ord++ });
        await _db.SaveChangesAsync();
    }

    private async Task<BrokenOptionsDto> BuildOptionsAsync(IReadOnlyList<string>? mesLines)
    {
        var opts = await _db.BrokenOptions.OrderBy(o => o.OrderIndex).ThenBy(o => o.Id).ToListAsync();

        var teams = await _db.OrgUnits
            .Where(o => o.Kind == "team" && o.Name != "")
            .OrderBy(o => o.OrderIndex).ThenBy(o => o.Id)
            .Select(o => o.Name)
            .ToListAsync();
        // 조직에 없는 옛 팀명도 기록에 남아 있으면 고를 수 있어야 한다 (예: 이름 바꾸기 전 기록 수정)
        var recordTeams = await _db.BrokenRecords.Select(b => b.Team).Distinct().ToListAsync();

        var recordLines = await _db.BrokenRecords.Select(b => b.Line).Distinct().ToListAsync();

        return new BrokenOptionsDto(
            opts.Where(o => o.Kind == "productType").Select(o => o.Name).ToList(),
            opts.Where(o => o.Kind == "occurStage").Select(o => o.Name).ToList(),
            Merge(teams, recordTeams),
            Merge(mesLines ?? Array.Empty<string>(), recordLines));
    }

    // 마스터 목록을 앞에 두고, 거기 없는 기록 속 값을 뒤에 덧붙인다.
    private static List<string> Merge(IReadOnlyList<string> master, IReadOnlyList<string> extra)
    {
        var result = Clean(master);
        var seen = new HashSet<string>(result, StringComparer.OrdinalIgnoreCase);
        var rest = Clean(extra).Where(v => !seen.Contains(v)).ToList();
        rest.Sort(StringComparer.CurrentCulture);
        result.AddRange(rest);
        return result;
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
