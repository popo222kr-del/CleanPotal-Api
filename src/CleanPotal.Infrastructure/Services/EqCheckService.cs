using System.Globalization;
using System.Text.Json;
using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// 체크시트 (설비) — 설비 점검표 AQ-C-13 Rev.7 을 웹으로 옮긴 것.
///
/// 주기와 기간: 일상 = 하루 1회(근무일 — 07시 전은 전날), 주간 = 그 주 금요일 09시(기간 키 = 그 주 금요일 날짜),
/// 월간 = 매월 첫째 주 금요일 09시(기간 키 = yyyy-MM). 기한 전이라도 그 기간 안이면 언제든 적을 수 있다.
/// 누가: 일상·주간은 생산팀(체크시트 조회 등급이면 된다), 월간은 설비팀(설정 '월간 점검 부서·팀' 에 적힌 부서·팀 사람).
/// 판정: O=정상, △·X=NG(△ 도 NG — 2026-09-30 확인). 보기는 첫 보기가 정상, '*' 보기(조치함)는 NG 로 남기되 바로 조치 완료.
/// 수치는 기준 범위, 여러 칸은 첫 칸과의 편차로 판정. 가동 중에만 재는 항목은 '비가동' 이면 정상으로 둔다.
/// </summary>
public class EqCheckService
{
    public const string MonthlyTeamsKey = "EqMonthlyTeams";
    public const string DefaultMonthlyTeams = "설비팀";
    public const string AutoClosePrefix = "현장 조치: ";

    private readonly CleanPotalDbContext _db;
    private readonly TimeProvider _clock;
    public EqCheckService(CleanPotalDbContext db) : this(db, TimeProvider.System) { }
    public EqCheckService(CleanPotalDbContext db, TimeProvider clock) { _db = db; _clock = clock; }

    private DateTime Now => _clock.GetLocalNow().DateTime;

    // ───────── 기간 ─────────

    /// <summary>그 주(월~일)의 금요일.</summary>
    public static DateOnly WeekFriday(DateOnly d)
    {
        var iso = ((int)d.DayOfWeek + 6) % 7 + 1;   // 월=1 … 일=7
        return d.AddDays(5 - iso);
    }

    /// <summary>그 달의 첫째 금요일 — 월간 점검 기한.</summary>
    public static DateOnly FirstFriday(int year, int month)
    {
        var d = new DateOnly(year, month, 1);
        while (d.DayOfWeek != DayOfWeek.Friday) d = d.AddDays(1);
        return d;
    }

    public static string PeriodKeyOf(string cycle, DateOnly d) => cycle switch
    {
        EqCycles.Daily or EqCycles.Fault => d.ToString("yyyy-MM-dd"),
        EqCycles.Weekly => WeekFriday(d).ToString("yyyy-MM-dd"),
        EqCycles.Monthly => d.ToString("yyyy-MM"),
        _ => throw new BusinessRuleException($"주기가 올바르지 않습니다: {cycle}"),
    };

    /// <summary>기간 키 → (시작, 기한, 끝). 키가 그 주기의 키가 아니면 예외.</summary>
    public static (DateOnly Start, DateOnly Due, DateOnly End) Period(string cycle, string key)
    {
        if (cycle == EqCycles.Monthly)
        {
            if (!DateOnly.TryParseExact(key + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m))
                throw new BusinessRuleException($"기간이 올바르지 않습니다: {key}");
            return (m, FirstFriday(m.Year, m.Month), m.AddMonths(1).AddDays(-1));
        }
        if (!DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            throw new BusinessRuleException($"기간이 올바르지 않습니다: {key}");
        if (cycle == EqCycles.Weekly)
        {
            if (d.DayOfWeek != DayOfWeek.Friday) throw new BusinessRuleException($"주간 점검 기간은 금요일 날짜입니다: {key}");
            return (d.AddDays(-4), d, d.AddDays(2));
        }
        if (cycle is EqCycles.Daily or EqCycles.Fault) return (d, d, d);
        throw new BusinessRuleException($"주기가 올바르지 않습니다: {cycle}");
    }

    private static string Dow(DateOnly d) => "월화수목금토일"[((int)d.DayOfWeek + 6) % 7].ToString();

    public static string PeriodLabel(string cycle, string key)
    {
        var (start, due, end) = Period(cycle, key);
        return cycle switch
        {
            EqCycles.Weekly => $"{start:M'/'d}~{end:M'/'d} 주 (기한 {due:M'/'d} 금 09시)",
            EqCycles.Monthly => $"{start:yyyy}년 {start.Month}월 (기한 {due:M'/'d} 첫째 주 금 09시)",
            _ => $"{start:M'/'d}({Dow(start)})",
        };
    }

    /// <summary>근무일 — 교대 시작(기본 07시) 전이면 전날 근무. 일상 점검 하루가 여기에 맞춰진다.</summary>
    private async Task<DateOnly> TodayAsync()
    {
        var set = await _db.CheckSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "DayStart");
        var start = TimeOnly.TryParse(set?.Value, CultureInfo.InvariantCulture, out var t) ? t : new TimeOnly(7, 0);
        var now = Now;
        var d = DateOnly.FromDateTime(now);
        return TimeOnly.FromDateTime(now) < start ? d.AddDays(-1) : d;
    }

    public static string State(string cycle, string key, DateOnly today, int done, int total)
    {
        if (total > 0 && done >= total) return "done";
        var (_, due, end) = Period(cycle, key);
        if (today > end) return "late";
        if (today > due) return "late";
        if (today == due) return "due";
        return done > 0 ? "partial" : "todo";
    }

