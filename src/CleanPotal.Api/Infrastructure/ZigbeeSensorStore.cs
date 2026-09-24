using System.Collections.Concurrent;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Iot;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 지금 이 순간의 센서 값과 수집 계통 상태. 메모리에만 있다.
///
/// 화면은 몇 초마다 "지금 값" 을 묻는데, 그때마다 표를 뒤지면 이력이 쌓일수록 느려진다.
/// 최신값은 어차피 한 센서에 한 줄이라 여기 두고, 표는 그래프·이력이 읽는다.
/// 서버를 다시 켜면 비지만, 센서가 다음 값을 올리면 바로 채워지고 그전까지는 표의 마지막 줄로 메운다.
/// </summary>
public class ZigbeeSensorStore
{
    /// <summary>센서가 마지막으로 올린 값.</summary>
    public readonly record struct Live(
        double? Temperature, double? Humidity, int? Battery, int? LinkQuality, DateTime ReceivedAt);

    private readonly ConcurrentDictionary<string, Live> _latest = new();

    /// <summary>브로커에 붙어 있는지. 붙어 있지 않으면 값이 안 들어오는 것이 당연하다.</summary>
    public volatile bool MqttConnected;

    /// <summary>
    /// bridge/state 토픽이 알려 준 값. 이 토픽은 Z2M 이 켜질 때 한 번만 나오므로 그 순간을 놓치면 계속 null 이다.
    /// 그래서 이것만 믿지 않고 <see cref="Zigbee2MqttSeenAt"/> 을 같이 본다.
    /// </summary>
    public bool? Zigbee2MqttState { get; set; }

    /// <summary>
    /// Z2M 이 무엇이든 마지막으로 보낸 때. 센서 값이든 bridge/health 든, 뭔가 왔다는 것은 살아 있다는 뜻이다.
    /// 한 번만 오는 신호에 기대지 않고 이 시각으로 살아 있음을 판단한다.
    /// </summary>
    public DateTime? Zigbee2MqttSeenAt { get; set; }

    /// <summary>
    /// Z2M 이 살아 있는가. 모르면 null(화면은 '확인 중').
    ///
    /// 마지막으로 뭔가 온 때 하나만 본다 — bridge/health, 센서 값, 그 무엇이든 정해 둔 시간
    /// 안에 왔으면 살아 있는 것이다. bridge/state 가 offline 을 알리면(ZigbeeMqttService 가)
    /// 이 시각을 아주 옛날로 되돌려 두므로, 그 뒤 아무 메시지나 한 번 더 오기 전까지는
    /// 자동으로 끊김으로 보인다 — 그리고 실제로 뭔가 오면 바로 되살아난다.
    ///
    /// 예전에는 bridge/state==offline 을 다른 무엇보다 먼저, 무조건 따랐다. 그래서 한 번
    /// 오프라인을 알린 뒤 다시 붙어 센서 값이 멀쩡히 들어와도 이 표시만 영원히 회색이었다.
    /// </summary>
    public bool? Zigbee2MqttAlive(DateTime now, int silentMinutes)
    {
        if (Zigbee2MqttSeenAt is not { } seen) return Zigbee2MqttState;
        return now - seen <= TimeSpan.FromMinutes(Math.Max(1, silentMinutes));
    }

    /// <summary>마지막으로 막힌 이유. 화면 오른쪽 위 표시의 설명이다.</summary>
    public string? LastError { get; set; }

    public void Set(string deviceId, Live live) => _latest[deviceId] = live;

    public Live? Get(string deviceId) => _latest.TryGetValue(deviceId, out var v) ? v : null;

    /// <summary>표에서 읽어 온 마지막 줄로 메운다. 이미 더 새 값이 있으면 그대로 둔다.</summary>
    public void SeedIfEmpty(string deviceId, Live live)
        => _latest.AddOrUpdate(deviceId, live, (_, cur) => cur.ReceivedAt >= live.ReceivedAt ? cur : live);
}

/// <summary>저장한 값을 화면이 쓰는 모양으로 옮긴다.</summary>
public static class ZigbeeMapping
{
    public static SensorReadingDto ToDto(
        string deviceId, string deviceName, string site,
        ZigbeeSensorStore.Live? live, DateTime now, ZigbeeLimits limits)
    {
        var verdict = SensorStatusEvaluator.Evaluate(
            live?.Temperature, live?.Humidity, live?.ReceivedAt, now, limits);
        var battery = live?.Battery;
        return new SensorReadingDto(
            deviceId, deviceName, site,
            live?.Temperature, live?.Humidity, battery, live?.LinkQuality, live?.ReceivedAt,
            Code(verdict.Status), SensorStatusEvaluator.Label(verdict.Status), verdict.Reason,
            battery is { } b && b <= limits.LowBatteryPercent, limits.Source);
    }

    public static string Code(SensorStatus status) => status switch
    {
        SensorStatus.Normal => "normal",
        SensorStatus.Warning => "warn",
        SensorStatus.Alert => "alert",
        _ => "offline",
    };
}
