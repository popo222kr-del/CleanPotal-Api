using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class IcpmsService : IIcpmsService
{
    private readonly CleanPotalDbContext _db;
    public IcpmsService(CleanPotalDbContext db) => _db = db;

    private static readonly string[] E = IcpElements.Order;

    private static double Get(EquipmentAnalysis a, string el) => el switch
    {
        "Li" => a.Li, "Na" => a.Na, "Mg" => a.Mg, "Al" => a.Al, "K" => a.K, "Ca" => a.Ca,
        "Ti" => a.Ti, "Cr" => a.Cr, "Mn" => a.Mn, "Fe" => a.Fe, "Co" => a.Co, "Ni" => a.Ni,
        "Cu" => a.Cu, "Zn" => a.Zn, "Ge" => a.Ge, "As" => a.As, "Cd" => a.Cd, "In" => a.In,
        "Ba" => a.Ba, "Ta" => a.Ta, "W" => a.W, "Pb" => a.Pb, _ => 0,
    };
    private static void Set(EquipmentAnalysis a, string el, double v)
    {
        switch (el)
        {
            case "Li": a.Li = v; break; case "Na": a.Na = v; break; case "Mg": a.Mg = v; break;
            case "Al": a.Al = v; break; case "K": a.K = v; break; case "Ca": a.Ca = v; break;
            case "Ti": a.Ti = v; break; case "Cr": a.Cr = v; break; case "Mn": a.Mn = v; break;
            case "Fe": a.Fe = v; break; case "Co": a.Co = v; break; case "Ni": a.Ni = v; break;
            case "Cu": a.Cu = v; break; case "Zn": a.Zn = v; break; case "Ge": a.Ge = v; break;
            case "As": a.As = v; break; case "Cd": a.Cd = v; break; case "In": a.In = v; break;
            case "Ba": a.Ba = v; break; case "Ta": a.Ta = v; break; case "W": a.W = v; break;
            case "Pb": a.Pb = v; break;
        }
    }
    private static Dictionary<string, double> Values(EquipmentAnalysis a) => E.ToDictionary(el => el, el => Get(a, el));
    private static MeasurementDto ToDto(EquipmentAnalysis a) =>
        new(a.Id, a.ProcessType, a.EqId, a.BathGb, a.Category, a.Unit, a.AnalysisDate, Values(a));

    private static string Key(EquipmentAnalysis a) => $"{a.ProcessType}{a.EqId}{a.BathGb}{a.Category}{a.AnalysisDate}";
    private static string Key(MeasurementUploadRow r) => $"{r.ProcessType}{r.EqId}{r.BathGb}{r.Category}{r.AnalysisDate}";

    private async Task LogAsync(string type, string detail, string user)
    {
        _db.EquipmentActionLogs.Add(new EquipmentActionLog { ActionType = type, Detail = detail, UserName = user, CreatedAt = DateTime.Now });
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<EquipmentDto>> GetEquipmentAsync()
    {
        var dataIds = await _db.EquipmentAnalyses.Select(a => a.EqId).Distinct().ToListAsync();
        var masters = await _db.EquipmentMasters.ToListAsync();
        var procById = masters.ToDictionary(m => m.EqId, m => m.Process);
        var all = dataIds.Union(masters.Select(m => m.EqId)).OrderBy(x => x).ToList();
        return all.Select(id => new EquipmentDto(id, procById.GetValueOrDefault(id, ""), dataIds.Contains(id))).ToList();
    }

    public async Task<IcpmsFiltersDto> GetFiltersAsync(IReadOnlyList<string>? processTypes)
    {
        var q = _db.EquipmentAnalyses.AsQueryable();
        if (processTypes is { Count: > 0 }) q = q.Where(a => processTypes.Contains(a.ProcessType));
        var rows = await q.Select(a => new { a.ProcessType, a.BathGb, a.EqId, a.AnalysisDate }).ToListAsync();
        var pts = await _db.EquipmentAnalyses.Select(a => a.ProcessType).Distinct().OrderBy(x => x).ToListAsync();
        return new IcpmsFiltersDto(
            pts.Where(x => !string.IsNullOrEmpty(x)).ToList(),
            rows.Select(r => r.BathGb).Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToList(),
            rows.Select(r => r.EqId).Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToList(),
            rows.Select(r => r.AnalysisDate).Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderByDescending(x => x).ToList());
    }

    private IQueryable<EquipmentAnalysis> Filtered(IReadOnlyList<string>? pt, IReadOnlyList<string>? baths, IReadOnlyList<string>? eqIds, IReadOnlyList<string>? dates)
    {
        var q = _db.EquipmentAnalyses.AsQueryable();
        if (pt is { Count: > 0 }) q = q.Where(a => pt.Contains(a.ProcessType));
        if (baths is { Count: > 0 }) q = q.Where(a => baths.Contains(a.BathGb));
        if (eqIds is { Count: > 0 }) q = q.Where(a => eqIds.Contains(a.EqId));
        if (dates is { Count: > 0 }) q = q.Where(a => dates.Contains(a.AnalysisDate));
        return q;
    }

    public async Task<IReadOnlyList<MeasurementDto>> GetMeasurementsAsync(IReadOnlyList<string>? pt, IReadOnlyList<string>? baths, IReadOnlyList<string>? eqIds, IReadOnlyList<string>? dates)
    {
        var rows = await Filtered(pt, baths, eqIds, dates)
            .OrderByDescending(a => a.AnalysisDate).ThenBy(a => a.EqId).ThenBy(a => a.BathGb).ToListAsync();
        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<(string EqId, string Process, IReadOnlyDictionary<string, double> Values)>> GetComparisonAsync(
        IReadOnlyList<string>? pt, IReadOnlyList<string>? baths, IReadOnlyList<string>? eqIds, IReadOnlyList<string>? dates)
    {
        var rows = await Filtered(pt, baths, eqIds, dates).ToListAsync();
        var procById = await _db.EquipmentMasters.ToDictionaryAsync(m => m.EqId, m => m.Process);
        var result = new List<(string, string, IReadOnlyDictionary<string, double>)>();
        foreach (var g in rows.GroupBy(a => a.EqId).OrderBy(g => g.Key))
        {
            var latest = g.Max(a => a.AnalysisDate);                 // 필터 내 최신일
            var sameDay = g.Where(a => a.AnalysisDate == latest).ToList();
            var vals = E.ToDictionary(el => el, el => sameDay.Average(a => Get(a, el)));   // 동일일 다행이면 평균
            result.Add((g.Key, procById.GetValueOrDefault(g.Key, ""), vals));
        }
        return result;
    }

    public async Task<IcpmsSummaryDto> GetSummaryAsync(IReadOnlyList<string>? dates, IReadOnlyList<string>? elements)
    {
        var els = elements is { Count: > 0 } ? elements.Where(E.Contains).ToArray() : E;
        // 날짜: 지정 없으면 최신 측정일
        var effDates = dates is { Count: > 0 }
            ? dates.ToList()
            : (await _db.EquipmentAnalyses.OrderByDescending(a => a.AnalysisDate).Select(a => a.AnalysisDate).Take(1).ToListAsync());
        var rows = effDates.Count == 0 ? new List<EquipmentAnalysis>()
            : await _db.EquipmentAnalyses.Where(a => effDates.Contains(a.AnalysisDate)).ToListAsync();

        var equip = await GetEquipmentAsync();
        int totalEquip = equip.Count;
        int measured = rows.Select(a => a.EqId).Distinct().Count();

        double max = 0; string maxEq = "", maxEl = "", maxDate = "";
        double sum = 0; int n = 0;
        foreach (var a in rows)
            foreach (var el in els)
            {
                var v = Get(a, el);
                sum += v; n++;
                if (v > max) { max = v; maxEq = a.EqId; maxEl = el; maxDate = a.AnalysisDate; }
            }
        var latestOverall = await _db.EquipmentAnalyses.OrderByDescending(a => a.AnalysisDate).Select(a => a.AnalysisDate).FirstOrDefaultAsync() ?? "";
        var dateCount = await _db.EquipmentAnalyses.Select(a => a.AnalysisDate).Distinct().CountAsync();
        return new IcpmsSummaryDto(totalEquip, measured, totalEquip - measured,
            latestOverall, dateCount, max, maxEq, maxEl, maxDate, n > 0 ? sum / n : 0, "ppb");
    }

    public async Task<MeasurementBulkResult> BulkInsertAsync(IReadOnlyList<MeasurementUploadRow> rows, string user)
    {
        var existing = new HashSet<string>(
            (await _db.EquipmentAnalyses.Select(a => new { a.ProcessType, a.EqId, a.BathGb, a.Category, a.AnalysisDate }).ToListAsync())
            .Select(a => $"{a.ProcessType}{a.EqId}{a.BathGb}{a.Category}{a.AnalysisDate}"));
        int inserted = 0;
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.EqId)) continue;
            var k = Key(r);
            if (!existing.Add(k)) continue;   // 중복(기존 or 배치 내) 무시
            var a = new EquipmentAnalysis
            {
                ProcessType = r.ProcessType ?? "", EqId = r.EqId.Trim(), BathGb = r.BathGb ?? "",
                Category = r.Category ?? "", Unit = string.IsNullOrWhiteSpace(r.Unit) ? "ppb" : r.Unit, AnalysisDate = r.AnalysisDate ?? "",
            };
            if (r.Values != null) foreach (var kv in r.Values) Set(a, kv.Key, kv.Value);
            _db.EquipmentAnalyses.Add(a);
            inserted++;
        }
        await _db.SaveChangesAsync();
        await LogAsync("엑셀 업로드", $"{rows.Count}행 중 {inserted}행 추가(중복 {rows.Count - inserted} 제외)", user);
        return new MeasurementBulkResult(rows.Count, inserted, rows.Count - inserted);
    }

    public async Task<EquipmentDto?> UpdateEquipmentAsync(string eqId, string? newEqId, string? process, string user)
    {
        var master = await _db.EquipmentMasters.FirstOrDefaultAsync(m => m.EqId == eqId);
        var hasData = await _db.EquipmentAnalyses.AnyAsync(a => a.EqId == eqId);
        if (master is null && !hasData) return null;

        var target = newEqId?.Trim();
        // rename remap (3테이블 일괄 UPDATE) — 트랜잭션
        if (!string.IsNullOrWhiteSpace(target) && target != eqId)
        {
            if (await _db.EquipmentMasters.AnyAsync(m => m.EqId == target) || await _db.EquipmentAnalyses.AnyAsync(a => a.EqId == target))
                throw new InvalidOperationException($"이미 존재하는 설비 ID입니다: {target}");
            await using var tx = await _db.Database.BeginTransactionAsync();
            await _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE EquipmentAnalyses SET EqId={target} WHERE EqId={eqId}");
            await _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE EquipmentCheckNotes SET EqId={target} WHERE EqId={eqId}");
            await _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE EquipmentMasters SET EqId={target} WHERE EqId={eqId}");
            await tx.CommitAsync();
            _db.ChangeTracker.Clear();
            await LogAsync("설비명 변경", $"{eqId} → {target}", user);
            master = await _db.EquipmentMasters.FirstOrDefaultAsync(m => m.EqId == target);
            eqId = target;
        }

        if (process is not null)
        {
            if (master is null) { master = new EquipmentMaster { EqId = eqId, Process = process.Trim() }; _db.EquipmentMasters.Add(master); }
            else if (master.Process != process.Trim()) { await LogAsync("공정 변경", $"{eqId}: {master.Process} → {process.Trim()}", user); master.Process = process.Trim(); }
            else master.Process = process.Trim();
            await _db.SaveChangesAsync();
        }
        var dataNow = await _db.EquipmentAnalyses.AnyAsync(a => a.EqId == eqId);
        return new EquipmentDto(eqId, master?.Process ?? "", dataNow);
    }

    public async Task<EquipmentDto> AddEquipmentAsync(string eqId, string user)
    {
        eqId = eqId.Trim();
        if (string.IsNullOrWhiteSpace(eqId)) throw new InvalidOperationException("설비 ID를 입력하세요.");
        if (await _db.EquipmentMasters.AnyAsync(m => m.EqId == eqId)) throw new InvalidOperationException("이미 등록된 설비입니다.");
        _db.EquipmentMasters.Add(new EquipmentMaster { EqId = eqId, Process = "" });
        await _db.SaveChangesAsync();
        await LogAsync("설비 추가", eqId, user);
        var hasData = await _db.EquipmentAnalyses.AnyAsync(a => a.EqId == eqId);
        return new EquipmentDto(eqId, "", hasData);
    }

    public async Task<(bool ok, string? error)> DeleteEquipmentAsync(string eqId, string user)
    {
        if (await _db.EquipmentAnalyses.AnyAsync(a => a.EqId == eqId))
            return (false, "측정 데이터가 있는 설비는 삭제할 수 없습니다. (전체 삭제 또는 이름 변경을 사용하세요)");
        var master = await _db.EquipmentMasters.FirstOrDefaultAsync(m => m.EqId == eqId);
        if (master is null) return (false, "설비를 찾을 수 없습니다.");
        _db.EquipmentMasters.Remove(master);
        var notes = await _db.EquipmentCheckNotes.Where(n => n.EqId == eqId).ToListAsync();
        _db.EquipmentCheckNotes.RemoveRange(notes);
        await _db.SaveChangesAsync();
        await LogAsync("설비 삭제", eqId, user);
        return (true, null);
    }

    public async Task<IReadOnlyList<CheckNoteItemDto>> GetCheckNotesAsync(string date)
    {
        var equip = await GetEquipmentAsync();
        var dayRows = await _db.EquipmentAnalyses.Where(a => a.AnalysisDate == date).ToListAsync();
        var notes = await _db.EquipmentCheckNotes.Where(n => n.CheckDate == date).ToDictionaryAsync(n => n.EqId, n => n.Note);
        var byEq = dayRows.GroupBy(a => a.EqId).ToDictionary(g => g.Key, g => g.ToList());
        return equip.Select(e =>
        {
            var measured = byEq.TryGetValue(e.EqId, out var list) && list.Count > 0;
            string topEl = ""; double topV = 0;
            if (measured)
                foreach (var a in list!)
                    foreach (var el in E) { var v = Get(a, el); if (v > topV) { topV = v; topEl = el; } }
            return new CheckNoteItemDto(e.EqId, e.Process, measured, topEl, topV, notes.GetValueOrDefault(e.EqId, ""));
        }).ToList();
    }

    public async Task SaveCheckNoteAsync(string eqId, string date, string note, string user)
    {
        var n = await _db.EquipmentCheckNotes.FirstOrDefaultAsync(x => x.EqId == eqId && x.CheckDate == date);
        if (n is null) _db.EquipmentCheckNotes.Add(new EquipmentCheckNote { EqId = eqId, CheckDate = date, Note = note, UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
        else { n.Note = note; n.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); }
        await _db.SaveChangesAsync();
        await LogAsync("특이사항 수정", $"{eqId} ({date})", user);
    }

    public async Task<IReadOnlyList<CheckNoteHistoryDto>> GetCheckNoteHistoryAsync(string eqId)
        => await _db.EquipmentCheckNotes.Where(n => n.EqId == eqId && n.Note != "")
            .OrderByDescending(n => n.CheckDate)
            .Select(n => new CheckNoteHistoryDto(n.CheckDate, n.Note, n.UpdatedAt)).ToListAsync();

    public async Task<int> DeleteAllAsync(string user)
    {
        var cnt = await _db.EquipmentAnalyses.CountAsync();
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM EquipmentAnalyses");
        await LogAsync("전체 삭제", $"측정 데이터 {cnt}행 삭제", user);
        return cnt;
    }

    public async Task<IReadOnlyList<ActionLogDto>> GetActionLogAsync()
        => await _db.EquipmentActionLogs.OrderByDescending(l => l.Id).Take(500)
            .Select(l => new ActionLogDto(l.Id, l.ActionType, l.Detail, l.UserName, l.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"))).ToListAsync();
}
