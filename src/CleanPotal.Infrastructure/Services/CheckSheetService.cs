using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// QR 체크시트.
///
/// 한 구역(방)의 점검 화면에는 그 구역 항목과, 같은 라인 공통 구역(예: M-ALL)의 항목이 함께 뜬다.
/// 어떤 항목이 오늘 이 교대에 해당하는지는 "점검 시점" 으로 정한다:
///   주·야 각 1회 → 두 교대 모두 / 주간조만·야간조만 → 그 교대만 /
///   주 1회 → 그 주(월~일) 에 한 번. 지정 요일부터 해당, 지난 요일이면 "밀림" / 이벤트 발생 시 → 필요할 때만.
/// 근무일은 교대가 시작된 날이다 — 야간이 자정을 넘겨도 시작일로 기록한다.
///
/// 결과는 항목마다 바로 저장한다(휴대폰이 끊겨도 한 일은 남게). "제출" 은 빠진 필수 항목·사진이 없는지
/// 확인하고 마감한다. 마감 뒤에는 관리자만 사유를 적고 고칠 수 있고, 바뀐 내용은 자료 변경 이력에 남는다.
/// </summary>
public class CheckSheetService : ICheckSheetService
{
    public const string AuditType = "체크시트";
    public const string ShiftDay = "주간";
    public const string ShiftNight = "야간";

    private const string GroupCommon = "common";
    private const string GroupZone = "zone";
    private const string GroupWeekly = "weekly";
    private const string GroupEvent = "event";

    private static readonly string[] WeekdayNames = { "", "월", "화", "수", "목", "금", "토", "일" };
    private static readonly Regex CodePattern = new(@"^[A-Z0-9][A-Z0-9-]{0,19}$", RegexOptions.Compiled);
    private static readonly Regex AttRef = new(@"^att:\d+\|", RegexOptions.Compiled);
    private static readonly HashSet<string> PhotoKinds = new() { "before", "after", "ng", "photo" };
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>설정 기본값. 관리 화면에서 바꾸면 CheckSettings 표에 남는다.</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultSettings = new Dictionary<string, string>
    {
        ["DayStart"] = "07:00",
        ["NightStart"] = "17:30",
        ["QrBaseUrl"] = "",
        ["FormName"] = "3정 5S 점검 Sheet",
        ["Revision"] = "Rev.2",
        ["EffectiveDate"] = "",
    };

    private readonly CleanPotalDbContext _db;
    private readonly TimeProvider _clock;

    public CheckSheetService(CleanPotalDbContext db) : this(db, TimeProvider.System) { }
    public CheckSheetService(CleanPotalDbContext db, TimeProvider clock) { _db = db; _clock = clock; }

    private DateTime Now => _clock.GetLocalNow().DateTime;

    // ───────────────────────── 교대 ─────────────────────────

    /// <summary>지금이 어느 근무일·교대인지. 주간 시작 전 새벽은 전날 야간이다.</summary>
    public static (DateOnly Date, string Shift) ShiftAt(DateTime now, TimeOnly dayStart, TimeOnly nightStart)
    {
        var t = TimeOnly.FromDateTime(now);
        var d = DateOnly.FromDateTime(now);
        if (t < dayStart) return (d.AddDays(-1), ShiftNight);
        return t < nightStart ? (d, ShiftDay) : (d, ShiftNight);
    }

    private static (DateOnly, string) PreviousShift(DateOnly d, string shift)
        => shift == ShiftDay ? (d.AddDays(-1), ShiftNight) : (d, ShiftDay);

    private static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
    private static int IsoWeekday(DateOnly d) => ((int)d.DayOfWeek + 6) % 7 + 1;   // 1=월 … 7=일

    private async Task<Dictionary<string, string>> SettingsAsync()
    {
        var map = new Dictionary<string, string>(DefaultSettings);
        foreach (var s in await _db.CheckSettings.AsNoTracking().ToListAsync()) map[s.Key] = s.Value;
        return map;
    }

