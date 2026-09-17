using CleanPotal.Api.Infrastructure;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Iot;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 현장 점검 — 온·습도 모니터링. 창고에 달린 Zigbee 센서 값을 화면에 전달한다.
///
/// 브라우저는 브리지(기본 127.0.0.1:5002)를 직접 부르지 않는다. 포털이 대신 물어보고 정리해서 준다 —
/// CORS 를 피하고, 장비 포트를 밖으로 내보내지 않고, 브리지 PC 가 바뀌어도 설정 한 줄만 고치면 되게.
///
/// 브리지가 멎어도 200 으로 답한다. 화면 전체를 오류로 만들지 않고 센서 칸만 '통신 끊김' 으로 두기 위해서다.
/// </summary>
[ApiController]
[Route("api/iot/zigbee")]
[Authorize(Policy = "ViewField")]
public class IotController : ControllerBase
{
    private const int MaxHistory = 1000;

    private readonly ZigbeeBridgeClient _bridge;
    private readonly ZigbeeOptions _options;

    public IotController(ZigbeeBridgeClient bridge, IOptions<ZigbeeOptions> options)
    {
        _bridge = bridge;
        _options = options.Value;
    }

    /// <summary>센서 전부의 최신 값 + 수집 계통 상태. 화면이 몇 초마다 이것만 부른다.</summary>
    [HttpGet("latest")]
    public async Task<ActionResult<SensorSnapshotDto>> Latest(CancellationToken ct)
    {
        var now = DateTime.Now;
        IReadOnlyList<ZigbeeBridgeClient.RawReading> raw = [];
        var bridgeOnline = true;
        bool? mqtt = null;
        string? message = null;

        try
        {
            raw = await _bridge.LatestAsync(ct);
            (_, mqtt) = await _bridge.StatusAsync(ct);
        }
        catch (ZigbeeBridgeUnavailableException ex)
        {
            bridgeOnline = false;
            message = ex.Message;
        }

        var sensors = Merge(raw, now);
        var online = sensors.Count(s => s.Status != "offline");
        return Ok(new SensorSnapshotDto(
            sensors, new ZigbeeStatusDto(bridgeOnline, mqtt, online, sensors.Count, message)));
    }

    /// <summary>센서 한 대의 최신 값.</summary>
    [HttpGet("latest/{deviceId}")]
    public async Task<ActionResult<SensorReadingDto>> Latest(string deviceId, CancellationToken ct)
    {
        var now = DateTime.Now;
        var known = _options.Sensors.FirstOrDefault(s => s.DeviceId == deviceId);
        try
        {
            var raw = await _bridge.LatestAsync(deviceId, ct);
            return Ok(ZigbeeMapping.ToDto(deviceId, Name(known, raw?.DeviceName, deviceId), known?.Site ?? "", raw, now, _options));
        }
        catch (ZigbeeBridgeUnavailableException)
        {
            return Ok(ZigbeeMapping.ToDto(deviceId, Name(known, null, deviceId), known?.Site ?? "", null, now, _options));
        }
    }

    /// <summary>센서 한 대의 이력. 그래프가 쓴다. 브리지가 멎어 있으면 빈 목록이다.</summary>
    [HttpGet("history/{deviceId}")]
    public async Task<ActionResult<SensorHistoryDto>> History(string deviceId, [FromQuery] int limit, CancellationToken ct)
    {
        var take = limit <= 0 ? 100 : Math.Min(limit, MaxHistory);
        var known = _options.Sensors.FirstOrDefault(s => s.DeviceId == deviceId);
        try
        {
            var raw = await _bridge.HistoryAsync(deviceId, take, ct);
            var points = raw
                .Where(r => r.ReceivedAt is not null)
                .OrderBy(r => r.ReceivedAt)
                .Select(r => new SensorHistoryPointDto(r.ReceivedAt!.Value, r.Temperature, r.Humidity))
                .ToList();
            var name = Name(known, raw.FirstOrDefault().DeviceName, deviceId);
            return Ok(new SensorHistoryDto(deviceId, name, points));
        }
        catch (ZigbeeBridgeUnavailableException)
        {
            return Ok(new SensorHistoryDto(deviceId, Name(known, null, deviceId), []));
        }
    }

    /// <summary>수집 계통 상태만. 화면 오른쪽 위의 작은 표시가 쓴다.</summary>
    [HttpGet("status")]
    public async Task<ActionResult<ZigbeeStatusDto>> Status(CancellationToken ct)
    {
        var now = DateTime.Now;
        try
        {
            var (_, mqtt) = await _bridge.StatusAsync(ct);
            var sensors = Merge(await _bridge.LatestAsync(ct), now);
            return Ok(new ZigbeeStatusDto(true, mqtt, sensors.Count(s => s.Status != "offline"), sensors.Count, null));
        }
        catch (ZigbeeBridgeUnavailableException ex)
        {
            return Ok(new ZigbeeStatusDto(false, null, 0, _options.Sensors.Count, ex.Message));
        }
    }

    // ── 내부 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 설정에 적어 둔 센서를 기준으로 삼되, 브리지가 알려 준 낯선 센서도 뒤에 붙인다.
    /// 설정에만 있는 센서는 값이 없어도 칸이 남아 '통신 끊김' 으로 보인다 — 센서가 죽은 것을 알아야 하니까.
    /// </summary>
    private List<SensorReadingDto> Merge(IReadOnlyList<ZigbeeBridgeClient.RawReading> raw, DateTime now)
    {
        var byId = new Dictionary<string, ZigbeeBridgeClient.RawReading>();
        foreach (var r in raw) byId[r.DeviceId] = r;

        var list = new List<SensorReadingDto>();
        foreach (var known in _options.Sensors)
        {
            ZigbeeBridgeClient.RawReading? found = byId.Remove(known.DeviceId, out var hit) ? hit : null;
            list.Add(ZigbeeMapping.ToDto(
                known.DeviceId, Name(known, found?.DeviceName, known.DeviceId), known.Site, found, now, _options));
        }

        // 설정에 없는데 값이 들어오는 센서 — 새로 붙였는데 아직 등록 전인 경우다. 숨기지 않는다.
        foreach (var left in byId.Values)
            list.Add(ZigbeeMapping.ToDto(left.DeviceId, left.DeviceName ?? left.DeviceId, "", left, now, _options));

        return list;
    }

    private static string Name(ZigbeeSensorOptions? known, string? fromBridge, string deviceId)
        => !string.IsNullOrWhiteSpace(known?.Name) ? known!.Name
         : !string.IsNullOrWhiteSpace(fromBridge) ? fromBridge!
         : deviceId;
}
