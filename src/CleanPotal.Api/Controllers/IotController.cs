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
public class IotController : ControllerBase
{
    private const int MaxHistory = 2000;
    private const int MaxRecent = 200;

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

    /// <summary>센서 한 대의 이력. 그래프가 쓴다. 기본은 최근 24시간이다.</summary>
    [HttpGet("history/{deviceId}")]
    public async Task<ActionResult<SensorHistoryDto>> History(
        string deviceId, [FromQuery] int hours, [FromQuery] int limit, CancellationToken ct)
    {
        var since = DateTime.Now.AddHours(-(hours <= 0 ? 24 : Math.Min(hours, 24 * 30)));
        var take = limit <= 0 ? 500 : Math.Min(limit, MaxHistory);

        var s = await _db.ZigbeeSensors.AsNoTracking().FirstOrDefaultAsync(x => x.DeviceId == deviceId, ct);
        if (s is null) return NotFound();

        // 최근 것부터 잘라 온 뒤 시간 순으로 되돌린다 — 구간이 길면 앞이 아니라 뒤를 봐야 한다.
        var rows = await _db.ZigbeeReadings.AsNoTracking()
            .Where(r => r.DeviceId == deviceId && r.ReceivedAt >= since)
            .OrderByDescending(r => r.ReceivedAt)
            .Take(take)
            .ToListAsync(ct);

        var points = rows
            .OrderBy(r => r.ReceivedAt)
            .Select(r => new SensorHistoryPointDto(r.ReceivedAt, r.Temperature, r.Humidity))
            .ToList();

        return Ok(new SensorHistoryDto(deviceId, s.DisplayName, points));
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

    private static ZigbeeLimitResolver.Row ToRow(ZigbeeThreshold t) => new(
        t.Scope, t.ScopeKey,
        t.TempNormalMin, t.TempNormalMax, t.TempWarnMin, t.TempWarnMax,
        t.HumidNormalMin, t.HumidNormalMax, t.HumidWarnMin, t.HumidWarnMax,
        t.OfflineAfterMinutes, t.LowBatteryPercent, t.SnapshotIntervalMinutes);

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
        => new(_store.MqttConnected, _store.Zigbee2MqttOnline,
               sensors.Count(s => s.Status != "offline"), sensors.Count, _store.LastError);
}