    private static TimeOnly ParseTime(string? s, TimeOnly fallback)
        => TimeOnly.TryParseExact((s ?? "").Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : fallback;

    private async Task<(DateOnly Date, string Shift)> CurrentShiftAsync()
    {
        var s = await SettingsAsync();
        return ShiftAt(Now, ParseTime(s["DayStart"], new TimeOnly(7, 0)), ParseTime(s["NightStart"], new TimeOnly(17, 30)));
    }

    private static string NormalizeShift(string? shift)
    {
        var s = (shift ?? "").Trim();
        if (s is ShiftDay or ShiftNight) return s;
        throw new BusinessRuleException("교대는 주간 또는 야간이어야 합니다.");
    }

    // ───────────────────────── 항목 해당 여부 ─────────────────────────

    private static bool ValidOn(CheckItem i, DateOnly d)
        => i.IsActive && (i.ValidFrom is null || i.ValidFrom <= d) && (i.ValidTo is null || i.ValidTo >= d);

    public static string SpecText(CheckItem i)
    {
        if (i.ResultType != CheckResultTypes.Num) return "";
        string F(decimal? v) => v?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";
        return i.JudgeMode switch
        {
            CheckJudgeModes.Abs when i.MaxValue is not null => $"±{F(i.MaxValue)}{i.Unit} 이내",
            CheckJudgeModes.Range when i.MinValue is not null && i.MaxValue is not null => $"{F(i.MinValue)}~{F(i.MaxValue)}{i.Unit}",
            CheckJudgeModes.Range when i.MinValue is not null => $"{F(i.MinValue)}{i.Unit} 이상",
            CheckJudgeModes.Range when i.MaxValue is not null => $"{F(i.MaxValue)}{i.Unit} 이하",
            _ => i.Unit.Length > 0 ? $"기록({i.Unit})" : "기록",
        };
    }

    public static bool Judge(CheckItem i, decimal v) => i.JudgeMode switch
    {
        CheckJudgeModes.Abs => i.MaxValue is null || Math.Abs(v) <= i.MaxValue,
        CheckJudgeModes.Range => (i.MinValue is null || v >= i.MinValue) && (i.MaxValue is null || v <= i.MaxValue),
        _ => true,
    };

    /// <summary>한 구역·근무일·교대에 뜨는 항목과 그 상태. 저장·제출·현황·리포트가 모두 이 판정을 쓴다.</summary>
    private sealed record Line(CheckItem Item, string Group, string DueState, bool RequiredNow, CheckResult? DoneElsewhere);

    private sealed class Context
    {
        public required List<CheckZone> Zones { get; init; }
        public required List<CheckItem> Items { get; init; }
    }

    private async Task<Context> LoadContextAsync() => new()
    {
        Zones = await _db.CheckZones.AsNoTracking().ToListAsync(),
        Items = await _db.CheckItems.AsNoTracking().ToListAsync(),
    };

    /// <summary>이 구역 화면에 뜰 수 있는 항목(자기 구역 + 같은 라인 공통 구역).</summary>
    private static List<(CheckItem Item, bool Common)> ItemsOf(Context ctx, CheckZone zone)
    {
        var common = ctx.Zones.Where(z => z.IsCommon && z.IsActive && z.Line == zone.Line)
            .Select(z => z.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ctx.Items
            .Where(i => string.Equals(i.ZoneCode, zone.Code, StringComparison.OrdinalIgnoreCase) || common.Contains(i.ZoneCode))
            .Select(i => (i, common.Contains(i.ZoneCode)))
            .ToList();
    }

    /// <param name="weekResults">이 구역의 그 주 결과(교대별 실적 포함). 주 1회 항목이 이미 끝났는지 본다.</param>
    private static List<Line> Lines(Context ctx, CheckZone zone, DateOnly date, string shift,
        IReadOnlyList<(CheckRun Run, CheckResult Result)> weekResults, int? currentRunId)
    {
        var list = new List<Line>();
        foreach (var (item, common) in ItemsOf(ctx, zone))
        {
            if (!ValidOn(item, date)) continue;
            switch (item.Timing)
            {
                case CheckTimings.DayOnly when shift != ShiftDay:
                case CheckTimings.NightOnly when shift != ShiftNight:
                    continue;
                case CheckTimings.Weekly:
                {
                    var done = weekResults
                        .Where(w => w.Result.ItemId == item.Id && w.Run.Id != currentRunId && w.Result.Result.Length > 0)
                        .OrderBy(w => w.Result.CheckedAt).Select(w => w.Result).FirstOrDefault();
                    string due;
                    if (done is not null) due = "완료";
                    else if (item.Weekday is null) due = "이번 주";
                    else
                    {
                        var today = IsoWeekday(date);
                        due = today == item.Weekday ? "오늘" : today > item.Weekday ? "밀림" : "예정";
                    }
                    var required = item.Required && done is null && due is "오늘" or "밀림";
                    list.Add(new Line(item, GroupWeekly, due, required, done));
                    continue;
                }
                case CheckTimings.Event:
                    list.Add(new Line(item, GroupEvent, "", false, null));
                    continue;
            }
            list.Add(new Line(item, common ? GroupCommon : GroupZone, "", item.Required, null));
        }
        int GroupOrder(string g) => g switch { GroupCommon => 0, GroupZone => 1, GroupWeekly => 2, _ => 3 };
        return list.OrderBy(l => GroupOrder(l.Group)).ThenBy(l => l.Item.SortOrder).ThenBy(l => l.Item.Code).ToList();
    }

    private async Task<List<(CheckRun Run, CheckResult Result)>> WeekResultsAsync(string zoneCode, DateOnly date)
    {
        var weekFrom = WeekStart(date);
        var weekTo = weekFrom.AddDays(6);
        var rows = await (from r in _db.CheckRuns.AsNoTracking()
                          join x in _db.CheckResults.AsNoTracking() on r.Id equals x.RunId
                          where r.ZoneCode == zoneCode && r.WorkDate >= weekFrom && r.WorkDate <= weekTo
                          select new { r, x }).ToListAsync();
        return rows.Select(v => (v.r, v.x)).ToList();
    }

    private async Task<CheckZone?> FindZoneAsync(string code)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        return await _db.CheckZones.AsNoTracking().FirstOrDefaultAsync(z => z.Code == c && z.IsActive && !z.IsCommon);
    }

    // ───────────────────────── 점검 화면 ─────────────────────────

    public async Task<CheckSheetDto?> GetSheetAsync(string zoneCode, DateOnly? date, string? shift, CheckActor actor)
    {
        var zone = await FindZoneAsync(zoneCode);
        if (zone is null) return null;
        var current = await CurrentShiftAsync();
        var d = date ?? current.Date;
        var s = shift is null ? (date is null ? current.Shift : ShiftDay) : NormalizeShift(shift);
        return await BuildSheetAsync(zone, d, s, actor, current);
    }

    private async Task<CheckSheetDto> BuildSheetAsync(CheckZone zone, DateOnly d, string s, CheckActor actor, (DateOnly Date, string Shift) current)
    {
        var ctx = await LoadContextAsync();
        var run = await _db.CheckRuns.AsNoTracking().FirstOrDefaultAsync(r => r.ZoneCode == zone.Code && r.WorkDate == d && r.Shift == s);
        var results = run is null
            ? new Dictionary<int, CheckResult>()
            : await _db.CheckResults.AsNoTracking().Where(x => x.RunId == run.Id).ToDictionaryAsync(x => x.ItemId);
        var week = await WeekResultsAsync(zone.Code, d);
        var lines = Lines(ctx, zone, d, s, week, run?.Id);

        var items = lines.Select(l =>
        {
            results.TryGetValue(l.Item.Id, out var res);
            var elsewhere = l.DoneElsewhere is { } e
                ? DescribeRun(week.First(w => w.Result.Id == e.Id).Run, e.CheckedByName)
                : "";
            return new CheckSheetItemDto(
                l.Item.Id, l.Item.Code, l.Group, l.Item.Text, l.Item.Detail, l.Item.Timing,
                l.Item.Weekday is int w ? WeekdayNames[w] : "", l.DueState, l.Item.ResultType, l.Item.Unit,
                l.Item.MinValue, l.Item.MaxValue, l.Item.JudgeMode, SpecText(l.Item), l.Item.PhotoPolicy,
                l.RequiredNow, l.Item.AllowNa, l.Item.PaperForm, res is null ? null : ToDto(res), elsewhere);
        }).ToList();

        var submitted = run?.SubmittedAt is not null;
        var mayTouch = MayEditShift(actor, d, s, current);
        var canEdit = actor.CanEdit && ((mayTouch && !submitted) || actor.IsAdmin);
        return new CheckSheetDto(zone.Code, zone.Name, zone.Line, d, s, (d, s) == current,
            run?.Id, run?.SubmittedAt, run?.SubmittedByName ?? "", canEdit, submitted && actor.IsAdmin, items);
    }

    private static string DescribeRun(CheckRun run, string who)
        => $"{run.WorkDate:M/d}({"월화수목금토일"[IsoWeekday(run.WorkDate) - 1]}) {run.Shift} {who}".Trim();

    /// <summary>관리자가 아니면 지금 교대와 바로 앞 교대만 입력할 수 있다(끝나 가는 야간을 아침에 마저 올리는 경우).</summary>
    private static bool MayEditShift(CheckActor actor, DateOnly d, string s, (DateOnly Date, string Shift) current)
        => actor.IsAdmin || (d, s) == current || (d, s) == PreviousShift(current.Date, current.Shift);

    public static CheckResultDto ToDto(CheckResult r)
        => new(r.Id, r.Result, r.NumValue, r.Memo, ParsePhotos(r.Photos), r.CheckedAt, r.CheckedByName, r.NgStatus);

    public static IReadOnlyList<CheckPhotoDto> ParsePhotos(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<CheckPhotoDto>();
        try { return JsonSerializer.Deserialize<List<CheckPhotoDto>>(json, Json) ?? new List<CheckPhotoDto>(); }
        catch (JsonException) { return Array.Empty<CheckPhotoDto>(); }
    }

    // ───────────────────────── 결과 저장 ─────────────────────────

    public async Task<CheckResultDto?> SaveResultAsync(string zoneCode, int itemId, CheckResultSaveRequest req, CheckActor actor)
    {
        if (!actor.CanEdit) throw new ForbiddenException("점검 결과를 입력할 권한이 없습니다(현장 점검 편집 등급 필요).");
        var zone = await FindZoneAsync(zoneCode) ?? throw new BusinessRuleException("없는 구역입니다. QR 을 다시 확인하세요.");
        var shift = NormalizeShift(req.Shift);
        var current = await CurrentShiftAsync();

        var run = await _db.CheckRuns.FirstOrDefaultAsync(r => r.ZoneCode == zone.Code && r.WorkDate == req.Date && r.Shift == shift);
        var submitted = run?.SubmittedAt is not null;
        if (submitted && !actor.IsAdmin)
            throw new ForbiddenException("이미 제출한 점검입니다. 고쳐야 하면 관리자에게 요청하세요.");
        if (!submitted && !MayEditShift(actor, req.Date, shift, current))
            throw new ForbiddenException("지금 교대와 바로 앞 교대만 입력할 수 있습니다.");
        var reason = (req.Reason ?? "").Trim();
        if (submitted && reason.Length == 0)
            throw new BusinessRuleException("제출된 점검을 고치려면 수정 사유를 적어야 합니다.");

        var ctx = await LoadContextAsync();
        var week = await WeekResultsAsync(zone.Code, req.Date);
        var line = Lines(ctx, zone, req.Date, shift, week, run?.Id).FirstOrDefault(l => l.Item.Id == itemId)
            ?? throw new BusinessRuleException("이 구역·교대에 해당하지 않는 항목입니다.");
        if (line.DoneElsewhere is not null)
            throw new BusinessRuleException("이번 주에 이미 점검한 항목입니다.");
        var item = line.Item;

        // ── 입력 정리 ──
        var result = (req.Result ?? "").Trim().ToUpperInvariant();
        if (result is not ("" or "OK" or "NG" or "NA"))
            throw new BusinessRuleException("결과는 OK / NG / NA 중 하나여야 합니다.");
        if (result == "NA" && !item.AllowNa)
            throw new BusinessRuleException("이 항목은 N/A(해당 없음)를 고를 수 없습니다.");
        decimal? num = null;
        if (item.ResultType == CheckResultTypes.Num && result != "NA")
        {
            num = req.NumValue;
            // 수치 항목은 값으로 판정한다 — 화면이 보낸 OK/NG 보다 기준이 우선이다.
            result = num is null ? "" : (Judge(item, num.Value) ? "OK" : "NG");
        }
        var memo = (req.Memo ?? "").Trim();
        if (memo.Length > 500) memo = memo[..500];
        var photos = (req.Photos ?? Array.Empty<CheckPhotoDto>())
            .Where(p => p is not null && PhotoKinds.Contains(p.K) && AttRef.IsMatch(p.V ?? ""))
            .Take(20).ToList();

        var row = run is null ? null : await _db.CheckResults.FirstOrDefaultAsync(x => x.RunId == run.Id && x.ItemId == itemId);
        var empty = result.Length == 0 && num is null && memo.Length == 0 && photos.Count == 0;
        var before = row is null ? "(없음)" : Summary(row.Result, row.NumValue, row.Memo, ParsePhotos(row.Photos).Count);

        if (empty)
        {
            if (row is not null)
            {
                _db.CheckResults.Remove(row);
                if (submitted) ContentAuditAdd(row.Id, "수정", $"{zone.Code} {req.Date:yyyy-MM-dd} {shift} {item.Code}: {before} → (지움) / 사유: {reason}", actor);
                await _db.SaveChangesAsync();
            }
            return null;
        }

        if (run is null)
        {
            run = new CheckRun
            {
                ZoneCode = zone.Code, WorkDate = req.Date, Shift = shift,
                StartedAt = Now, StartedBy = actor.Username, ViaQr = req.ViaQr,
            };
            _db.CheckRuns.Add(run);
            await _db.SaveChangesAsync();
        }

        if (row is null)
        {
            row = new CheckResult { RunId = run.Id, ItemId = item.Id };
            _db.CheckResults.Add(row);
        }
        row.ItemCode = item.Code;
        row.ItemText = item.Text;
        row.ItemDetail = item.Detail;
        row.SpecText = SpecText(item);
        row.Result = result;
        row.NumValue = num;
        row.Memo = memo;
        row.Photos = JsonSerializer.Serialize(photos, Json);
        row.CheckedAt = Now;
        row.CheckedBy = actor.Username;
        row.CheckedByName = actor.Name;
        if (result == "NG")
        {
            if (row.NgStatus.Length == 0) row.NgStatus = "OPEN";
        }
        else
        {
            row.NgStatus = "";
            row.NgClosedAt = null;
            row.NgClosedBy = "";
            row.NgCloseNote = "";
        }
        await _db.SaveChangesAsync();

        if (submitted)
        {
            ContentAuditAdd(row.Id, "수정",
                $"{zone.Code} {req.Date:yyyy-MM-dd} {shift} {item.Code}: {before} → {Summary(row.Result, row.NumValue, row.Memo, photos.Count)} / 사유: {reason}", actor);
            await _db.SaveChangesAsync();
        }
        return ToDto(row);
    }

    private static string Summary(string result, decimal? num, string memo, int photos)
    {
        var parts = new List<string> { result.Length == 0 ? "미입력" : result };
        if (num is not null) parts.Add(num.Value.ToString("0.###", CultureInfo.InvariantCulture));
        if (memo.Length > 0) parts.Add($"메모 \"{(memo.Length > 40 ? memo[..40] + "…" : memo)}\"");
        if (photos > 0) parts.Add($"사진 {photos}장");
        return string.Join(" ", parts);
    }

    private void ContentAuditAdd(int entityId, string action, string detail, CheckActor actor)
    {
        _db.ContentAudits.Add(new ContentAudit
        {
            EntityType = AuditType, EntityId = entityId, Action = action,
            Detail = detail.Length > 500 ? detail[..500] + "…" : detail,
            ByUserName = actor.Name, CreatedAt = Now,
        });
    }

    // ───────────────────────── 제출 ─────────────────────────

    /// <summary>필수 항목이 다 찼고, NG 사유와 사진 정책에 맞는 사진이 있는지 본다. 빠진 것을 사람이 읽을 문장으로 돌려준다.</summary>
    public static List<string> Missing(IEnumerable<(CheckItem Item, bool Required, CheckResult? Result)> lines)
    {
        var problems = new List<string>();
        foreach (var (item, required, res) in lines)
        {
            var label = $"{item.Code} {item.Text}";
            if (res is null || res.Result.Length == 0)
            {
                if (required) problems.Add($"{label} — 결과 미입력");
                continue;
            }
            if (res.Result == "NA") continue;
            var kinds = ParsePhotos(res.Photos).Select(p => p.K).ToHashSet();
            if (res.Result == "NG" && res.Memo.Length == 0) problems.Add($"{label} — NG 사유 필요");
            switch (item.PhotoPolicy)
            {
                case CheckPhotoPolicies.OnNg when res.Result == "NG" && !kinds.Contains("ng"):
                    problems.Add($"{label} — NG 사진 필요"); break;
                case CheckPhotoPolicies.Before when !kinds.Contains("before"):
                    problems.Add($"{label} — 작업 전 사진 필요"); break;
                case CheckPhotoPolicies.After when !kinds.Contains("after"):
                    problems.Add($"{label} — 작업 후 사진 필요"); break;
                case CheckPhotoPolicies.BeforeAfter when !kinds.Contains("before") || !kinds.Contains("after"):
                    problems.Add($"{label} — 작업 전·후 사진 필요"); break;
                case CheckPhotoPolicies.Always when kinds.Count == 0:
                    problems.Add($"{label} — 사진 필요"); break;
            }
        }
        return problems;
    }

    public async Task<CheckSheetDto> SubmitAsync(string zoneCode, CheckSubmitRequest req, CheckActor actor)
    {
        if (!actor.CanEdit) throw new ForbiddenException("점검을 제출할 권한이 없습니다(현장 점검 편집 등급 필요).");
        var zone = await FindZoneAsync(zoneCode) ?? throw new BusinessRuleException("없는 구역입니다.");
        var shift = NormalizeShift(req.Shift);
        var current = await CurrentShiftAsync();
        if (!MayEditShift(actor, req.Date, shift, current))
            throw new ForbiddenException("지금 교대와 바로 앞 교대만 제출할 수 있습니다.");

        var run = await _db.CheckRuns.FirstOrDefaultAsync(r => r.ZoneCode == zone.Code && r.WorkDate == req.Date && r.Shift == shift)
            ?? throw new BusinessRuleException("입력한 항목이 없습니다. 항목을 점검한 뒤 제출하세요.");
        if (run.SubmittedAt is not null) throw new BusinessRuleException("이미 제출한 점검입니다.");

        var ctx = await LoadContextAsync();
        var week = await WeekResultsAsync(zone.Code, req.Date);
        var results = await _db.CheckResults.Where(x => x.RunId == run.Id).ToDictionaryAsync(x => x.ItemId);
        var lines = Lines(ctx, zone, req.Date, shift, week, run.Id);
        var problems = Missing(lines.Select(l => (l.Item, l.RequiredNow, results.GetValueOrDefault(l.Item.Id))));
        if (problems.Count > 0)
        {
            var head = string.Join("\n", problems.Take(8));
            throw new BusinessRuleException($"아직 제출할 수 없습니다({problems.Count}건).\n{head}{(problems.Count > 8 ? "\n…" : "")}");
        }

        run.SubmittedAt = Now;
        run.SubmittedBy = actor.Username;
        run.SubmittedByName = actor.Name;
        if (req.ViaQr) run.ViaQr = true;
        await _db.SaveChangesAsync();
        return await BuildSheetAsync(zone, req.Date, shift, actor, current);
    }

    // ───────────────────────── 현황 ─────────────────────────

    public async Task<CheckStatusDto> GetStatusAsync(DateOnly? date)
    {
        var current = await CurrentShiftAsync();
        var d = date ?? current.Date;
        var ctx = await LoadContextAsync();
        var from = WeekStart(d);
        var to = from.AddDays(6);
        var runs = await _db.CheckRuns.AsNoTracking().Where(r => r.WorkDate >= from && r.WorkDate <= to).ToListAsync();
        var runIds = runs.Select(r => r.Id).ToList();
        var results = await _db.CheckResults.AsNoTracking().Where(x => runIds.Contains(x.RunId)).ToListAsync();
        var byRun = results.GroupBy(x => x.RunId).ToDictionary(g => g.Key, g => g.ToList());

        var lines = new List<CheckLineStatusDto>();
        foreach (var lineGroup in ctx.Zones.Where(z => z.IsActive && !z.IsCommon)
                     .GroupBy(z => z.Line).OrderBy(g => LineOrder(g.Key)))
        {
            var zones = new List<CheckZoneStatusDto>();
            foreach (var zone in lineGroup.OrderBy(z => z.SortOrder).ThenBy(z => z.Code))
            {
                var week = runs.Where(r => r.ZoneCode == zone.Code)
                    .SelectMany(r => byRun.GetValueOrDefault(r.Id, new()).Select(x => (r, x))).ToList();
                CheckShiftStatusDto Of(string shift)
                {
                    var run = runs.FirstOrDefault(r => r.ZoneCode == zone.Code && r.WorkDate == d && r.Shift == shift);
                    var ls = Lines(ctx, zone, d, shift, week, run?.Id);
                    var res = run is null ? new List<CheckResult>() : byRun.GetValueOrDefault(run.Id, new());
                    var required = ls.Where(l => l.RequiredNow).Select(l => l.Item.Id).ToHashSet();
                    var done = res.Count(x => required.Contains(x.ItemId) && x.Result.Length > 0);
                    var ng = res.Count(x => x.Result == "NG");
                    var state = run is null ? "none" : run.SubmittedAt is null ? "progress" : "submitted";
                    if (required.Count == 0 && run is null) state = "na";
                    return new CheckShiftStatusDto(state, done, required.Count, ng, run?.SubmittedByName ?? "", run?.SubmittedAt);
                }
                var dayLines = Lines(ctx, zone, d, ShiftDay, week, null);
                zones.Add(new CheckZoneStatusDto(zone.Code, zone.Name, Of(ShiftDay), Of(ShiftNight),
                    dayLines.Count(l => l.Group == GroupWeekly && l.DueState == "오늘"),
                    dayLines.Count(l => l.Group == GroupWeekly && l.DueState == "밀림")));
            }
            lines.Add(new CheckLineStatusDto(lineGroup.Key, zones));
        }
        var openNg = await _db.CheckResults.CountAsync(x => x.NgStatus == "OPEN");
        return new CheckStatusDto(d, current.Shift, current.Date, lines, openNg);
    }

    private static int LineOrder(string line) => line switch { "METAL" => 0, "N-METAL" => 1, _ => 2 };

    // ───────────────────────── NG ─────────────────────────

    public async Task<IReadOnlyList<CheckNgDto>> GetNgsAsync(bool openOnly, string? line, DateOnly? from, DateOnly? to)
    {
        var q = from x in _db.CheckResults.AsNoTracking()
                join r in _db.CheckRuns.AsNoTracking() on x.RunId equals r.Id
                where x.NgStatus != ""
                select new { x, r };
        if (openOnly) q = q.Where(v => v.x.NgStatus == "OPEN");
        if (from is not null) q = q.Where(v => v.r.WorkDate >= from);
        if (to is not null) q = q.Where(v => v.r.WorkDate <= to);
        var rows = await q.OrderByDescending(v => v.r.WorkDate).ThenByDescending(v => v.x.CheckedAt).Take(500).ToListAsync();
        var zones = await _db.CheckZones.AsNoTracking().ToDictionaryAsync(z => z.Code, StringComparer.OrdinalIgnoreCase);
        var depts = await _db.CheckItems.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.NgDept);
        return rows
            .Where(v => line is null || (zones.TryGetValue(v.r.ZoneCode, out var z) && z.Line == line))
            .Select(v => ToNg(v.x, v.r, zones.GetValueOrDefault(v.r.ZoneCode), depts.GetValueOrDefault(v.x.ItemId, "")))
            .ToList();
    }