    // ───────── 권한 ─────────

    public async Task<string> MonthlyTeamsAsync()
        => (await _db.CheckSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == MonthlyTeamsKey))?.Value ?? DefaultMonthlyTeams;

    /// <summary>월간 점검을 할 수 있는 사람 — 관리자, 또는 부서·팀 이름에 설정의 이름(쉼표로 여러 개)이 들어간 사람.</summary>
    public static bool IsMonthlyTeam(EqCheckActor a, string teams)
    {
        if (a.IsAdmin) return true;
        var names = teams.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0) return a.CanEdit;
        return names.Any(n => a.Department.Contains(n, StringComparison.OrdinalIgnoreCase)
                              || a.TeamName.Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>이 기간을 적을 수 있나 — 지금 기간과 바로 앞 기간(늦게 적는 경우)만. 관리자는 언제든. 월간은 설비팀만.</summary>
    public static string? CannotEdit(EqCheckActor a, string cycle, string key, DateOnly today, bool monthlyTeam)
    {
        if (cycle == EqCycles.Monthly && !monthlyTeam) return "월간 점검은 설비팀이 합니다.";
        if (a.IsAdmin) return null;
        var (start, _, _) = Period(cycle, key);
        var cur = PeriodKeyOf(cycle, today);
        var prev = cycle switch
        {
            EqCycles.Daily => PeriodKeyOf(cycle, today.AddDays(-1)),
            EqCycles.Weekly => PeriodKeyOf(cycle, today.AddDays(-7)),
            _ => PeriodKeyOf(cycle, today.AddMonths(-1)),
        };
        if (key == cur || key == prev) return null;
        return start > today ? "아직 점검할 기간이 아닙니다." : "지난 기간은 관리자만 고칠 수 있습니다.";
    }

    // ───────── 판정 ─────────

    public static IReadOnlyList<(string Text, bool Action)> ParseOptions(string options)
        => options.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(o => o.StartsWith('*') ? (o[1..].Trim(), true) : (o, false)).ToList();

    public static IReadOnlyList<string> ParseFields(string fields)
        => fields.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Fmt(decimal v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>판정 — (OK/NG/빈 칸=아직 다 안 적음, 보기 조치함 여부, 저장할 값).</summary>
    public static (string Judge, bool AutoClose, string Value) Judge(EqCheckItem i, string? value, IReadOnlyDictionary<string, decimal?> nums, bool notRunning)
    {
        value = (value ?? "").Trim();
        switch (i.InputType)
        {
            case EqInputTypes.Oxa:
                value = value.ToUpperInvariant() switch { "O" or "○" => "O", "△" or "D" => "△", "X" or "×" => "X", _ => value };
                if (value is not ("O" or "△" or "X")) throw new BusinessRuleException("O / △ / X 중에서 고르세요.");
                return (value == "O" ? "OK" : "NG", false, value);
            case EqInputTypes.Choice:
            {
                var opts = ParseOptions(i.Options);
                var idx = opts.ToList().FindIndex(o => o.Text == value);
                if (idx < 0) throw new BusinessRuleException($"보기 중에서 고르세요: {string.Join(" / ", opts.Select(o => o.Text))}");
                return (idx == 0 ? "OK" : "NG", idx > 0 && opts[idx].Action, value);
            }
            case EqInputTypes.Num:
            {
                if (i.RunOnly && notRunning) return ("OK", false, "");
                var v = nums.GetValueOrDefault("");
                if (v is null) return ("", false, "");
                var ok = (i.Min is null || v >= i.Min) && (i.Max is null || v <= i.Max);
                return (ok ? "OK" : "NG", false, "");
            }
            case EqInputTypes.Multi:
            {
                if (i.RunOnly && notRunning) return ("OK", false, "");
                var fields = ParseFields(i.Fields);
                var vals = fields.Select(f => nums.GetValueOrDefault(f)).ToList();
                if (vals.Any(v => v is null)) return ("", false, "");
                if (i.Max is not { } tol) return ("OK", false, "");
                var first = vals[0]!.Value;
                return (vals.Skip(1).All(v => Math.Abs(v!.Value - first) <= tol) ? "OK" : "NG", false, "");
            }
        }
        throw new BusinessRuleException($"입력 방식이 올바르지 않습니다: {i.InputType}");
    }

    public static Dictionary<string, decimal?> ParseNums(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, decimal?>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static string ValueText(string inputType, string value, string numsJson, bool notRunning, string unit, string fields)
    {
        if (notRunning) return "비가동";
        var nums = ParseNums(numsJson);
        return inputType switch
        {
            EqInputTypes.Num => nums.GetValueOrDefault("") is { } v ? $"{Fmt(v)}{(unit.Length > 0 ? " " + unit : "")}" : "",
            EqInputTypes.Multi => string.Join(" / ", ParseFields(fields).Select(f => $"{f} {(nums.GetValueOrDefault(f) is { } x ? Fmt(x) : "-")}")),
            _ => value,
        };
    }

    // ───────── 조회용 변환 ─────────

    private static EqCheckItemDto ToDto(EqCheckItem i) => new(i.Id, i.TemplateId, i.Cycle, i.SortOrder, i.Category, i.Name, i.Point, i.Spec,
        i.InputType, i.Options, i.Fields, i.Unit, i.Min, i.Max, i.RunOnly, i.IsActive);

    private static EqCheckResultDto ToDto(EqCheckResult r, EqCheckItem? item) => new(r.Id, r.ItemId, r.Value, ParseNums(r.Nums), r.NotRunning,
        r.Judge, r.Memo, ValueText(r.InputType, r.Value, r.Nums, r.NotRunning, item?.Unit ?? "", item?.Fields ?? ""),
        r.CheckedByName, r.CheckedAt, r.NgStatus, r.NgCloseNote, r.NgClosedBy, r.NgClosedAt);

    /// <summary>호기 코드 → (라인, 공정) — 설비 목록(스케줄 보드 설비)에서 챔버 번호를 뗀 코드로 찾는다.</summary>
    private async Task<Dictionary<string, (string Line, string Process)>> EquipInfoAsync()
    {
        var eq = await _db.ScheduleEquipments.AsNoTracking().Where(e => e.IsActive)
            .OrderBy(e => e.OrderIndex).Select(e => new { e.Name, e.Line, e.Process }).ToListAsync();
        var map = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var e in eq)
        {
            var code = EquipmentCatalog.UnitCode(e.Name);
            if (!map.ContainsKey(code)) map[code] = (e.Line.Length > 0 ? e.Line : EquipmentCatalog.GuessLine(e.Name), e.Process);
        }
        return map;
    }

    private static EqCheckUnitDto UnitDto(EqCheckUnit u, IReadOnlyDictionary<int, EqCheckTemplate> tpl, IReadOnlyDictionary<string, (string Line, string Process)> eq)
    {
        var has = eq.TryGetValue(u.Code, out var info);
        return new EqCheckUnitDto(u.Id, u.Code, u.TemplateId, tpl.TryGetValue(u.TemplateId, out var t) ? t.Name : "", u.IsActive, u.Note,
            has ? info.Line : EquipmentCatalog.GuessLine(u.Code), has ? info.Process : "", has);
    }

    private async Task<EqCheckUnit> UnitAsync(string code)
        => await _db.EqCheckUnits.AsNoTracking().FirstOrDefaultAsync(u => u.Code == code && u.IsActive)
           ?? throw new BusinessRuleException($"'{code}' 설비 점검표를 찾을 수 없습니다. QR 을 다시 확인하세요.");

    // ───────── 현장(QR) 화면 ─────────

    /// <summary>호기 한 대의 점검 화면 — 일상(그날)·주간(그 주)·월간(그 달). date 를 주면 그날이 든 기간.</summary>
    public async Task<EqCheckSheetDto?> GetSheetAsync(string code, DateOnly? date, EqCheckActor actor)
    {
        var unit = await _db.EqCheckUnits.AsNoTracking().FirstOrDefaultAsync(u => u.Code == code && u.IsActive);
        if (unit is null) return null;
        var today = await TodayAsync();
        var d = date ?? today;
        var monthly = IsMonthlyTeam(actor, await MonthlyTeamsAsync());
        var tpl = await _db.EqCheckTemplates.AsNoTracking().ToDictionaryAsync(t => t.Id);
        var items = await _db.EqCheckItems.AsNoTracking().Where(i => i.TemplateId == unit.TemplateId)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToListAsync();
        var periods = new List<EqCheckPeriodDto>();
        foreach (var cycle in EqCycles.All)
        {
            var key = PeriodKeyOf(cycle, d);
            var rec = await _db.EqCheckRecords.AsNoTracking().FirstOrDefaultAsync(r => r.UnitCode == code && r.Cycle == cycle && r.PeriodKey == key);
            var results = rec is null ? new List<EqCheckResult>()
                : await _db.EqCheckResults.AsNoTracking().Where(r => r.RecordId == rec.Id && r.ItemId > 0).ToListAsync();
            // 지금 쓰는 항목 + 그 기간에 이미 적은 옛 항목(양식을 고친 뒤에도 적힌 기록은 보인다)
            var used = results.Select(r => r.ItemId).ToHashSet();
            var list = items.Where(i => i.Cycle == cycle && (i.IsActive || used.Contains(i.Id))).ToList();
            var byId = list.ToDictionary(i => i.Id);
            var active = list.Where(i => i.IsActive).Select(i => i.Id).ToHashSet();
            var done = results.Count(r => active.Contains(r.ItemId) && r.Judge != "");
            var why = CannotEdit(actor, cycle, key, today, monthly);
            periods.Add(new EqCheckPeriodDto(cycle, key, PeriodLabel(cycle, key), Period(cycle, key).Due,
                State(cycle, key, today, done, active.Count), why is null, why ?? "",
                list.Select(ToDto).ToList(), results.Select(r => ToDto(r, byId.GetValueOrDefault(r.ItemId))).ToList(),
                rec?.Note ?? "", done, active.Count, results.Count(r => r.Judge == "NG")));
        }
        return new EqCheckSheetDto(UnitDto(unit, tpl, await EquipInfoAsync()), today, periods, monthly);
    }

    private async Task<EqCheckRecord> RecordAsync(string code, string cycle, string key, string actor)
    {
        var rec = await _db.EqCheckRecords.FirstOrDefaultAsync(r => r.UnitCode == code && r.Cycle == cycle && r.PeriodKey == key);
        if (rec is not null) return rec;
        rec = new EqCheckRecord { UnitCode = code, Cycle = cycle, PeriodKey = key, UpdatedAt = Now, UpdatedBy = actor };
        _db.EqCheckRecords.Add(rec);
        try
        {
            await _db.SaveChangesAsync();
            return rec;
        }
        catch (DbUpdateException)
        {
            // 두 사람이 같은 순간에 시작 — 먼저 만든 줄을 쓴다
            _db.Entry(rec).State = EntityState.Detached;
            return await _db.EqCheckRecords.FirstAsync(r => r.UnitCode == code && r.Cycle == cycle && r.PeriodKey == key);
        }
    }

    /// <summary>항목 결과 저장. 값·수치를 모두 비우면 지운다. 저장한 결과(지웠으면 null).</summary>
    public async Task<EqCheckResultDto?> SaveResultAsync(string code, int itemId, EqCheckSaveRequest req, EqCheckActor actor)
    {
        var unit = await UnitAsync(code);
        var item = await _db.EqCheckItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.TemplateId == unit.TemplateId)
                   ?? throw new BusinessRuleException("이 설비 점검표의 항목이 아닙니다. 화면을 새로 고쳐 보세요.");
        if (item.Cycle != req.Cycle) throw new BusinessRuleException("주기가 맞지 않습니다. 화면을 새로 고쳐 보세요.");
        var (start, _, _) = Period(req.Cycle, req.PeriodKey);
        if (PeriodKeyOf(req.Cycle, start) != req.PeriodKey) throw new BusinessRuleException($"기간이 올바르지 않습니다: {req.PeriodKey}");
        var today = await TodayAsync();
        var why = CannotEdit(actor, req.Cycle, req.PeriodKey, today, IsMonthlyTeam(actor, await MonthlyTeamsAsync()));
        if (why is not null) throw new BusinessRuleException(why);

        var nums = (req.Nums ?? new()).Where(kv => kv.Value is not null).ToDictionary(kv => (kv.Key ?? "").Trim(), kv => kv.Value);
        var memo = (req.Memo ?? "").Trim();
        if (memo.Length > 500) throw new BusinessRuleException("메모는 500자까지입니다.");
        var rec = await RecordAsync(code, req.Cycle, req.PeriodKey, actor.Username);
        var row = await _db.EqCheckResults.FirstOrDefaultAsync(r => r.RecordId == rec.Id && r.ItemId == item.Id);

        var empty = string.IsNullOrWhiteSpace(req.Value) && nums.Count == 0 && !req.NotRunning;
        if (empty)
        {
            if (row is not null) { _db.EqCheckResults.Remove(row); await _db.SaveChangesAsync(); }
            return null;
        }
        var (judge, autoClose, value) = Judge(item, req.Value, nums, req.NotRunning);
        var numsJson = nums.Count == 0 ? "" : JsonSerializer.Serialize(nums);
        var same = row is not null && row.Value == value && row.Nums == numsJson && row.NotRunning == req.NotRunning;
        if (row is null)
        {
            row = new EqCheckResult { RecordId = rec.Id, UnitCode = code, Cycle = req.Cycle, PeriodKey = req.PeriodKey, ItemId = item.Id };
            _db.EqCheckResults.Add(row);
        }
        row.Category = item.Category; row.Name = item.Name; row.Point = item.Point; row.Spec = item.Spec; row.InputType = item.InputType;
        row.Value = value; row.Nums = numsJson; row.NotRunning = req.NotRunning && item.RunOnly; row.Judge = judge; row.Memo = memo;
        row.CheckedAt = Now; row.CheckedBy = actor.Username; row.CheckedByName = actor.RealName;
        if (judge != "NG") { row.NgStatus = ""; row.NgClosedAt = null; row.NgClosedBy = ""; row.NgCloseNote = ""; }
        else if (autoClose) { row.NgStatus = "DONE"; row.NgClosedAt = Now; row.NgClosedBy = actor.RealName; row.NgCloseNote = AutoClosePrefix + value; }
        else if (!same || row.NgStatus == "") { row.NgStatus = "OPEN"; row.NgClosedAt = null; row.NgClosedBy = ""; row.NgCloseNote = ""; }
        rec.UpdatedAt = Now; rec.UpdatedBy = actor.Username;
        await _db.SaveChangesAsync();
        return ToDto(row, item);
    }

    /// <summary>특이사항(월간 점검표의 특이사항 칸 등).</summary>
    public async Task<string> SaveNoteAsync(string code, EqCheckNoteRequest req, EqCheckActor actor)
    {
        await UnitAsync(code);
        var (start, _, _) = Period(req.Cycle, req.PeriodKey);
        if (PeriodKeyOf(req.Cycle, start) != req.PeriodKey) throw new BusinessRuleException($"기간이 올바르지 않습니다: {req.PeriodKey}");
        var why = CannotEdit(actor, req.Cycle, req.PeriodKey, await TodayAsync(), IsMonthlyTeam(actor, await MonthlyTeamsAsync()));
        if (why is not null) throw new BusinessRuleException(why);
        var note = (req.Note ?? "").Trim();
        if (note.Length > 1000) throw new BusinessRuleException("특이사항은 1000자까지입니다.");
        var rec = await RecordAsync(code, req.Cycle, req.PeriodKey, actor.Username);
        rec.Note = note; rec.UpdatedAt = Now; rec.UpdatedBy = actor.Username;
        await _db.SaveChangesAsync();
        return note;
    }

    /// <summary>점검과 따로 적는 고장·부적합 — 미조치 NG 로 올라간다.</summary>
    public async Task<EqCheckNgDto> AddFaultAsync(EqCheckFaultRequest req, EqCheckActor actor)
    {
        var unit = await UnitAsync(req.UnitCode);
        var text = (req.Text ?? "").Trim();
        if (text.Length == 0) throw new BusinessRuleException("고장·부적합 내용을 적으세요.");
        if (text.Length > 200) throw new BusinessRuleException("내용은 200자까지입니다.");
        var memo = (req.Memo ?? "").Trim();
        if (memo.Length > 500) throw new BusinessRuleException("메모는 500자까지입니다.");
        if (req.Date > await TodayAsync()) throw new BusinessRuleException("앞으로의 날짜에는 적을 수 없습니다.");
        var key = PeriodKeyOf(EqCycles.Fault, req.Date);
        var rec = await RecordAsync(unit.Code, EqCycles.Fault, key, actor.Username);
        var row = new EqCheckResult
        {
            RecordId = rec.Id, UnitCode = unit.Code, Cycle = EqCycles.Fault, PeriodKey = key, ItemId = 0,
            Category = "고장·부적합", Name = text, InputType = "", Judge = "NG", Memo = memo, NgStatus = "OPEN",
            CheckedAt = Now, CheckedBy = actor.Username, CheckedByName = actor.RealName,
        };
        _db.EqCheckResults.Add(row);
        await _db.SaveChangesAsync();
        var eq = await EquipInfoAsync();
        return NgDto(row, eq);
    }

    // ───────── 현황 ─────────

    public async Task<EqCheckStatusDto> GetStatusAsync(DateOnly? date)
    {
        var today = await TodayAsync();
        var d = date ?? today;
        var dayKey = PeriodKeyOf(EqCycles.Daily, d);
        var weekKey = PeriodKeyOf(EqCycles.Weekly, d);
        var monthKey = PeriodKeyOf(EqCycles.Monthly, d);
        var monthStart = new DateOnly(d.Year, d.Month, 1);
        var monthDays = Enumerable.Range(0, d.Day).Select(i => monthStart.AddDays(i).ToString("yyyy-MM-dd")).ToList();

        var units = await _db.EqCheckUnits.AsNoTracking().Where(u => u.IsActive).ToListAsync();
        var tpl = await _db.EqCheckTemplates.AsNoTracking().ToDictionaryAsync(t => t.Id);
        var counts = (await _db.EqCheckItems.AsNoTracking().Where(i => i.IsActive).Select(i => new { i.Id, i.TemplateId, i.Cycle }).ToListAsync());
        var activeIds = counts.Select(c => c.Id).ToHashSet();
        var total = counts.GroupBy(c => (c.TemplateId, c.Cycle)).ToDictionary(g => g.Key, g => g.Count());
        var keys = monthDays.Append(weekKey).Append(monthKey).ToHashSet();
        var recs = await _db.EqCheckRecords.AsNoTracking()
            .Where(r => r.Cycle != EqCycles.Fault && keys.Contains(r.PeriodKey)).ToListAsync();
        var recIds = recs.Select(r => r.Id).ToList();
        var res = await _db.EqCheckResults.AsNoTracking().Where(r => recIds.Contains(r.RecordId) && r.ItemId > 0)
            .Select(r => new { r.RecordId, r.ItemId, r.Judge, r.CheckedByName, r.CheckedAt }).ToListAsync();
        var byRec = res.GroupBy(r => r.RecordId).ToDictionary(g => g.Key, g => g.ToList());
        var openNg = (await _db.EqCheckResults.AsNoTracking().Where(r => r.NgStatus == "OPEN").Select(r => r.UnitCode).ToListAsync())
            .GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
        var eq = await EquipInfoAsync();

        EqCheckCellDto Cell(EqCheckUnit u, string cycle, string key)
        {
            var t = total.GetValueOrDefault((u.TemplateId, cycle));
            var rec = recs.FirstOrDefault(r => r.UnitCode == u.Code && r.Cycle == cycle && r.PeriodKey == key);
            var rs = rec is null ? new() : byRec.GetValueOrDefault(rec.Id) ?? new();
            var done = rs.Count(r => r.Judge != "" && activeIds.Contains(r.ItemId));
            var by = rs.OrderByDescending(r => r.CheckedAt).Select(r => r.CheckedByName).FirstOrDefault() ?? "";
            return new EqCheckCellDto(t == 0 ? "none" : State(cycle, key, today, done, t), done, t, rs.Count(r => r.Judge == "NG"), by);
        }

        var order = await _db.ScheduleEquipments.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.OrderIndex)
            .Select(e => e.Name).ToListAsync();
        int Rank(string code) { var i = order.FindIndex(n => EquipmentCatalog.UnitCode(n) == code); return i < 0 ? int.MaxValue : i; }

        var rows = units.OrderBy(u => Rank(u.Code)).ThenBy(u => u.Code, StringComparer.Ordinal).Select(u =>
        {
            var info = UnitDto(u, tpl, eq);
            var dailyTotal = total.GetValueOrDefault((u.TemplateId, EqCycles.Daily));
            var doneDays = dailyTotal == 0 ? 0 : recs.Count(r => r.UnitCode == u.Code && r.Cycle == EqCycles.Daily && monthDays.Contains(r.PeriodKey)
                && (byRec.GetValueOrDefault(r.Id)?.Count(x => x.Judge != "" && activeIds.Contains(x.ItemId)) ?? 0) >= dailyTotal);
            return new EqCheckStatusRowDto(u.Code, info.Line, info.Process, info.TemplateName,
                Cell(u, EqCycles.Daily, dayKey), Cell(u, EqCycles.Weekly, weekKey), Cell(u, EqCycles.Monthly, monthKey),
                doneDays, openNg.GetValueOrDefault(u.Code));
        }).ToList();
        return new EqCheckStatusDto(d, weekKey, Period(EqCycles.Weekly, weekKey).Due, monthKey, Period(EqCycles.Monthly, monthKey).Due,
            monthDays.Count, rows);
    }

    // ───────── NG·고장 ─────────

    private static EqCheckNgDto NgDto(EqCheckResult r, IReadOnlyDictionary<string, (string Line, string Process)> eq) => new(
        r.Id, r.UnitCode, eq.TryGetValue(r.UnitCode, out var i) ? i.Line : EquipmentCatalog.GuessLine(r.UnitCode),
        r.Cycle, r.PeriodKey, r.Category, r.Name, r.Point, r.Spec,
        r.ItemId == 0 ? "" : ValueText(r.InputType, r.Value, r.Nums, r.NotRunning, "", ""),
        r.Memo, r.CheckedByName, r.CheckedAt, r.NgStatus, r.NgCloseNote, r.NgClosedBy, r.NgClosedAt);

    public async Task<IReadOnlyList<EqCheckNgDto>> GetNgsAsync(bool openOnly, string? line, string? unit, DateOnly? from, DateOnly? to)
    {
        var q = _db.EqCheckResults.AsNoTracking().Where(r => r.Judge == "NG");
        if (openOnly) q = q.Where(r => r.NgStatus == "OPEN");
        if (!string.IsNullOrWhiteSpace(unit)) q = q.Where(r => r.UnitCode == unit);
        if (from is { } f) { var fd = f.ToDateTime(TimeOnly.MinValue); q = q.Where(r => r.CheckedAt >= fd); }
        if (to is { } t) { var td = t.AddDays(1).ToDateTime(TimeOnly.MinValue); q = q.Where(r => r.CheckedAt < td); }
        var rows = await q.OrderByDescending(r => r.CheckedAt).Take(1000).ToListAsync();
        var eq = await EquipInfoAsync();
        var list = rows.Select(r => NgDto(r, eq));
        if (!string.IsNullOrWhiteSpace(line)) list = list.Where(x => x.Line == line);
        return list.ToList();
    }

    /// <summary>NG 조치 완료 — 편집 등급 또는 설비팀. 조치 내용은 꼭 적는다.</summary>
    public async Task<EqCheckNgDto> CloseNgAsync(int resultId, string? note, EqCheckActor actor)
    {
        if (!actor.CanEdit && !IsMonthlyTeam(actor, await MonthlyTeamsAsync()))
            throw new BusinessRuleException("NG 조치 완료는 편집 권한이 있는 사람이나 설비팀이 합니다.");
        var r = await _db.EqCheckResults.FirstOrDefaultAsync(x => x.Id == resultId) ?? throw new BusinessRuleException("기록을 찾을 수 없습니다.");
        if (r.Judge != "NG") throw new BusinessRuleException("NG 기록이 아닙니다.");
        var text = (note ?? "").Trim();
        if (text.Length == 0) throw new BusinessRuleException("조치 내용 및 결과를 적으세요.");
        if (text.Length > 500) throw new BusinessRuleException("조치 내용은 500자까지입니다.");
        r.NgStatus = "DONE"; r.NgCloseNote = text; r.NgClosedAt = Now; r.NgClosedBy = actor.RealName;
        await _db.SaveChangesAsync();
        return NgDto(r, await EquipInfoAsync());
    }

    // ───────── 월간 점검표(종이 양식 모양) ─────────

    public async Task<EqCheckMonthDto?> GetMonthAsync(string code, int year, int month)
    {
        if (month is < 1 or > 12 || year is < 2020 or > 2100) throw new BusinessRuleException("연·월이 올바르지 않습니다.");
        var unit = await _db.EqCheckUnits.AsNoTracking().FirstOrDefaultAsync(u => u.Code == code);
        if (unit is null) return null;
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var monthKey = start.ToString("yyyy-MM");
        var weekKeys = new List<string>();
        for (var f = FirstFriday(year, month); f <= end; f = f.AddDays(7)) weekKeys.Add(f.ToString("yyyy-MM-dd"));
        var dayPrefix = monthKey + "-";

        var recs = await _db.EqCheckRecords.AsNoTracking().Where(r => r.UnitCode == code
            && ((r.Cycle == EqCycles.Daily || r.Cycle == EqCycles.Fault) && r.PeriodKey.StartsWith(dayPrefix)
                || r.Cycle == EqCycles.Weekly && weekKeys.Contains(r.PeriodKey)
                || r.Cycle == EqCycles.Monthly && r.PeriodKey == monthKey)).ToListAsync();
        var recIds = recs.Select(r => r.Id).ToList();
        var results = await _db.EqCheckResults.AsNoTracking().Where(r => recIds.Contains(r.RecordId)).ToListAsync();
        var used = results.Select(r => r.ItemId).ToHashSet();
        var items = await _db.EqCheckItems.AsNoTracking().Where(i => i.TemplateId == unit.TemplateId && (i.IsActive || used.Contains(i.Id)))
            .ToListAsync();
        var cycleOrder = EqCycles.All.ToList();
        items = items.OrderBy(i => cycleOrder.IndexOf(i.Cycle)).ThenBy(i => i.SortOrder).ThenBy(i => i.Id).ToList();
        var byId = items.ToDictionary(i => i.Id);
        var cells = results.Where(r => r.ItemId > 0).Select(r =>
        {
            var it = byId.GetValueOrDefault(r.ItemId);
            return new EqCheckMonthCellDto(r.ItemId, r.Cycle, r.PeriodKey,
                ValueText(r.InputType, r.Value, r.Nums, r.NotRunning, it?.Unit ?? "", it?.Fields ?? ""), r.Judge, r.CheckedByName);
        }).ToList();
        var names = await NamesAsync(recs.Select(r => r.UpdatedBy));
        var notes = recs.Where(r => r.Note.Length > 0)
            .Select(r => new EqCheckMonthNoteDto(r.Cycle, r.PeriodKey, r.Note, names.GetValueOrDefault(r.UpdatedBy, r.UpdatedBy))).ToList();
        var eq = await EquipInfoAsync();
        var faults = results.Where(r => r.Judge == "NG").OrderBy(r => r.CheckedAt).Select(r => NgDto(r, eq)).ToList();
        var tpl = await _db.EqCheckTemplates.AsNoTracking().ToDictionaryAsync(t => t.Id);
        return new EqCheckMonthDto(UnitDto(unit, tpl, eq), year, month, items.Select(ToDto).ToList(), weekKeys, monthKey,
            FirstFriday(year, month), cells, notes, faults);
    }

    private async Task<Dictionary<string, string>> NamesAsync(IEnumerable<string> usernames)
    {
        var set = usernames.Where(u => u.Length > 0).Distinct().ToList();
        return await _db.Users.AsNoTracking().Where(u => set.Contains(u.Username))
            .ToDictionaryAsync(u => u.Username, u => u.RealName);
    }

    // ───────── 양식 관리(관리자) ─────────

    public async Task<IReadOnlyList<EqCheckTemplateDto>> GetTemplatesAsync()
    {
        var tpls = await _db.EqCheckTemplates.AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();
        var items = await _db.EqCheckItems.AsNoTracking().ToListAsync();
        var units = await _db.EqCheckUnits.AsNoTracking().Where(u => u.IsActive).ToListAsync();
        var cycleOrder = EqCycles.All.ToList();
        return tpls.Select(t => new EqCheckTemplateDto(t.Id, t.Code, t.Name, t.Note, t.SortOrder, t.IsActive,
            items.Where(i => i.TemplateId == t.Id).OrderBy(i => cycleOrder.IndexOf(i.Cycle)).ThenBy(i => i.SortOrder).ThenBy(i => i.Id).Select(ToDto).ToList(),
            units.Where(u => u.TemplateId == t.Id).Select(u => u.Code).OrderBy(c => c, StringComparer.Ordinal).ToList())).ToList();
    }

    public async Task<EqCheckTemplateDto> SaveTemplateAsync(EqCheckTemplateSaveRequest req, string actor)
    {
        var code = (req.Code ?? "").Trim().ToUpperInvariant();
        var name = (req.Name ?? "").Trim();
        if (code.Length is 0 or > 20) throw new BusinessRuleException("양식 코드는 1~20자입니다.");
        if (name.Length is 0 or > 60) throw new BusinessRuleException("양식 이름은 1~60자입니다.");
        if (await _db.EqCheckTemplates.AnyAsync(t => t.Code == code && t.Id != req.Id)) throw new BusinessRuleException($"'{code}' 양식이 이미 있습니다.");
        var t = req.Id > 0 ? await _db.EqCheckTemplates.FindAsync(req.Id) ?? throw new BusinessRuleException("양식을 찾을 수 없습니다.") : null;
        if (t is null)
        {
            t = new EqCheckTemplate { SortOrder = (await _db.EqCheckTemplates.MaxAsync(x => (int?)x.SortOrder) ?? 0) + 1 };
            _db.EqCheckTemplates.Add(t);
        }
        t.Code = code; t.Name = name; t.Note = (req.Note ?? "").Trim(); t.IsActive = req.IsActive;
        t.UpdatedAt = Now; t.UpdatedBy = actor;
        await _db.SaveChangesAsync();
        return (await GetTemplatesAsync()).Single(x => x.Id == t.Id);
    }

    public async Task<EqCheckItemDto> SaveItemAsync(EqCheckItemDto dto, string actor)
    {
        if (!await _db.EqCheckTemplates.AnyAsync(t => t.Id == dto.TemplateId)) throw new BusinessRuleException("양식을 찾을 수 없습니다.");
        if (!EqCycles.All.Contains(dto.Cycle)) throw new BusinessRuleException("주기는 일상 / 주간 / 월간 중 하나입니다.");
        if (!EqInputTypes.All.Contains(dto.InputType)) throw new BusinessRuleException("입력 방식이 올바르지 않습니다.");
        var name = (dto.Name ?? "").Trim();
        if (name.Length is 0 or > 80) throw new BusinessRuleException("항목 이름은 1~80자입니다.");
        var options = string.Join("|", ParseOptions(dto.Options ?? "").Select(o => (o.Action ? "*" : "") + o.Text));
        var fields = string.Join("|", ParseFields(dto.Fields ?? ""));
        if (dto.InputType == EqInputTypes.Choice && ParseOptions(options).Count < 2) throw new BusinessRuleException("보기를 두 개 이상 적으세요(| 로 나눔, 첫 보기가 정상).");
        if (dto.InputType == EqInputTypes.Multi && ParseFields(fields).Count < 2) throw new BusinessRuleException("칸 이름을 두 개 이상 적으세요(| 로 나눔).");
        if (dto.Min is { } mn && dto.Max is { } mx && dto.InputType == EqInputTypes.Num && mn > mx) throw new BusinessRuleException("하한이 상한보다 큽니다.");
        var i = dto.Id > 0 ? await _db.EqCheckItems.FindAsync(dto.Id) ?? throw new BusinessRuleException("항목을 찾을 수 없습니다.") : null;
        if (i is null)
        {
            i = new EqCheckItem
            {
                TemplateId = dto.TemplateId,
                SortOrder = (await _db.EqCheckItems.Where(x => x.TemplateId == dto.TemplateId && x.Cycle == dto.Cycle).MaxAsync(x => (int?)x.SortOrder) ?? 0) + 1,
            };
            _db.EqCheckItems.Add(i);
        }
        else if (dto.SortOrder > 0) i.SortOrder = dto.SortOrder;
        i.Cycle = dto.Cycle; i.Category = (dto.Category ?? "").Trim(); i.Name = name; i.Point = (dto.Point ?? "").Trim();
        i.Spec = (dto.Spec ?? "").Trim(); i.InputType = dto.InputType; i.Options = dto.InputType == EqInputTypes.Choice ? options : "";
        i.Fields = dto.InputType == EqInputTypes.Multi ? fields : ""; i.Unit = (dto.Unit ?? "").Trim();
        i.Min = dto.InputType == EqInputTypes.Num ? dto.Min : null; i.Max = dto.InputType is EqInputTypes.Num or EqInputTypes.Multi ? dto.Max : null;
        i.RunOnly = dto.RunOnly && dto.InputType is EqInputTypes.Num or EqInputTypes.Multi; i.IsActive = dto.IsActive;
        if (i.Category.Length > 40 || i.Point.Length > 60 || i.Spec.Length > 200 || i.Unit.Length > 20 || i.Options.Length > 200 || i.Fields.Length > 100)
            throw new BusinessRuleException("글자 수가 너무 깁니다(대분류 40·위치 60·기준 200·단위 20·보기 200·칸 100자).");
        var t = await _db.EqCheckTemplates.FindAsync(dto.TemplateId);
        if (t is not null) { t.UpdatedAt = Now; t.UpdatedBy = actor; }
        await _db.SaveChangesAsync();
        return ToDto(i);
    }

    /// <summary>항목 순서 바꾸기 — 같은 양식·주기 안에서 보낸 순서대로.</summary>
    public async Task ReorderItemsAsync(IReadOnlyList<int> ids)
    {
        var items = await _db.EqCheckItems.Where(i => ids.Contains(i.Id)).ToListAsync();
        for (var n = 0; n < ids.Count; n++)
            if (items.FirstOrDefault(i => i.Id == ids[n]) is { } it) it.SortOrder = n + 1;
        await _db.SaveChangesAsync();
    }

    /// <summary>항목 지우기 — 기록이 있으면 끄기만 한다(지난 점검표에 남아야 한다).</summary>
    public async Task<bool> DeleteItemAsync(int id)
    {
        var i = await _db.EqCheckItems.FindAsync(id);
        if (i is null) return false;
        if (await _db.EqCheckResults.AnyAsync(r => r.ItemId == id)) i.IsActive = false;
        else _db.EqCheckItems.Remove(i);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<IReadOnlyList<EqCheckUnitDto>> GetUnitsAsync()
    {
        var tpl = await _db.EqCheckTemplates.AsNoTracking().ToDictionaryAsync(t => t.Id);
        var eq = await EquipInfoAsync();
        return (await _db.EqCheckUnits.AsNoTracking().OrderBy(u => u.Code).ToListAsync()).Select(u => UnitDto(u, tpl, eq)).ToList();
    }

    public async Task<EqCheckUnitDto> SaveUnitAsync(EqCheckUnitSaveRequest req)
    {
        var code = EquipmentCatalog.UnitCode(EquipmentCatalog.NormalizeCode(req.Code));
        if (code.Length is 0 or > 30) throw new BusinessRuleException("호기 코드를 입력하세요.");
        if (!await _db.EqCheckTemplates.AnyAsync(t => t.Id == req.TemplateId)) throw new BusinessRuleException("양식을 고르세요.");
        var u = await _db.EqCheckUnits.FirstOrDefaultAsync(x => x.Code == code);
        if (u is null) { u = new EqCheckUnit { Code = code }; _db.EqCheckUnits.Add(u); }
        u.TemplateId = req.TemplateId; u.IsActive = req.IsActive; u.Note = (req.Note ?? "").Trim();
        await _db.SaveChangesAsync();
        var tpl = await _db.EqCheckTemplates.AsNoTracking().ToDictionaryAsync(t => t.Id);
        return UnitDto(u, tpl, await EquipInfoAsync());
    }

    public async Task<string> SaveMonthlyTeamsAsync(string? teams)
    {
        var v = string.Join(", ", (teams ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (v.Length > 400) throw new BusinessRuleException("너무 깁니다.");
        var row = await _db.CheckSettings.FirstOrDefaultAsync(s => s.Key == MonthlyTeamsKey);
        if (row is null) _db.CheckSettings.Add(new CheckSetting { Key = MonthlyTeamsKey, Value = v });
        else row.Value = v;
        await _db.SaveChangesAsync();
        return v;
    }
}
