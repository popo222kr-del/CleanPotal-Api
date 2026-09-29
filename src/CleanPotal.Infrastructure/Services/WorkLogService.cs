using System.Text.RegularExpressions;
using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// 업무 파일 통합 관리 — 설비 목록 · 약액(CHEMICAL) 교체 기록 · 업무보고(세정/BAKE).
///
/// 엑셀 "CHEMICAL 교체 및 설비 변경점" 은 날짜 × 설비 표에 "S2 100%,HF100%" 처럼 적고 셀 메모로 설비 변경점을 남겼다.
/// 여기서는 칸 하나 = (날짜, 설비) 한 줄로 두고, 업무보고는 그 날 칸을 설비·공정 목록에 붙여 자동으로 만든다.
/// </summary>
public partial class WorkLogService
{
    private readonly CleanPotalDbContext _db;
    public WorkLogService(CleanPotalDbContext db) => _db = db;

    /// <summary>
    /// 처음 쓸 때 채우는 설비 목록 — 엑셀 업무보고(2.세정/BAKE)의 설비·공정 그대로.
    /// 이후에는 화면(설비 목록)에서 고친다.
    /// </summary>
    private static readonly (string Line, string Code, string Process)[] DefaultEquipment =
    {
        ("METAL", "MDC01", "POLY(L10)"), ("METAL", "MDC02", "Hot Chemical(L30)"), ("METAL", "MDC03", "Hot Chemical(L30)"),
        ("METAL", "MDC04", "POLY(G40)"), ("METAL", "MDC05", "TEOS"), ("METAL", "MDC06", "ALO/HFO(L30)"),
        ("METAL", "MDC07", "POLY(L10)"), ("METAL", "MDC08", "N,G,D-POLY"), ("METAL", "MDC09", "SIGE(L20)"),
        ("METAL", "MDC10", "ALO/HFO(L30)"), ("METAL", "MSC02-1", "POLY(대대배치)"), ("METAL", "MSC02-2", "RINSE 전용"),
        ("METAL", "MBO01-1", "BAKE"), ("METAL", "MBO01-2", "BAKE"), ("METAL", "MBO02-1", "BAKE"), ("METAL", "MBO02-2", "BAKE"),
        ("METAL", "MBO03-1", "BAKE"), ("METAL", "MBO03-2", "BAKE"),
        ("N-METAL", "MBO04-1", "BAKE"), ("N-METAL", "MBO04-2", "BAKE"),
        ("N-METAL", "NDC01", "WOOAM"), ("N-METAL", "NDC02", "OXIDE"), ("N-METAL", "NDC03", "A급(G07)"), ("N-METAL", "NDC04", "A급(G07)"),
        ("N-METAL", "NDC05", "N,G,D-POLY"), ("N-METAL", "NDC06", "Hot Chemical(L30)"), ("N-METAL", "NDC07", "SIN"), ("N-METAL", "NDC08", "OTT"),
        ("N-METAL", "NBO01-1", "BAKE"), ("N-METAL", "NBO01-2", "BAKE"), ("N-METAL", "NBO02-1", "BAKE"), ("N-METAL", "NBO02-2", "BAKE"),
        ("N-METAL", "NBO03-1", "BAKE"), ("N-METAL", "NBO03-2", "BAKE"),
    };

    public const string KindClean = "세정";
    public const string KindBake = "BAKE";

    /// <summary>코드로 설비 종류를 짐작한다 — xBOnn 은 BAKE 오븐, 그 밖은 세정 설비.</summary>
    public static string GuessKind(string code) => BakePattern().IsMatch(code) ? KindBake : KindClean;
    /// <summary>코드로 라인을 짐작한다 — N 으로 시작하면 N-METAL.</summary>
    public static string GuessLine(string code) => code.StartsWith('N') ? "N-METAL" : "METAL";

    [GeneratedRegex("^[A-Z]BO\\d", RegexOptions.CultureInvariant)]
    private static partial Regex BakePattern();

