using System.Text.Json;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Iot;
using Microsoft.Extensions.Options;

namespace CleanPotal.Api.Infrastructure;

/// <summary>브리지에 닿지 못했다. 화면 전체를 오류로 만들지 않고 센서 칸만 '통신 끊김' 으로 보이게 하는 신호다.</summary>
public class ZigbeeBridgeUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// AETS Zigbee Bridge(Flask) 에 묻는 쪽. 브라우저는 이 주소를 모른다 — 포털이 대신 물어보고 정리해서 준다.
/// 그래야 CORS 도, 5002 포트 노출도, 브리지 PC 가 바뀔 때 화면을 고치는 일도 없다.
///
/// 브리지가 주는 값은 snake_case 이고 시간은 "2026-09-17 14:26:02" 처럼 표준 형식이 아니다.
/// 여기서 한 번에 우리 쪽 모양으로 바꾸고, 형태가 조금 달라도 넘어가게 느슨하게 읽는다
/// — 센서 하나의 칸이 비는 것이 화면 전체가 죽는 것보다 낫다.
/// </summary>
public class ZigbeeBridgeClient
{
    private readonly HttpClient _http;
    private readonly ZigbeeOptions _options;
    private readonly ILogger<ZigbeeBridgeClient> _log;

    public ZigbeeBridgeClient(HttpClient http, IOptions<ZigbeeOptions> options, ILogger<ZigbeeBridgeClient> log)
    {
        _http = http;
        _options = options.Value;
        _log = log;
    }

    /// <summary>센서별 최신 값. 설정에 적어 둔 센서는 값이 없어도 자리를 남긴다.</summary>
    public async Task<IReadOnlyList<RawReading>> LatestAsync(CancellationToken ct)
        => ReadArray(await GetAsync("/api/zigbee/latest", ct));

    public async Task<RawReading?> LatestAsync(string deviceId, CancellationToken ct)
    {
        var json = await GetAsync($"/api/zigbee/latest/{Uri.EscapeDataString(deviceId)}", ct);
        // 브리지가 한 건만 줄 수도, 한 건짜리 배열로 줄 수도 있다.
        return json.ValueKind == JsonValueKind.Array ? ReadArray(json).FirstOrDefault() : ReadOne(json);
    }

    public async Task<IReadOnlyList<RawReading>> HistoryAsync(string deviceId, int limit, CancellationToken ct)
        => ReadArray(await GetAsync($"/api/zigbee/history/{Uri.EscapeDataString(deviceId)}?limit={limit}", ct));

    /// <summary>브리지·MQTT 계통 상태. 키 이름이 브리지마다 조금씩 달라 아는 이름을 차례로 찾아본다.</summary>
    public async Task<(bool Bridge, bool? Mqtt)> StatusAsync(CancellationToken ct)
    {
        var json = await GetAsync("/api/zigbee/status", ct);
        if (json.ValueKind != JsonValueKind.Object) return (true, null);
        return (true, Bool(json, "mqtt_connected", "mqtt", "connected", "mqtt_online"));
    }

    private async Task<JsonElement> GetAsync(string path, CancellationToken ct)
    {
        var url = _options.BridgeUrl.TrimEnd('/') + path;
        try
        {
            using var res = await _http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
                throw new ZigbeeBridgeUnavailableException($"Zigbee Bridge 응답 오류({(int)res.StatusCode}).");

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.Clone();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ZigbeeBridgeUnavailableException("Zigbee Bridge 응답이 없습니다(시간 초과).");
        }
        catch (ZigbeeBridgeUnavailableException) { throw; }
        catch (Exception ex)
        {
            // 주소·방화벽·브리지 종료 — 사용자에게는 모두 "연결 실패" 한 가지다. 자세한 것은 로그에만 남긴다.
            _log.LogWarning(ex, "Zigbee Bridge 호출 실패 — {Url}", url);
            throw new ZigbeeBridgeUnavailableException("Zigbee Bridge 에 연결하지 못했습니다.", ex);
        }
    }

    // ── 브리지 JSON 읽기 ──────────────────────────────────────────────────

    /// <summary>브리지가 준 값 한 줄. 여기서는 판정하지 않는다 — 그건 SensorStatusEvaluator 몫이다.</summary>
    public readonly record struct RawReading(
        string DeviceId, string? DeviceName, double? Temperature, double? Humidity,
        int? Battery, int? LinkQuality, DateTime? ReceivedAt);

    private static IReadOnlyList<RawReading> ReadArray(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Array) return [];
        var list = new List<RawReading>();
        foreach (var item in json.EnumerateArray())
        {
            var one = ReadOne(item);
            if (one is { } r) list.Add(r);
        }
        return list;
    }

    private static RawReading? ReadOne(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        var id = Str(item, "device_id", "deviceId", "id");
        if (string.IsNullOrWhiteSpace(id)) return null;
        return new RawReading(
            id,
            Str(item, "device_name", "deviceName", "name"),
            Num(item, "temperature", "temp"),
            Num(item, "humidity", "humi"),
            (int?)Num(item, "battery"),
            (int?)Num(item, "linkquality", "link_quality", "lqi"),
            Time(item, "received_at", "receivedAt", "timestamp", "time"));
    }

    private static string? Str(JsonElement o, params string[] names)
    {
        foreach (var n in names)
            if (o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        return null;
    }

    private static double? Num(JsonElement o, params string[] names)
    {
        foreach (var n in names)
        {
            if (!o.TryGetProperty(n, out var v)) continue;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
            // 브리지가 문자열로 줄 때도 있다("28.5").
            if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out var s)) return s;
        }
        return null;
    }

    private static bool? Bool(JsonElement o, params string[] names)
    {
        foreach (var n in names)
        {
            if (!o.TryGetProperty(n, out var v)) continue;
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
        }
        return null;
    }

    /// <summary>"2026-09-17 14:26:02" 같은 형식. 표준형이 아니어서 직접 읽고, 시간대는 서버 기준으로 본다.</summary>
    private static DateTime? Time(JsonElement o, params string[] names)
    {
        var text = Str(o, names);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeLocal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var utc)
            ? utc.ToLocalTime()
            : null;
    }
}

/// <summary>브리지가 준 값을 화면이 쓰는 모양으로 옮긴다.</summary>
public static class ZigbeeMapping
{
    public static SensorReadingDto ToDto(
        string deviceId, string deviceName, string site,
        ZigbeeBridgeClient.RawReading? raw, DateTime now, ZigbeeOptions options)
    {
        var verdict = SensorStatusEvaluator.Evaluate(raw?.Temperature, raw?.Humidity, raw?.ReceivedAt, now, options);
        var battery = raw?.Battery;
        return new SensorReadingDto(
            deviceId, deviceName, site,
            raw?.Temperature, raw?.Humidity, battery, raw?.LinkQuality, raw?.ReceivedAt,
            Code(verdict.Status), SensorStatusEvaluator.Label(verdict.Status), verdict.Reason,
            battery is { } b && b <= options.LowBatteryPercent);
    }

    public static string Code(SensorStatus status) => status switch
    {
        SensorStatus.Normal => "normal",
        SensorStatus.Warning => "warn",
        SensorStatus.Alert => "alert",
        _ => "offline",
    };
}
