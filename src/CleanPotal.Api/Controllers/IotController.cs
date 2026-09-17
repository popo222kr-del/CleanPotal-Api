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
        var list = sensors.Select(s => ZigbeeMapping.ToDto(
            s.DeviceId, s.DisplayName, s.Site, _store.Get(s.DeviceId), now, _options)).ToList();

        return Ok(new SensorSnapshotDto(list, Status(list)));
    }

    /// <summary>센서 한 대의 최신 값.</summary>
    [HttpGet("latest/{deviceId}")]
    public async Task<ActionResult<SensorReadingDto>> Latest(string deviceId, CancellationToken ct)
    {
        var s = await _db.ZigbeeSensors.AsNoTracking().FirstOrDefaultAsync(x => x.DeviceId == deviceId, ct);
        if (s is null) return NotFound();
        return Ok(ZigbeeMapping.ToDto(s.DeviceId, s.DisplayName, s.Site, _store.Get(s.DeviceId), DateTime.Now, _options));
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
        var now = DateTime.Now;
        var take = limit <= 0 ? 50 : Math.Min(limit, MaxRecent);
        var sensors = (await SensorsAsync(ct)).ToDictionary(s => s.DeviceId, s => s, StringComparer.OrdinalIgnoreCase);

        var rows = await _db.ZigbeeReadings.AsNoTracking()
            .OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id)
            .Take(take)
            .ToListAsync(ct);

        var list = rows.Select(r =>
        {
            sensors.TryGetValue(r.DeviceId, out var s);
            var live = new ZigbeeSensorStore.Live(r.Temperature, r.Humidity, r.Battery, r.LinkQuality, r.ReceivedAt);
            // 이력 한 줄의 상태는 '그때 그 값' 으로 본다 — 지금 시각으로 재면 옛날 줄이 전부 통신 끊김이 된다.
            return ZigbeeMapping.ToDto(r.DeviceId, s?.DisplayName ?? r.DeviceId, s?.Site ?? "", live, r.ReceivedAt, _options);
        }).ToList();

        return Ok(list);
    }

    /// <summary>수집 계통 상태만. 화면 오른쪽 위의 작은 표시가 쓴다.</summary>
    [HttpGet("status")]
    public async Task<ActionResult<ZigbeeStatusDto>> Status(CancellationToken ct)
    {
        var now = DateTime.Now;
        var list = (await SensorsAsync(ct))
            .Select(s => ZigbeeMapping.ToDto(s.DeviceId, s.DisplayName, s.Site, _store.Get(s.DeviceId), now, _options))
            .ToList();
        return Ok(Status(list));
    }

    // ── 내부 ───────────────────────────────────────────────────────────────

    private Task<List<ZigbeeSensor>> SensorsAsync(CancellationToken ct)
        => _db.ZigbeeSensors.AsNoTracking()
            .Where(s => s.IsEnabled)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DeviceId)
            .ToListAsync(ct);

    private ZigbeeStatusDto Status(IReadOnlyList<SensorReadingDto> sensors)
        => new(_store.MqttConnected, _store.Zigbee2MqttOnline,
               sensors.Count(s => s.Status != "offline"), sensors.Count, _store.LastError);
}