    /// <summary>
    /// 설비 코드 정리 — 대문자·공백 제거, 숫자 앞의 0 은 맞춘다(MSC1-1 과 MSC01-1 을 같은 설비로).
    /// 엑셀마다 같은 설비를 조금씩 다르게 적어 두었다(MSC1-1 / MSC01-1, "MDC 01").
    /// </summary>
    public static string NormalizeCode(string? raw)
    {
        var s = Regex.Replace((raw ?? "").Trim().ToUpperInvariant(), @"\s+", "");
        return Regex.Replace(s, @"^([A-Z]+)0*(\d+)", m => m.Groups[1].Value + m.Groups[2].Value.PadLeft(2, '0'));
    }

    private static WorkEquipmentDto ToDto(WorkEquipment e) => new(e.Id, e.Code, e.Line, e.Kind, e.Process, e.SortOrder, e.IsActive);

    /// <summary>설비 목록(비어 있으면 업무보고 기준 목록으로 채운다).</summary>
    public async Task<IReadOnlyList<WorkEquipmentDto>> GetEquipmentAsync(bool activeOnly = false)
    {
        await EnsureEquipmentAsync();
        var q = _db.WorkEquipments.AsNoTracking();
        if (activeOnly) q = q.Where(e => e.IsActive);
        return (await q.OrderBy(e => e.SortOrder).ThenBy(e => e.Code).ToListAsync()).Select(ToDto).ToList();
    }

    private async Task EnsureEquipmentAsync()
    {
        if (await _db.WorkEquipments.AnyAsync()) return;
        var order = 0;
        foreach (var (line, code, process) in DefaultEquipment)
            _db.WorkEquipments.Add(new WorkEquipment
            {
                Code = code, Line = line, Kind = GuessKind(code), Process = process, SortOrder = ++order, IsActive = true,
            });
        await _db.SaveChangesAsync();
    }

    /// <summary>설비 목록 저장. 보낸 순서가 곧 표시 순서, 빠진 설비는 끈다(지난 기록이 있으므로 지우지 않는다).</summary>
    public async Task<IReadOnlyList<WorkEquipmentDto>> SaveEquipmentAsync(IReadOnlyList<WorkEquipmentDto> items)
    {
        await EnsureEquipmentAsync();
        var all = await _db.WorkEquipments.ToListAsync();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var order = 0;
        foreach (var it in items ?? Array.Empty<WorkEquipmentDto>())
        {
            var code = NormalizeCode(it.Code);
            if (code.Length == 0) continue;
            if (!seen.Add(code)) throw new BusinessRuleException($"설비 코드 '{code}' 가 두 번 있습니다.");
            if (code.Length > 30) throw new BusinessRuleException($"설비 코드 '{code}' 가 너무 깁니다.");
            var e = all.FirstOrDefault(x => x.Id == it.Id && it.Id > 0) ?? all.FirstOrDefault(x => x.Code == code);
            if (e is null) { e = new WorkEquipment(); _db.WorkEquipments.Add(e); all.Add(e); }
            else if (e.Code != code)
            {
                // 코드를 바꾸면 지난 기록도 새 코드로 옮긴다.
                if (all.Any(x => x != e && x.Code == code)) throw new BusinessRuleException($"'{code}' 설비가 이미 있습니다.");
                var old = e.Code;
                await _db.ChemicalChanges.Where(c => c.EqCode == old).ExecuteUpdateAsync(u => u.SetProperty(c => c.EqCode, code));
            }
            e.Code = code;
            e.Line = (it.Line ?? "").Trim() is { Length: > 0 } l ? l : GuessLine(code);
            e.Kind = it.Kind is KindBake or KindClean ? it.Kind : GuessKind(code);
            e.Process = (it.Process ?? "").Trim();
            if (e.Process.Length > 60) e.Process = e.Process[..60];
            e.SortOrder = ++order;
            e.IsActive = it.IsActive;
        }
        foreach (var e in all.Where(x => !seen.Contains(x.Code))) e.IsActive = false;
        await _db.SaveChangesAsync();
        return await GetEquipmentAsync();
    }

