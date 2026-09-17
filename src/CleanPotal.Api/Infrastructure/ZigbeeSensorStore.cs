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
    /// bridge/state 가 offline 이라고 했으면 그 말을 따른다 — Z2M 이 스스로 내려간다고 알린 것이다.
    /// 그 밖에는 마지막으로 뭔가 온 때로 판단한다. Z2M 은 bridge/health 를 10분마다 보내고 센서도
    /// 그 사이에 값을 올리므로, 정해 둔 시간 동안 아무 소식이 없으면 멎은 것으로 본다.
    /// </summary>
    public bool? Zigbee2MqttAlive(DateTime now, int silentMinutes)
    {
        if (Zigbee2MqttState == false) return false;
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
