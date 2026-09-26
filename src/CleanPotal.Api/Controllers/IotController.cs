using System.Security.Claims;
using CleanPotal.Api.Infrastructure;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Iot;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 현장 점검 — 온·습도 모니터링. 창고 센서 값을 화면에 전달한다.
///
/// 값은 포털이 직접 Mosquitto 를 구독해 받아 둔 것이다(ZigbeeMqttService). 중간에 다른 프로그램은 없다.
/// 최신값은 메모리에서, 이력은 ZigbeeReadings 표에서 읽는다 — 화면이 몇 초마다 묻는 최신값 때문에
/// 이력 표를 뒤지지 않게 하기 위해서다.
///
/// 브로커가 멎어도 200 으로 답한다. 화면 전체를 오류로 만들지 않고 센서 칸만 '통신 끊김' 으로 두기 위해서다.
/// </summary>
[ApiController]
[Route("api/iot/zigbee")]
[Authorize(Policy = "ViewField")]
[MenuGate("/temp-humidity")]
public class IotController : ControllerBase
{
    private const int MaxRecent = 200;

    /// <summary>한 번에 읽어 올 원본 줄 수 상한. 이보다 길면 묶어서 평균을 낸다.</summary>
    public const int MaxRawPoints = 20_000;

    /// <summary>그래프에 그릴 점 수 상한. 이보다 촘촘해도 사람 눈에는 같다.</summary>
    private const int MaxPlotPoints = 1_200;

    /// <summary>내보내기 줄 수 상한. 브라우저가 엑셀을 만들다 멈추지 않을 정도다.</summary>
    private const int MaxExportRows = 50_000;

    /// <summary>조회할 수 있는 가장 긴 구간(일). 더 길면 읽어야 할 줄이 감당이 안 된다.</summary>
    private const int MaxRangeDays = 92;

    /// <summary>이 기간을 넘으면 주기 기록을 빼고 실제 수신만 본다 — 줄 수가 한 자릿수로 줄어든다.</summary>
    private const int RealOnlyAfterDays = 2;

    private readonly CleanPotalDbContext _db;
    private readonly ZigbeeSensorStore _store;
    private readonly ZigbeeOptions _options;

    public IotController(CleanPotalDbContext db, ZigbeeSensorStore store, IOptions<ZigbeeOptions> options)
    {
        _db = db;
        _store = store;
        _options = options.Value;
    }

    /// <summary>센서 전부의 최신 값 + 수집 계통 상태. 화면이 몇 초마다 이것만 부른다.</summary>
    [HttpGet("latest")]
    public async Task<ActionResult<SensorSnapshotDto>> Latest(CancellationToken ct)
    {
        var now = DateTime.Now;
        var sensors = await SensorsAsync(ct);
        var rows = await LimitRowsAsync(ct);
        var list = sensors.Select(s => ZigbeeMapping.ToDto(
            s.DeviceId, s.DisplayName, s.Site, _store.Get(s.DeviceId), now,
            ZigbeeLimitResolver.Resolve(s.DeviceId, s.Site, rows, _options))).ToList();

        return Ok(new SensorSnapshotDto(list, Status(list)));
    }

    /// <summary>센서 한 대의 최신 값.</summary>
    [HttpGet("latest/{deviceId}")]
    public async Task<ActionResult<SensorReadingDto>> Latest(string deviceId, CancellationToken ct)
    {
        var s = await _db.ZigbeeSensors.AsNoTracking().FirstOrDefaultAsync(x => x.DeviceId == deviceId, ct);
        if (s is null) return NotFound();
        var limits = ZigbeeLimitResolver.Resolve(s.DeviceId, s.Site, await LimitRowsAsync(ct), _options);
        return Ok(ZigbeeMapping.ToDto(s.DeviceId, s.DisplayName, s.Site, _store.Get(s.DeviceId), DateTime.Now, limits));
    }