    private static CheckNgDto ToNg(CheckResult x, CheckRun r, CheckZone? z, string dept)
        => new(x.Id, r.ZoneCode, z?.Name ?? r.ZoneCode, z?.Line ?? "", r.WorkDate, r.Shift, x.ItemCode, x.ItemText,
            x.ItemDetail, x.SpecText, x.NumValue, x.Memo, ParsePhotos(x.Photos), x.CheckedByName, x.CheckedAt, dept,
            x.NgStatus, x.NgClosedAt, x.NgClosedBy, x.NgCloseNote);

    public async Task<CheckNgDto> CloseNgAsync(int resultId, string? note, CheckActor actor)
    {
        if (!actor.CanEdit) throw new ForbiddenException("NG 조치를 처리할 권한이 없습니다.");
        var x = await _db.CheckResults.FirstOrDefaultAsync(r => r.Id == resultId && r.NgStatus != "")
            ?? throw new BusinessRuleException("NG 기록을 찾을 수 없습니다.");
        var text = (note ?? "").Trim();
        if (text.Length == 0) throw new BusinessRuleException("어떻게 조치했는지 적어 주세요.");
        x.NgStatus = "DONE";
        x.NgClosedAt = Now;
        x.NgClosedBy = actor.Name;
        x.NgCloseNote = text.Length > 500 ? text[..500] : text;
        ContentAuditAdd(x.Id, "NG 조치 완료", $"{x.ItemCode} {x.ItemText}: {x.NgCloseNote}", actor);
        await _db.SaveChangesAsync();
        var r = await _db.CheckRuns.AsNoTracking().FirstAsync(v => v.Id == x.RunId);
        var z = await _db.CheckZones.AsNoTracking().FirstOrDefaultAsync(v => v.Code == r.ZoneCode);
        var dept = await _db.CheckItems.AsNoTracking().Where(i => i.Id == x.ItemId).Select(i => i.NgDept).FirstOrDefaultAsync() ?? "";
        return ToNg(x, r, z, dept);
    }