    // ───────── 약액 교체 ─────────

    private static ChemicalChangeDto ToDto(ChemicalChange c) => new(c.Date, c.EqCode, c.Content, c.Note, c.UpdatedBy, c.UpdatedAt);

    public async Task<ChemicalMonthDto> GetChemicalMonthAsync(int year, int month)
    {
        if (month is < 1 or > 12 || year is < 2000 or > 2100) throw new BusinessRuleException("연·월이 올바르지 않습니다.");
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var cells = await _db.ChemicalChanges.AsNoTracking()
            .Where(c => c.Date >= from && c.Date <= to).OrderBy(c => c.Date).ThenBy(c => c.EqCode).ToListAsync();
        var eq = await GetEquipmentAsync();
        // 끈 설비도 그 달에 기록이 있으면 열을 보여 준다(지난 달 기록을 볼 때).
        var used = cells.Select(c => c.EqCode).ToHashSet();
        return new ChemicalMonthDto(year, month, eq.Where(e => e.IsActive || used.Contains(e.Code)).ToList(), cells.Select(ToDto).ToList());
    }

    /// <summary>
    /// 교체 내용 정리 — "S2 100%,HF100%" → "S2 100%, HF 100%". 약액 이름과 비율 사이·항목 사이 띄어쓰기만 맞춘다.
    /// </summary>
    public static string NormalizeContent(string? raw)
    {
        var parts = (raw ?? "").Replace('，', ',').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // 약액 이름 = 영문 + 숫자 한 자리까지(S2, HF, DI, HNO3), 비율은 0 으로 시작하지 않는다 —
        // 붙여 쓴 "S2100%" 는 S2 / 100%, "HF100%" 는 HF / 100% 로 나뉜다(HF1 / 00% 가 아니라).
        var fixedParts = parts.Select(p => Regex.Replace(p, @"^([A-Za-z]+\d?)\s*((?:[1-9]\d*|0)(?:\.\d+)?\s*%)$",
            m => $"{m.Groups[1].Value} {m.Groups[2].Value.Replace(" ", "")}"));
        return string.Join(", ", fixedParts);
    }

    public async Task<ChemicalChangeDto?> SaveChemicalAsync(ChemicalSaveRequest r, string actor)
    {
        var code = NormalizeCode(r.EqCode);
        if (code.Length == 0) throw new BusinessRuleException("설비를 지정하세요.");
        await EnsureEquipmentAsync();
        if (!await _db.WorkEquipments.AnyAsync(e => e.Code == code)) throw new BusinessRuleException($"'{code}' 설비가 목록에 없습니다.");
        var content = NormalizeContent(r.Content);
        var note = (r.Note ?? "").Trim();
        if (content.Length > 200) throw new BusinessRuleException("교체 내용이 너무 깁니다(200자까지).");
        if (note.Length > 1000) throw new BusinessRuleException("메모가 너무 깁니다(1000자까지).");

        var row = await _db.ChemicalChanges.FirstOrDefaultAsync(c => c.Date == r.Date && c.EqCode == code);
        if (content.Length == 0 && note.Length == 0)
        {
            if (row is not null) { _db.ChemicalChanges.Remove(row); await _db.SaveChangesAsync(); }
            return null;
        }
        if (row is null) { row = new ChemicalChange { Date = r.Date, EqCode = code }; _db.ChemicalChanges.Add(row); }
        row.Content = content;
        row.Note = note;
        row.UpdatedBy = actor;
        row.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return ToDto(row);
    }