    /// <summary>
    /// 센서 한 대의 이력. 그래프가 쓴다. 기본은 최근 24시간이고, from/to 로 지난 날짜도 볼 수 있다.
    ///
    /// 구간이 길어지면 줄 수가 감당이 안 된다(1분마다 남기므로 30일이면 4만 줄이 넘는다). 그래서
    /// 이틀을 넘기면 <b>실제 수신만</b> 읽고, 그래도 많으면 <b>묶어서 평균</b>을 낸다. 화면에서 보이는
    /// 모양은 같고 그리는 속도만 달라진다.
    /// </summary>
    [HttpGet("history/{deviceId}")]
    public async Task<ActionResult<SensorHistoryDto>> History(
        string deviceId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int hours, [FromQuery] int bucketMinutes, CancellationToken ct)
    {
        var s = await _db.ZigbeeSensors.AsNoTracking().FirstOrDefaultAsync(x => x.DeviceId == deviceId, ct);
        if (s is null) return NotFound();

        var (start, end, realOnly) = Range(from, to, hours);

        var q = _db.ZigbeeReadings.AsNoTracking()
            .Where(r => r.DeviceId == deviceId && r.ReceivedAt >= start && r.ReceivedAt <= end);
        if (realOnly) q = q.Where(r => !r.IsSnapshot);

        // 줄이 너무 많으면 원본을 다 읽지 않고 DB 에서 시간 칸별 평균을 낸다. 예전에는 오래된 순으로
        // 앞 2만 줄만 읽어서, 긴 구간을 보면 최근 며칠이 아무 표시 없이 그래프에서 빠졌다.
        if (await q.CountAsync(ct) > MaxRawPoints)
        {
            var (aggregated, aggBucket) = await AggregateAsync(q, start, end, ct);
            return Ok(new SensorHistoryDto(deviceId, s.DisplayName, aggregated, start, end, aggBucket, realOnly));
        }

        var rows = await q
            .OrderBy(r => r.ReceivedAt)
            .Select(r => new { r.ReceivedAt, r.Temperature, r.Humidity })
            .ToListAsync(ct);

        var points = rows
            .Select(r => new SensorHistoryPointDto(r.ReceivedAt, r.Temperature, r.Humidity))
            .ToList();

        // 점이 너무 많으면 화면이 버벅인다. 사람 눈에 보이는 해상도 이상은 의미가 없다.
        var bucket = bucketMinutes > 0 ? bucketMinutes : AutoBucket(points.Count, start, end);
        if (bucket > 0) points = Bucketize(points, bucket);

        return Ok(new SensorHistoryDto(deviceId, s.DisplayName, points, start, end, bucket, realOnly));
    }

    /// <summary>구간 요약 — 최고·최저·평균과 기준을 벗어난 시간. 품질 기록에서 실제로 찾게 되는 값이다.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<SensorSummaryPageDto>> Summary(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int hours, CancellationToken ct)
    {
        var (start, end, _) = Range(from, to, hours);
        var sensors = await SensorsAsync(ct);
        var limitRows = await LimitRowsAsync(ct);
        var list = new List<SensorSummaryDto>(sensors.Count);

        foreach (var s in sensors)
        {
            var limits = ZigbeeLimitResolver.Resolve(s.DeviceId, s.Site, limitRows, _options);
            // 질의식이 SQL 로 잘 옮겨지도록 숫자만 꺼내 둔다(객체 속성을 그대로 두면 번역이 흔들린다).
            double tWarnMin = limits.Temperature.WarnMin, tWarnMax = limits.Temperature.WarnMax;
            double tNormMin = limits.Temperature.NormalMin, tNormMax = limits.Temperature.NormalMax;
            double hWarnMin = limits.Humidity.WarnMin, hWarnMax = limits.Humidity.WarnMax;
            double hNormMin = limits.Humidity.NormalMin, hNormMax = limits.Humidity.NormalMax;

            var stat = await _db.ZigbeeReadings.AsNoTracking()
                .Where(r => r.DeviceId == s.DeviceId && r.ReceivedAt >= start && r.ReceivedAt <= end)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    First = (DateTime?)g.Min(r => r.ReceivedAt),
                    Last = (DateTime?)g.Max(r => r.ReceivedAt),
                    TempMin = g.Min(r => r.Temperature),
                    TempMax = g.Max(r => r.Temperature),
                    TempAvg = g.Average(r => r.Temperature),
                    HumidMin = g.Min(r => r.Humidity),
                    HumidMax = g.Max(r => r.Humidity),
                    HumidAvg = g.Average(r => r.Humidity),
                    // 경고는 주의 구간마저 벗어난 것 — 주의 구간이 정상 구간을 감싸므로 경고 ⊂ 정상 밖이다.
                    Alert = g.Count(r => (r.Temperature != null && (r.Temperature > tWarnMax || r.Temperature < tWarnMin))
                                      || (r.Humidity != null && (r.Humidity > hWarnMax || r.Humidity < hWarnMin))),
                    Outside = g.Count(r => (r.Temperature != null && (r.Temperature > tNormMax || r.Temperature < tNormMin))
                                        || (r.Humidity != null && (r.Humidity > hNormMax || r.Humidity < hNormMin))),
                })
                .FirstOrDefaultAsync(ct);