    // ───────────────────────── 월간 리포트 ─────────────────────────

    public async Task<CheckReportDto> GetReportAsync(string line, int year, int month)
    {
        if (month is < 1 or > 12 || year is < 2020 or > 2100) throw new BusinessRuleException("연·월이 올바르지 않습니다.");
        var settings = await SettingsAsync();
        var current = await CurrentShiftAsync();
        var ctx = await LoadContextAsync();
        var first = new DateOnly(year, month, 1);
        var days = DateTime.DaysInMonth(year, month);
        var last = first.AddDays(days - 1);
        // 주 1회 항목 판정 때문에 달 앞뒤 주까지 읽는다.
        var readFrom = WeekStart(first);
        var readTo = WeekStart(last).AddDays(6);

        var zones = ctx.Zones.Where(z => z.Line == line && !z.IsCommon && z.IsActive)
            .OrderBy(z => z.SortOrder).ThenBy(z => z.Code).ToList();
        var zoneCodes = zones.Select(z => z.Code).ToList();
        var runs = await _db.CheckRuns.AsNoTracking()
            .Where(r => zoneCodes.Contains(r.ZoneCode) && r.WorkDate >= readFrom && r.WorkDate <= readTo).ToListAsync();
        var runIds = runs.Select(r => r.Id).ToList();
        var results = await _db.CheckResults.AsNoTracking().Where(x => runIds.Contains(x.RunId)).ToListAsync();
        var runById = runs.ToDictionary(r => r.Id);

        // 시행일 전(또는 이 체크시트를 처음 쓴 날 전)은 "미" 로 보지 않는다.
        DateOnly? start = DateOnly.TryParse(settings["EffectiveDate"], CultureInfo.InvariantCulture, out var eff) ? eff : null;
        if (start is null && await _db.CheckRuns.AnyAsync())
            start = await _db.CheckRuns.MinAsync(r => r.WorkDate);

        bool Elapsed(DateOnly d, string shift)
        {
            if (start is null || d < start) return false;
            if (d < current.Date) return true;
            if (d > current.Date) return false;
            return shift == ShiftDay && current.Shift == ShiftNight;   // 오늘 주간은 야간이 시작돼야 지난 것
        }

        string Mark(CheckResult x) => x.Result switch
        {
            "OK" => x.NumValue is { } v ? v.ToString("0.###", CultureInfo.InvariantCulture) : "O",
            "NG" => x.NumValue is { } v ? "!" + v.ToString("0.###", CultureInfo.InvariantCulture) : "X",
            "NA" => "N/A",
            _ => "",
        };

        var rows = new List<CheckReportRowDto>();
        var summaries = new List<CheckReportZoneSummaryDto>();
        foreach (var zone in zones)
        {
            var zoneRuns = runs.Where(r => r.ZoneCode == zone.Code).ToList();
            var zoneResults = results.Where(x => runById[x.RunId].ZoneCode == zone.Code).ToList();
            var usedItemIds = zoneResults.Where(x => runById[x.RunId].WorkDate >= first && runById[x.RunId].WorkDate <= last)
                .Select(x => x.ItemId).ToHashSet();
            var candidates = ItemsOf(ctx, zone)
                .Where(c => usedItemIds.Contains(c.Item.Id)
                            || (c.Item.IsActive && (c.Item.ValidFrom is null || c.Item.ValidFrom <= last) && (c.Item.ValidTo is null || c.Item.ValidTo >= first)))
                .OrderBy(c => c.Common ? 0 : 1)
                .ThenBy(c => c.Item.Timing == CheckTimings.Weekly ? 1 : c.Item.Timing == CheckTimings.Event ? 2 : 0)
                .ThenBy(c => c.Item.SortOrder).ThenBy(c => c.Item.Code)
                .ToList();
            int due = 0, done = 0, ng = 0, missing = 0;
            foreach (var (item, _) in candidates)
            {
                var cells = Enumerable.Repeat("", days * 2).ToArray();
                var itemResults = zoneResults.Where(x => x.ItemId == item.Id && x.Result.Length > 0).ToList();
                foreach (var x in itemResults)
                {
                    var r = runById[x.RunId];
                    if (r.WorkDate < first || r.WorkDate > last) continue;
                    cells[(r.WorkDate.Day - 1) * 2 + (r.Shift == ShiftNight ? 1 : 0)] = Mark(x);
                    if (x.Result == "NG") ng++;
                }
                if (item.Timing == CheckTimings.Weekly)
                {
                    for (var w = WeekStart(first); w <= last; w = w.AddDays(7))
                    {
                        var dueDay = w.AddDays((item.Weekday ?? 7) - 1);
                        var doneInWeek = itemResults.Any(x => runById[x.RunId].WorkDate >= w && runById[x.RunId].WorkDate <= w.AddDays(6));
                        if (dueDay < first || dueDay > last || !item.Required || !ValidOn(item, dueDay)) continue;
                        due++;
                        if (doneInWeek) { done++; continue; }
                        // 그 주가 끝났는데도 안 했으면 지정 요일 칸에 "미".
                        if (Elapsed(w.AddDays(6), ShiftNight)) { missing++; cells[(dueDay.Day - 1) * 2] = "미"; }
                    }
                }
                else if (item.Timing != CheckTimings.Event)
                {
                    for (var d = first; d <= last; d = d.AddDays(1))
                    {
                        if (!ValidOn(item, d) || !item.Required) continue;
                        foreach (var shift in new[] { ShiftDay, ShiftNight })
                        {
                            if (item.Timing == CheckTimings.DayOnly && shift != ShiftDay) continue;
                            if (item.Timing == CheckTimings.NightOnly && shift != ShiftNight) continue;
                            var idx = (d.Day - 1) * 2 + (shift == ShiftNight ? 1 : 0);
                            if (cells[idx].Length > 0) { due++; done++; continue; }
                            if (!Elapsed(d, shift)) continue;
                            due++; missing++; cells[idx] = "미";
                        }
                    }
                }
                rows.Add(new CheckReportRowDto(zone.Code, zone.Name, item.Code, item.Text, item.Detail, item.Timing, item.PaperForm, cells));
            }
            summaries.Add(new CheckReportZoneSummaryDto(zone.Code, zone.Name, due, done, ng, missing));
        }

        var ngs = results.Where(x => x.NgStatus != "" && runById[x.RunId].WorkDate >= first && runById[x.RunId].WorkDate <= last)
            .OrderBy(x => runById[x.RunId].WorkDate).ThenBy(x => x.CheckedAt)
            .Select(x => ToNg(x, runById[x.RunId], zones.FirstOrDefault(z => z.Code == runById[x.RunId].ZoneCode),
                ctx.Items.FirstOrDefault(i => i.Id == x.ItemId)?.NgDept ?? ""))
            .ToList();

        return new CheckReportDto(line, year, month, days, settings["FormName"], settings["Revision"], settings["EffectiveDate"],
            rows, summaries, ngs, Now);
    }