    /// <summary>
    /// 엑셀 가져오기. 모르는 설비 코드는 목록에 새로 넣는다(자료를 버리지 않게).
    /// <paramref name="overwrite"/> 가 false 면 이미 적힌 칸은 건너뛴다.
    /// </summary>
    public async Task<ChemicalImportResultDto> ImportChemicalAsync(IReadOnlyList<ChemicalImportCell> cells, bool overwrite, string actor)
    {
        if (cells is null || cells.Count == 0) throw new BusinessRuleException("가져올 칸이 없습니다.");
        if (cells.Count > 20000) throw new BusinessRuleException("한 번에 2만 칸까지 가져올 수 있습니다.");
        await EnsureEquipmentAsync();
        var eq = await _db.WorkEquipments.ToListAsync();
        var newEq = new List<string>();
        var order = eq.Count == 0 ? 0 : eq.Max(e => e.SortOrder);
        var from = cells.Min(c => c.Date);
        var to = cells.Max(c => c.Date);
        var existing = (await _db.ChemicalChanges.Where(c => c.Date >= from && c.Date <= to).ToListAsync())
            .ToDictionary(c => (c.Date, c.EqCode));
        int added = 0, updated = 0, skipped = 0;
        foreach (var cell in cells)
        {
            var code = NormalizeCode(cell.EqCode);
            var content = NormalizeContent(cell.Content);
            var note = (cell.Note ?? "").Trim();
            if (code.Length == 0 || code.Length > 30 || (content.Length == 0 && note.Length == 0)) { skipped++; continue; }
            if (content.Length > 200) content = content[..200];
            if (note.Length > 1000) note = note[..1000];
            if (eq.All(e => e.Code != code))
            {
                var e = new WorkEquipment { Code = code, Line = GuessLine(code), Kind = GuessKind(code), SortOrder = ++order, IsActive = true };
                _db.WorkEquipments.Add(e); eq.Add(e); newEq.Add(code);
            }
            if (existing.TryGetValue((cell.Date, code), out var row))
            {
                if (!overwrite) { skipped++; continue; }
                row.Content = content; row.Note = note; row.UpdatedBy = actor; row.UpdatedAt = DateTime.Now;
                updated++;
                continue;
            }
            row = new ChemicalChange { Date = cell.Date, EqCode = code, Content = content, Note = note, UpdatedBy = actor, UpdatedAt = DateTime.Now };
            _db.ChemicalChanges.Add(row);
            existing[(cell.Date, code)] = row;
            added++;
        }
        await _db.SaveChangesAsync();
        return new ChemicalImportResultDto(added, updated, skipped, newEq);
    }

    // ───────── 업무보고 ─────────