            if (stat is null || stat.Count == 0)
            {
                list.Add(new SensorSummaryDto(s.DeviceId, s.DisplayName, s.Site, limits.Source,
                    0, null, null, null, null, null, null, null, null, 0, 0, 0));
                continue;
            }

            // 줄 수를 시간으로 바꾼다. 줄 간격이 일정하지 않으므로 구간 길이를 줄 수로 나눠 환산한다.
            var perRow = (stat.Last!.Value - stat.First!.Value).TotalMinutes / Math.Max(1, stat.Count - 1);
            if (perRow <= 0 || double.IsNaN(perRow)) perRow = 1;
            int Minutes(int count) => (int)Math.Round(count * perRow);

            list.Add(new SensorSummaryDto(
                s.DeviceId, s.DisplayName, s.Site, limits.Source,
                stat.Count, stat.First, stat.Last,
                stat.TempMin, stat.TempMax, Round(stat.TempAvg),
                stat.HumidMin, stat.HumidMax, Round(stat.HumidAvg),
                Minutes(stat.Count - stat.Outside), Minutes(stat.Outside - stat.Alert), Minutes(stat.Alert)));
        }

        return Ok(new SensorSummaryPageDto(start, end, list));
    }

    /// <summary>내보내기용 원본 줄. 화면이 받아서 엑셀로 만든다.</summary>
    [HttpGet("export")]
    public async Task<ActionResult<SensorExportDto>> Export(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int hours, CancellationToken ct)
    {
        var (start, end, realOnly) = Range(from, to, hours);
        var sensors = (await SensorsAsync(ct)).ToDictionary(s => s.DeviceId, s => s, StringComparer.OrdinalIgnoreCase);
        var limitRows = await LimitRowsAsync(ct);

        var q = _db.ZigbeeReadings.AsNoTracking()
            .Where(r => r.ReceivedAt >= start && r.ReceivedAt <= end);
        if (realOnly) q = q.Where(r => !r.IsSnapshot);

        // 한 줄 더 읽어서 잘렸는지 본다 — 사용자가 "이게 전부"라고 오해하면 안 된다.
        var rows = await q.OrderBy(r => r.ReceivedAt).Take(MaxExportRows + 1).ToListAsync(ct);
        var truncated = rows.Count > MaxExportRows;
        if (truncated) rows.RemoveAt(rows.Count - 1);

        var list = rows.Select(r =>
        {
            sensors.TryGetValue(r.DeviceId, out var s);
            var limits = ZigbeeLimitResolver.Resolve(r.DeviceId, s?.Site ?? "", limitRows, _options);
            var verdict = SensorStatusEvaluator.Evaluate(r.Temperature, r.Humidity, r.ReceivedAt, r.ReceivedAt, limits);
            return new SensorExportRowDto(
                r.ReceivedAt, r.DeviceId, s?.DisplayName ?? r.DeviceId, s?.Site ?? "",
                r.Temperature, r.Humidity, r.Battery, r.LinkQuality, r.IsSnapshot,
                SensorStatusEvaluator.Label(verdict.Status));
        }).ToList();

        return Ok(new SensorExportDto(start, end, realOnly, truncated, list));
    }

    /// <summary>
    /// 센서를 가리지 않은 최근 수신 이력. 화면 아래 표가 쓴다.
    /// 표에서 읽으므로 새로 고쳐도 목록이 비지 않는다.
    /// </summary>
    [HttpGet("recent")]
    public async Task<ActionResult<IReadOnlyList<SensorReadingDto>>> Recent([FromQuery] int limit, CancellationToken ct)
    {
        var take = limit <= 0 ? 50 : Math.Min(limit, MaxRecent);
        var sensors = (await SensorsAsync(ct)).ToDictionary(s => s.DeviceId, s => s, StringComparer.OrdinalIgnoreCase);
        var limitRows = await LimitRowsAsync(ct);

        var readings = await _db.ZigbeeReadings.AsNoTracking()
            .Where(r => !r.IsSnapshot)
            .OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id)
            .Take(take)
            .ToListAsync(ct);

        var list = readings.Select(r =>
        {
            sensors.TryGetValue(r.DeviceId, out var s);
            var live = new ZigbeeSensorStore.Live(r.Temperature, r.Humidity, r.Battery, r.LinkQuality, r.ReceivedAt);
            // 이력 한 줄의 상태는 '그때 그 값' 으로 본다 — 지금 시각으로 재면 옛날 줄이 전부 통신 끊김이 된다.
            var limits = ZigbeeLimitResolver.Resolve(r.DeviceId, s?.Site ?? "", limitRows, _options);
            return ZigbeeMapping.ToDto(r.DeviceId, s?.DisplayName ?? r.DeviceId, s?.Site ?? "", live, r.ReceivedAt, limits);
        }).ToList();

        return Ok(list);
    }

    /// <summary>수집 계통 상태만. 화면 오른쪽 위의 작은 표시가 쓴다.</summary>
    [HttpGet("status")]
    public async Task<ActionResult<ZigbeeStatusDto>> Status(CancellationToken ct)
    {
        var now = DateTime.Now;
        var rows = await LimitRowsAsync(ct);
        var list = (await SensorsAsync(ct))
            .Select(s => ZigbeeMapping.ToDto(s.DeviceId, s.DisplayName, s.Site, _store.Get(s.DeviceId), now,
                ZigbeeLimitResolver.Resolve(s.DeviceId, s.Site, rows, _options)))
            .ToList();
        return Ok(Status(list));
    }

    // ── 판정 기준 (보기는 현장 점검, 고치기는 관리자) ──────────────────────

    /// <summary>
    /// 지금 걸려 있는 기준들. 좁은 쪽이 이긴다 — 센서 → 사업장 → 전체 → 설정 파일.
    /// 표에 없는 대상은 목록에 뜨지 않고, 그 대상은 위 단계의 기준을 그대로 따른다.
    /// </summary>
    [HttpGet("thresholds")]
    public async Task<ActionResult<ZigbeeThresholdPageDto>> Thresholds(CancellationToken ct)
    {
        var sensors = await SensorsAsync(ct);
        var stored = await _db.ZigbeeThresholds.AsNoTracking()
            .OrderBy(t => t.Scope).ThenBy(t => t.ScopeKey).ToListAsync(ct);

        var fallback = ZigbeeLimitResolver.FromOptions(_options);
        var sites = sensors
            .Where(s => !string.IsNullOrWhiteSpace(s.Site))
            .Select(s => s.Site).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .Select(x => new ZigbeeScopeOptionDto(x, x))
            .ToList();

        return Ok(new ZigbeeThresholdPageDto(
            FromLimits(ZigbeeLimitResolver.ScopeGlobal, "", "설정 파일 기본값", fallback),
            stored.Select(t => ToDto(t, Label(t, sensors))).ToList(),
            sites,
            sensors.Select(s => new ZigbeeScopeOptionDto(s.DeviceId, s.DisplayName)).ToList()));
    }

    /// <summary>기준 한 벌을 넣거나 고친다. 같은 대상이 이미 있으면 덮어쓴다.</summary>
    [HttpPut("thresholds")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<ActionResult<ZigbeeThresholdResultDto>> SaveThreshold(
        [FromBody] ZigbeeThresholdSaveRequest req, CancellationToken ct)
    {
        var scope = (req.Scope ?? "").Trim().ToLowerInvariant();
        var key = scope == ZigbeeLimitResolver.ScopeGlobal ? "" : (req.ScopeKey ?? "").Trim();

        if (scope is not (ZigbeeLimitResolver.ScopeGlobal or ZigbeeLimitResolver.ScopeSite or ZigbeeLimitResolver.ScopeDevice))
            return Ok(new ZigbeeThresholdResultDto(false, "적용 범위가 올바르지 않습니다."));
        if (scope != ZigbeeLimitResolver.ScopeGlobal && key.Length == 0)
            return Ok(new ZigbeeThresholdResultDto(false, "적용할 사업장 또는 센서를 고르세요."));

        // 주의 구간은 정상 구간을 감싸야 한다. 뒤집혀 있으면 모든 값이 경고가 되어 화면이 못 쓰게 된다.
        if (req.TempWarnMin > req.TempNormalMin || req.TempWarnMax < req.TempNormalMax)
            return Ok(new ZigbeeThresholdResultDto(false, "온도: 주의 구간이 정상 구간을 감싸야 합니다."));
        if (req.HumidWarnMin > req.HumidNormalMin || req.HumidWarnMax < req.HumidNormalMax)
            return Ok(new ZigbeeThresholdResultDto(false, "습도: 주의 구간이 정상 구간을 감싸야 합니다."));
        if (req.TempNormalMin > req.TempNormalMax || req.HumidNormalMin > req.HumidNormalMax)
            return Ok(new ZigbeeThresholdResultDto(false, "정상 구간의 하한이 상한보다 큽니다."));
        if (req.OfflineAfterMinutes < 1)
            return Ok(new ZigbeeThresholdResultDto(false, "미수신 판정 시간은 1분 이상이어야 합니다."));

        var row = await _db.ZigbeeThresholds.FirstOrDefaultAsync(t => t.Scope == scope && t.ScopeKey == key, ct);
        if (row is null)
        {
            row = new ZigbeeThreshold { Scope = scope, ScopeKey = key };
            _db.ZigbeeThresholds.Add(row);
        }

        row.TempNormalMin = req.TempNormalMin;
        row.TempNormalMax = req.TempNormalMax;
        row.TempWarnMin = req.TempWarnMin;
        row.TempWarnMax = req.TempWarnMax;
        row.HumidNormalMin = req.HumidNormalMin;
        row.HumidNormalMax = req.HumidNormalMax;
        row.HumidWarnMin = req.HumidWarnMin;
        row.HumidWarnMax = req.HumidWarnMax;
        row.OfflineAfterMinutes = req.OfflineAfterMinutes;
        row.LowBatteryPercent = Math.Clamp(req.LowBatteryPercent, 0, 100);
        // 기록 주기는 전체 공통이라 global 줄에만 의미가 있다. 다른 줄은 0 으로 두어 오해를 막는다.
        row.SnapshotIntervalMinutes = scope == ZigbeeLimitResolver.ScopeGlobal
            ? Math.Clamp(req.SnapshotIntervalMinutes, 0, 1440)
            : 0;
        row.UpdatedAt = DateTime.Now;
        row.UpdatedBy = User.FindFirst(ClaimTypes.Name)?.Value ?? "";

        await _db.SaveChangesAsync(ct);
        return Ok(new ZigbeeThresholdResultDto(true, "기준을 저장했습니다."));
    }

    /// <summary>기준을 지운다. 지우면 그 대상은 위 단계(사업장 → 전체 → 설정 파일)를 따른다.</summary>
    [HttpDelete("thresholds/{scope}")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<ActionResult<ZigbeeThresholdResultDto>> DeleteThreshold(
        string scope, [FromQuery] string? key, CancellationToken ct)
    {
        var s = (scope ?? "").Trim().ToLowerInvariant();
        var k = s == ZigbeeLimitResolver.ScopeGlobal ? "" : (key ?? "").Trim();
        var row = await _db.ZigbeeThresholds.FirstOrDefaultAsync(t => t.Scope == s && t.ScopeKey == k, ct);
        if (row is null) return Ok(new ZigbeeThresholdResultDto(false, "지울 기준이 없습니다."));

        _db.ZigbeeThresholds.Remove(row);
        await _db.SaveChangesAsync(ct);
        return Ok(new ZigbeeThresholdResultDto(true, "기준을 지웠습니다. 상위 기준을 따릅니다."));
    }

    // ── 내부 ───────────────────────────────────────────────────────────────

    /// <summary>표의 기준을 전부 읽어 온다. 몇 줄뿐이라 요청마다 읽어도 부담이 없고, 고치면 바로 반영된다.</summary>
    private async Task<List<ZigbeeLimitResolver.Row>> LimitRowsAsync(CancellationToken ct)
        => (await _db.ZigbeeThresholds.AsNoTracking().ToListAsync(ct)).Select(ToRow).ToList();

    internal static ZigbeeLimitResolver.Row ToRow(ZigbeeThreshold t) => new(
        t.Scope, t.ScopeKey,
        t.TempNormalMin, t.TempNormalMax, t.TempWarnMin, t.TempWarnMax,
        t.HumidNormalMin, t.HumidNormalMax, t.HumidWarnMin, t.HumidWarnMax,
        t.OfflineAfterMinutes, t.LowBatteryPercent, t.SnapshotIntervalMinutes);

    /// <summary>from/to 또는 hours 로 구간을 정한다. 너무 길면 자른다.</summary>
    private static (DateTime From, DateTime To, bool RealOnly) Range(DateTime? from, DateTime? to, int hours)
    {
        var end = to ?? DateTime.Now;
        var start = from ?? end.AddHours(-(hours <= 0 ? 24 : hours));
        if (start > end) (start, end) = (end, start);

        var max = TimeSpan.FromDays(MaxRangeDays);
        if (end - start > max) start = end - max;

        return (start, end, end - start > TimeSpan.FromDays(RealOnlyAfterDays));
    }

    /// <summary>점이 상한을 넘을 때만 묶는다. 묶는 간격은 구간 길이에서 거꾸로 구한다.</summary>
    private static int AutoBucket(int count, DateTime from, DateTime to)
    {
        if (count <= MaxPlotPoints) return 0;
        var minutes = (to - from).TotalMinutes / MaxPlotPoints;
        return Math.Max(1, (int)Math.Ceiling(minutes));
    }

    /// <summary>
    /// DB 에서 시간 칸별 평균을 낸다. 칸 길이는 그래프 점 수 상한에 맞춰 1시간을 나누는 값(분) 또는
    /// 하루를 나누는 값(시간)으로 고른다 — 연·월·일·시·분 부분만으로 묶어 SQLite·SQL Server 모두에서 번역된다.
    /// </summary>
    public static async Task<(List<SensorHistoryPointDto> Points, int BucketMinutes)> AggregateAsync(
        IQueryable<ZigbeeReading> q, DateTime start, DateTime end, CancellationToken ct)
    {
        var want = Math.Max(1, (int)Math.Ceiling((end - start).TotalMinutes / MaxPlotPoints));
        if (want <= 60)
        {
            var b = new[] { 1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30, 60 }.First(d => d >= want);
            var rows = await q
                .GroupBy(r => new { r.ReceivedAt.Year, r.ReceivedAt.Month, r.ReceivedAt.Day, r.ReceivedAt.Hour, Slot = r.ReceivedAt.Minute / b })
                .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, g.Key.Hour, g.Key.Slot, T = g.Average(r => r.Temperature), H = g.Average(r => r.Humidity) })
                .ToListAsync(ct);
            return (rows
                .Select(x => new SensorHistoryPointDto(new DateTime(x.Year, x.Month, x.Day, x.Hour, x.Slot * b, 0), Round(x.T), Round(x.H)))
                .OrderBy(p => p.ReceivedAt).ToList(), b);
        }
        else
        {
            var h = new[] { 1, 2, 3, 4, 6, 8, 12, 24 }.First(d => d * 60 >= want);
            var rows = await q
                .GroupBy(r => new { r.ReceivedAt.Year, r.ReceivedAt.Month, r.ReceivedAt.Day, Slot = r.ReceivedAt.Hour / h })
                .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, g.Key.Slot, T = g.Average(r => r.Temperature), H = g.Average(r => r.Humidity) })
                .ToListAsync(ct);
            return (rows
                .Select(x => new SensorHistoryPointDto(new DateTime(x.Year, x.Month, x.Day, x.Slot * h, 0, 0), Round(x.T), Round(x.H)))
                .OrderBy(p => p.ReceivedAt).ToList(), h * 60);
        }
    }

    /// <summary>같은 칸에 든 값을 평균 낸다. 시각은 그 칸의 시작으로 둔다.</summary>
    private static List<SensorHistoryPointDto> Bucketize(List<SensorHistoryPointDto> points, int minutes)
    {
        var result = new List<SensorHistoryPointDto>();
        var span = TimeSpan.FromMinutes(minutes).Ticks;

        foreach (var group in points.GroupBy(p => p.ReceivedAt.Ticks / span).OrderBy(g => g.Key))
        {
            var temps = group.Where(p => p.Temperature is not null).Select(p => p.Temperature!.Value).ToList();
            var humids = group.Where(p => p.Humidity is not null).Select(p => p.Humidity!.Value).ToList();
            result.Add(new SensorHistoryPointDto(
                new DateTime(group.Key * span),
                temps.Count > 0 ? Round(temps.Average()) : null,
                humids.Count > 0 ? Round(humids.Average()) : null));
        }

        return result;
    }

    private static double? Round(double? value)
        => value is null ? null : Math.Round(value.Value, 1);

    private Task<List<ZigbeeSensor>> SensorsAsync(CancellationToken ct)
        => _db.ZigbeeSensors.AsNoTracking()
            .Where(s => s.IsEnabled)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DeviceId)
            .ToListAsync(ct);

    private static string Label(ZigbeeThreshold t, IReadOnlyList<ZigbeeSensor> sensors) => t.Scope switch
    {
        ZigbeeLimitResolver.ScopeGlobal => "전체 기본",
        ZigbeeLimitResolver.ScopeSite => $"{t.ScopeKey} (사업장)",
        _ => sensors.FirstOrDefault(s => s.DeviceId == t.ScopeKey)?.DisplayName ?? t.ScopeKey,
    };

    private static ZigbeeThresholdDto ToDto(ZigbeeThreshold t, string label) => new(
        t.Scope, t.ScopeKey, label,
        t.TempNormalMin, t.TempNormalMax, t.TempWarnMin, t.TempWarnMax,
        t.HumidNormalMin, t.HumidNormalMax, t.HumidWarnMin, t.HumidWarnMax,
        t.OfflineAfterMinutes, t.LowBatteryPercent, t.SnapshotIntervalMinutes,
        true, t.UpdatedAt, t.UpdatedBy);

    /// <summary>표에 없는 기본값을 화면이 같은 모양으로 받도록 — 새로 만들 때의 출발점이 된다.</summary>
    private ZigbeeThresholdDto FromLimits(string scope, string key, string label, ZigbeeLimits l) => new(
        scope, key, label,
        l.Temperature.NormalMin, l.Temperature.NormalMax, l.Temperature.WarnMin, l.Temperature.WarnMax,
        l.Humidity.NormalMin, l.Humidity.NormalMax, l.Humidity.WarnMin, l.Humidity.WarnMax,
        l.OfflineAfterMinutes, l.LowBatteryPercent, _options.SnapshotIntervalMinutes,
        false, null, null);

    private ZigbeeStatusDto Status(IReadOnlyList<SensorReadingDto> sensors)
        => new(_store.MqttConnected,
               // 브로커에 붙어 있지 않으면 Z2M 소식을 들을 길이 없으니 '판단 보류'. 예전에는 끊긴 뒤에도 최대
               // 30분 동안 초록으로 남아, MQTT 는 빨강인데 Z2M 은 초록인 모순된 표시가 됐다.
               _store.MqttConnected ? _store.Zigbee2MqttAlive(DateTime.Now, _options.Zigbee2MqttSilentMinutes) : null,
               sensors.Count(s => s.Status != "offline"), sensors.Count, _store.LastError);
}