    // ───────────────────────── 양식 관리 ─────────────────────────

    public async Task<IReadOnlyList<CheckZoneDto>> GetZonesAsync()
        => (await _db.CheckZones.AsNoTracking().OrderBy(z => z.Line).ThenBy(z => z.SortOrder).ThenBy(z => z.Code).ToListAsync())
            .Select(ToDto).ToList();

    private static CheckZoneDto ToDto(CheckZone z)
        => new(z.Id, z.Code, z.Name, z.Line, z.SortOrder, z.IsCommon, z.HasQr, z.QrLocation, z.QrCount, z.IsActive, z.Note);

    public async Task<CheckZoneDto> SaveZoneAsync(CheckZoneDto dto)
    {
        var code = (dto.Code ?? "").Trim().ToUpperInvariant();
        if (!CodePattern.IsMatch(code)) throw new BusinessRuleException("구역코드는 영문 대문자·숫자·하이픈으로 20자 이내여야 합니다(예: M-OUT).");
        var name = (dto.Name ?? "").Trim();
        if (name.Length == 0) throw new BusinessRuleException("구역 이름을 적어 주세요.");

        CheckZone? zone;
        if (dto.Id > 0)
        {
            zone = await _db.CheckZones.FindAsync(dto.Id) ?? throw new BusinessRuleException("구역을 찾을 수 없습니다.");
            if (!string.Equals(zone.Code, code, StringComparison.Ordinal))
                throw new BusinessRuleException("구역코드는 QR 에 들어가 있어 바꿀 수 없습니다. 새 구역을 만들고 이 구역은 사용 안 함으로 두세요.");
        }
        else
        {
            if (await _db.CheckZones.AnyAsync(z => z.Code == code)) throw new BusinessRuleException("이미 있는 구역코드입니다.");
            zone = new CheckZone { Code = code };
            _db.CheckZones.Add(zone);
        }
        ApplyZone(zone, dto, name);
        await _db.SaveChangesAsync();
        return ToDto(zone);
    }