    /// <summary>
    /// 업무보고(세정/BAKE) — 쓰는 설비 목록에 그날 약액 교체 칸을 붙인다. 오늘 교체가 없는 설비는
    /// 마지막 교체일·내용을 함께 보여 준다(얼마나 됐는지 보려고).
    /// </summary>
    public async Task<WorkReportDto> GetReportAsync(DateOnly date)
    {
        var eq = await GetEquipmentAsync(activeOnly: true);
        var today = (await _db.ChemicalChanges.AsNoTracking().Where(c => c.Date == date).ToListAsync())
            .ToDictionary(c => c.EqCode);
        var codes = eq.Select(e => e.Code).ToList();
        // 설비별 마지막 교체(오늘 이전, 교체 내용이 있는 칸)
        // (한 해 수천 칸이라 가져와서 고른다 — 묶음 정렬 쿼리는 DB 마다 번역이 달라 피한다)
        var since = date.AddYears(-2);
        var last = (await _db.ChemicalChanges.AsNoTracking()
                .Where(c => c.Date < date && c.Date >= since && c.Content != "" && codes.Contains(c.EqCode))
                .Select(c => new { c.EqCode, c.Date, c.Content }).ToListAsync())
            .GroupBy(c => c.EqCode)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Date).First());
        var rows = eq.Select(e =>
        {
            today.TryGetValue(e.Code, out var t);
            last.TryGetValue(e.Code, out var l);
            return new WorkReportRowDto(e.Line, e.Kind, e.Code, e.Process, t?.Content ?? "", t?.Note ?? "",
                l?.Date, l?.Content ?? "");
        }).ToList();
        return new WorkReportDto(date, rows, rows.Count(r => r.Content.Length > 0));
    }

    // ───────── 가성소다·폐액 ─────────
    // 엑셀 "가성소다, 폐액 증가량 및 약액 교체 현황" — 날짜 × 주/야 한 줄씩. 2019년부터의 월별 시트를 가져와 추이를 본다.

    public const string ShiftDay = "주";
    public const string ShiftNight = "야";
    private static int ShiftOrder(string s) => s == ShiftNight ? 1 : 0;

    private static decimal? Diff(decimal? a, decimal? b) => a is { } x && b is { } y ? x - y : null;

    private static WasteLogDto ToDto(WasteLog w) => new(
        w.Date, w.Shift,
        w.CausticBefore, w.CausticAfter, Diff(w.CausticBefore, w.CausticAfter),
        w.WasteBefore, w.WasteAfter, Diff(w.WasteAfter, w.WasteBefore),
        w.DipEquipment, w.SprayEquipment, w.DailyChange, w.Note, w.UpdatedBy);

    public async Task<WasteMonthDto> GetWasteMonthAsync(int year, int month)
    {
        if (month is < 1 or > 12 || year is < 2000 or > 2100) throw new BusinessRuleException("연·월이 올바르지 않습니다.");
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var rows = (await _db.WasteLogs.AsNoTracking().Where(w => w.Date >= from && w.Date <= to).ToListAsync())
            .OrderBy(w => w.Date).ThenBy(w => ShiftOrder(w.Shift)).ToList();
        // 달 첫 줄의 前 값 = 그 전 마지막 줄의 現 값
        var prev = (await _db.WasteLogs.AsNoTracking().Where(w => w.Date < from).OrderByDescending(w => w.Date).Take(2).ToListAsync())
            .OrderByDescending(w => w.Date).ThenByDescending(w => ShiftOrder(w.Shift)).FirstOrDefault();
        // 날짜별 약액 교체 설비 — Dip/Spray 칸을 약액 교체 기록에서 채울 수 있게
        var chem = (await _db.ChemicalChanges.AsNoTracking().Where(c => c.Date >= from && c.Date <= to && c.Content != "")
                .Select(c => new { c.Date, c.EqCode }).ToListAsync())
            .GroupBy(c => c.Date.ToString("yyyy-MM-dd"))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(c => c.EqCode).OrderBy(c => c).ToList());
        return new WasteMonthDto(year, month, rows.Select(ToDto).ToList(), prev?.CausticAfter, prev?.WasteAfter, chem);
    }

    private static string CleanShift(string? s) => (s ?? "").Trim() switch
    {
        "주" or "주간" or "D" or "Day" => ShiftDay,
        "야" or "야간" or "N" or "Night" => ShiftNight,
        _ => throw new BusinessRuleException("교대는 주 또는 야 여야 합니다."),
    };

    private static bool IsEmpty(WasteSaveRequest r)
        => r.CausticBefore is null && r.CausticAfter is null && r.WasteBefore is null && r.WasteAfter is null
           && string.IsNullOrWhiteSpace(r.DipEquipment) && string.IsNullOrWhiteSpace(r.SprayEquipment)
           && r.DailyChange is null && string.IsNullOrWhiteSpace(r.Note);

    private static void Apply(WasteLog w, WasteSaveRequest r, string actor)
    {
        static string Cut(string? s, int n) { var t = (s ?? "").Trim(); return t.Length > n ? t[..n] : t; }
        w.CausticBefore = r.CausticBefore; w.CausticAfter = r.CausticAfter;
        w.WasteBefore = r.WasteBefore; w.WasteAfter = r.WasteAfter;
        w.DipEquipment = Cut(r.DipEquipment, 200); w.SprayEquipment = Cut(r.SprayEquipment, 200);
        w.DailyChange = r.DailyChange; w.Note = Cut(r.Note, 1000);
        w.UpdatedBy = actor; w.UpdatedAt = DateTime.Now;
    }

    /// <summary>한 줄 저장. 값이 모두 비면 그 줄을 지운다.</summary>
    public async Task<WasteLogDto?> SaveWasteAsync(WasteSaveRequest r, string actor)
    {
        var shift = CleanShift(r.Shift);
        var row = await _db.WasteLogs.FirstOrDefaultAsync(w => w.Date == r.Date && w.Shift == shift);
        if (IsEmpty(r))
        {
            if (row is not null) { _db.WasteLogs.Remove(row); await _db.SaveChangesAsync(); }
            return null;
        }
        if (row is null) { row = new WasteLog { Date = r.Date, Shift = shift }; _db.WasteLogs.Add(row); }
        Apply(row, r, actor);
        await _db.SaveChangesAsync();
        return ToDto(row);
    }

    /// <summary>엑셀(2019년~ 월별 시트) 가져오기. <paramref name="overwrite"/> 가 false 면 이미 있는 줄은 건너뛴다.</summary>
    public async Task<WasteImportResultDto> ImportWasteAsync(IReadOnlyList<WasteSaveRequest> rows, bool overwrite, string actor)
    {
        if (rows is null || rows.Count == 0) throw new BusinessRuleException("가져올 줄이 없습니다.");
        if (rows.Count > 20000) throw new BusinessRuleException("한 번에 2만 줄까지 가져올 수 있습니다.");
        var from = rows.Min(r => r.Date);
        var to = rows.Max(r => r.Date);
        var existing = (await _db.WasteLogs.Where(w => w.Date >= from && w.Date <= to).ToListAsync())
            .ToDictionary(w => (w.Date, w.Shift));
        int added = 0, updated = 0, skipped = 0;
        foreach (var r in rows)
        {
            string shift;
            try { shift = CleanShift(r.Shift); } catch (BusinessRuleException) { skipped++; continue; }
            if (IsEmpty(r)) { skipped++; continue; }
            if (existing.TryGetValue((r.Date, shift), out var row))
            {
                if (!overwrite) { skipped++; continue; }
                Apply(row, r, actor); updated++;
                continue;
            }
            row = new WasteLog { Date = r.Date, Shift = shift };
            Apply(row, r, actor);
            _db.WasteLogs.Add(row);
            existing[(r.Date, shift)] = row;
            added++;
        }
        await _db.SaveChangesAsync();
        return new WasteImportResultDto(added, updated, skipped, from, to);
    }

    /// <summary>
    /// 월별 추이(처음 기록부터 지금까지) — 가성소다 사용량(감소량 합)·폐액 증가량 합·기록한 날·교체 설비 수.
    /// 한 줄의 前·現 이 모두 있어야 그 줄의 감소·증가를 센다(빠진 칸을 0 으로 보지 않는다).
    /// </summary>
    public async Task<IReadOnlyList<WasteTrendPointDto>> GetWasteTrendAsync()
    {
        var all = await _db.WasteLogs.AsNoTracking()
            .Select(w => new { w.Date, w.CausticBefore, w.CausticAfter, w.WasteBefore, w.WasteAfter, w.DipEquipment, w.SprayEquipment })
            .ToListAsync();
        static int Count(string s) => s.Split(new[] { ',', '/', '·' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        return all.GroupBy(w => (w.Date.Year, w.Date.Month)).OrderBy(g => g.Key)
            .Select(g => new WasteTrendPointDto(g.Key.Year, g.Key.Month,
                g.Sum(w => Diff(w.CausticBefore, w.CausticAfter) ?? 0),
                g.Sum(w => Diff(w.WasteAfter, w.WasteBefore) ?? 0),
                g.Select(w => w.Date).Distinct().Count(),
                g.Sum(w => Count(w.DipEquipment) + Count(w.SprayEquipment))))
            .ToList();
    }
}