    private static void ApplyZone(CheckZone zone, CheckZoneDto dto, string name)
    {
        zone.Name = Cut(name, 40);
        zone.Line = Cut((dto.Line ?? "").Trim(), 20);
        zone.SortOrder = dto.SortOrder;
        zone.IsCommon = dto.IsCommon;
        zone.HasQr = !dto.IsCommon && dto.HasQr;
        zone.QrLocation = Cut((dto.QrLocation ?? "").Trim(), 100);
        zone.QrCount = Math.Clamp(dto.QrCount, 0, 20);
        zone.IsActive = dto.IsActive;
        zone.Note = Cut((dto.Note ?? "").Trim(), 300);
    }

    public async Task<IReadOnlyList<CheckItemDto>> GetItemsAsync()
        => (await _db.CheckItems.AsNoTracking().OrderBy(i => i.ZoneCode).ThenBy(i => i.SortOrder).ThenBy(i => i.Code).ToListAsync())
            .Select(ToDto).ToList();

    private static CheckItemDto ToDto(CheckItem i)
        => new(i.Id, i.Code, i.ZoneCode, i.SortOrder, i.Text, i.Detail, i.Cycle, i.Timing, i.Weekday, i.ResultType, i.Unit,
            i.MinValue, i.MaxValue, i.JudgeMode, i.PhotoPolicy, i.Required, i.AllowNa, i.PaperForm, i.NgDept,
            i.ValidFrom, i.ValidTo, i.RevisionNote, i.IsActive, i.Note, i.UpdatedAt, i.UpdatedBy);

    public async Task<CheckItemDto> SaveItemAsync(CheckItemDto dto, CheckActor actor)
    {
        var zones = await _db.CheckZones.AsNoTracking().ToListAsync();
        var codes = await _db.CheckItems.AsNoTracking().Select(i => new { i.Id, i.Code }).ToListAsync();
        var item = await UpsertItemAsync(dto, actor, zones, codes.Select(c => (c.Id, c.Code)).ToList());
        await _db.SaveChangesAsync();
        return ToDto(item);
    }

    private async Task<CheckItem> UpsertItemAsync(CheckItemDto dto, CheckActor actor, List<CheckZone> zones, List<(int Id, string Code)> existing)
    {
        var zoneCode = (dto.ZoneCode ?? "").Trim().ToUpperInvariant();
        var zone = zones.FirstOrDefault(z => z.Code == zoneCode)
            ?? throw new BusinessRuleException($"구역코드 '{zoneCode}' 가 없습니다. 구역을 먼저 만드세요.");
        var text = (dto.Text ?? "").Trim();
        if (text.Length == 0) throw new BusinessRuleException("점검 내용을 적어 주세요.");
        var timing = (dto.Timing ?? "").Trim();
        if (!CheckTimings.All.Contains(timing)) throw new BusinessRuleException($"점검 시점 '{timing}' 을(를) 알 수 없습니다.");
        if (dto.Weekday is < 1 or > 7) throw new BusinessRuleException("요일은 1(월)~7(일)이어야 합니다.");
        var type = dto.ResultType == CheckResultTypes.Num ? CheckResultTypes.Num : CheckResultTypes.OkNg;
        var judge = type == CheckResultTypes.Num ? (dto.JudgeMode is CheckJudgeModes.Abs or CheckJudgeModes.Range ? dto.JudgeMode : CheckJudgeModes.None) : CheckJudgeModes.None;
        if (judge == CheckJudgeModes.Abs && dto.MaxValue is null) throw new BusinessRuleException("절댓값 판정에는 상한이 필요합니다.");
        if (judge == CheckJudgeModes.Range && dto.MinValue is null && dto.MaxValue is null) throw new BusinessRuleException("범위 판정에는 하한이나 상한이 필요합니다.");
        if (dto.MinValue is not null && dto.MaxValue is not null && dto.MinValue > dto.MaxValue) throw new BusinessRuleException("하한이 상한보다 큽니다.");
        var photo = CheckPhotoPolicies.All.Contains(dto.PhotoPolicy ?? "") ? dto.PhotoPolicy! : CheckPhotoPolicies.OnNg;

        CheckItem? item = null;
        var code = (dto.Code ?? "").Trim().ToUpperInvariant();
        if (dto.Id > 0) item = await _db.CheckItems.FindAsync(dto.Id);
        if (item is null && code.Length > 0)
            item = await _db.CheckItems.FirstOrDefaultAsync(i => i.Code == code);
        if (item is null)
        {
            if (code.Length == 0) code = NextItemCode(zone, existing);
            if (!CodePattern.IsMatch(code)) throw new BusinessRuleException($"항목ID '{code}' 형식이 올바르지 않습니다.");
            if (existing.Any(e => e.Code == code)) throw new BusinessRuleException($"항목ID '{code}' 가 이미 있습니다.");
            item = new CheckItem { Code = code };
            _db.CheckItems.Add(item);
            existing.Add((0, code));
        }
        else if (code.Length > 0 && code != item.Code)
        {
            throw new BusinessRuleException("항목ID 는 바꿀 수 없습니다.");
        }

        item.ZoneCode = zone.Code;
        item.SortOrder = dto.SortOrder;
        item.Text = Cut(text, 300);
        item.Detail = Cut((dto.Detail ?? "").Trim(), 300);
        item.Timing = timing;
        item.Cycle = timing switch { CheckTimings.Weekly => "매주", CheckTimings.Event => "이벤트", _ => "매일" };
        item.Weekday = timing == CheckTimings.Weekly ? dto.Weekday : null;
        item.ResultType = type;
        item.Unit = Cut((dto.Unit ?? "").Trim(), 10);
        item.MinValue = type == CheckResultTypes.Num ? dto.MinValue : null;
        item.MaxValue = type == CheckResultTypes.Num ? dto.MaxValue : null;
        item.JudgeMode = judge;
        item.PhotoPolicy = photo;
        item.Required = dto.Required;
        item.AllowNa = dto.AllowNa;
        item.PaperForm = Cut((dto.PaperForm ?? "").Trim(), 60);
        item.NgDept = Cut((dto.NgDept ?? "").Trim(), 40);
        item.ValidFrom = dto.ValidFrom;
        item.ValidTo = dto.ValidTo;
        item.RevisionNote = Cut((dto.RevisionNote ?? "").Trim(), 200);
        item.IsActive = dto.IsActive;
        item.Note = Cut((dto.Note ?? "").Trim(), 300);
        item.UpdatedAt = Now;
        item.UpdatedBy = actor.Name;
        return item;
    }

    /// <summary>구역코드 앞부분(M-, N-)으로 다음 번호를 붙인다: M-034 …</summary>
    private static string NextItemCode(CheckZone zone, List<(int Id, string Code)> existing)
    {
        var dash = zone.Code.IndexOf('-');
        var prefix = (dash > 0 ? zone.Code[..dash] : zone.Code) + "-";
        var max = existing.Select(e => e.Code)
            .Where(c => c.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(c[prefix.Length..], out _))
            .Select(c => int.Parse(c[prefix.Length..], CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0).Max();
        return $"{prefix}{max + 1:000}";
    }

    public async Task<bool> DeleteItemAsync(int id)
    {
        var item = await _db.CheckItems.FindAsync(id);
        if (item is null) return false;
        if (await _db.CheckResults.AnyAsync(x => x.ItemId == id))
            throw new BusinessRuleException("이미 점검 기록이 있는 항목은 지울 수 없습니다. '사용' 을 끄거나 적용 종료일을 넣으세요.");
        _db.CheckItems.Remove(item);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>엑셀(양식 입력 파일)에서 읽은 구역·항목을 코드 기준으로 넣거나 고친다. 파일에 없는 것은 건드리지 않는다.</summary>
    public async Task<CheckImportResultDto> ImportAsync(CheckImportRequest req, CheckActor actor)
    {
        var warnings = new List<string>();
        int za = 0, zu = 0, ia = 0, iu = 0;
        await using var tx = await _db.Database.BeginTransactionAsync();

        foreach (var z in req.Zones ?? Array.Empty<CheckZoneDto>())
        {
            var code = (z.Code ?? "").Trim().ToUpperInvariant();
            if (!CodePattern.IsMatch(code) || string.IsNullOrWhiteSpace(z.Name)) { warnings.Add($"구역 '{z.Code}' — 코드나 이름이 올바르지 않아 건너뜀"); continue; }
            var zone = await _db.CheckZones.FirstOrDefaultAsync(v => v.Code == code);
            if (zone is null) { zone = new CheckZone { Code = code }; _db.CheckZones.Add(zone); za++; }
            else zu++;
            ApplyZone(zone, z, z.Name.Trim());
        }
        await _db.SaveChangesAsync();

        var zones = await _db.CheckZones.AsNoTracking().ToListAsync();
        var existing = (await _db.CheckItems.AsNoTracking().Select(i => new { i.Id, i.Code }).ToListAsync())
            .Select(c => (c.Id, c.Code)).ToList();
        foreach (var i in req.Items ?? Array.Empty<CheckItemDto>())
        {
            try
            {
                var had = !string.IsNullOrWhiteSpace(i.Code) && existing.Any(e => e.Code == i.Code.Trim().ToUpperInvariant() && e.Id > 0);
                await UpsertItemAsync(i with { Id = 0 }, actor, zones, existing);
                if (had) iu++; else ia++;
            }
            catch (BusinessRuleException ex)
            {
                warnings.Add($"항목 '{(string.IsNullOrWhiteSpace(i.Code) ? i.Text : i.Code)}' — {ex.Message}");
            }
        }
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return new CheckImportResultDto(za, zu, ia, iu, warnings);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetSettingsAsync() => await SettingsAsync();

    public async Task<IReadOnlyDictionary<string, string>> SaveSettingsAsync(IReadOnlyDictionary<string, string> values)
    {
        var merged = await SettingsAsync();
        var changed = new Dictionary<string, string>();
        foreach (var (key, raw) in values)
        {
            if (!DefaultSettings.ContainsKey(key)) continue;
            var value = (raw ?? "").Trim();
            if (key == "QrBaseUrl") value = value.TrimEnd('/');
            if (key is "DayStart" or "NightStart" && !TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new BusinessRuleException("교대 시각은 07:00 처럼 HH:mm 으로 적어 주세요.");
            if (key == "EffectiveDate" && value.Length > 0 && !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new BusinessRuleException("시행일은 2026-10-01 처럼 적어 주세요.");
            if (key == "QrBaseUrl" && value.Length > 0 && !(Uri.TryCreate(value, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"))
                throw new BusinessRuleException("QR 기본 주소는 http:// 로 시작하는 주소여야 합니다.");
            merged[key] = Cut(value, 400);
            changed[key] = merged[key];
        }
        if (ParseTime(merged["DayStart"], default) >= ParseTime(merged["NightStart"], default))
            throw new BusinessRuleException("야간 시작 시각이 주간 시작 시각보다 늦어야 합니다.");

        foreach (var (key, value) in changed)
        {
            var row = await _db.CheckSettings.FirstOrDefaultAsync(x => x.Key == key);
            if (row is null) _db.CheckSettings.Add(new CheckSetting { Key = key, Value = value });
            else row.Value = value;
        }
        await _db.SaveChangesAsync();
        return await SettingsAsync();
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];
}
