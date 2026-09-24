using System.Text.Json;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Iot;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MQTTnet;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// Mosquitto 에 직접 붙어 창고 센서 값을 받는다. 중간에 다른 프로그램을 두지 않는다.
///
///   SNZB-02D → ZBDongle-P → Zigbee2MQTT → Mosquitto(1883) → 여기 → DB · 화면
///
/// 세 가지를 지킨다.
/// 1. <b>포털을 죽이지 않는다.</b> 브로커가 없어도, 꺼져도, 메시지가 이상해도 이 서비스 안에서 끝낸다.
///    온·습도는 곁다리 기능이라, 이것 때문에 인수인계나 MES 가 멈추면 안 된다.
/// 2. <b>다시 붙는다.</b> 끊기면 정해진 간격으로 계속 다시 시도한다(브로커를 껐다 켜도 손댈 일이 없다).
/// 3. <b>표를 함부로 불리지 않는다.</b> 값이 바뀌었거나 정해 둔 간격이 지났을 때만 이력을 남긴다.
/// </summary>
public class ZigbeeMqttService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ZigbeeSensorStore _store;
    private readonly ZigbeeOptions _options;
    private readonly ILogger<ZigbeeMqttService> _log;

    /// <summary>센서별 마지막으로 표에 남긴 값. ZigbeeSavePolicy 가 이것을 보고 저장 여부를 정한다.</summary>
    private readonly Dictionary<string, ZigbeeSavePolicy.Saved> _lastSaved = new();

    /// <summary>
    /// 화면에 보일 센서. 표에 없는 센서의 메시지는 버린다.
    /// 시작할 때 한 번 읽고, 그 뒤로도 <see cref="KnownReloadInterval"/> 마다 다시 읽는다 —
    /// 처음 켜졌을 때 DB에 아직 안 올라와 있던 센서나, 사용 안 함으로 꺼져 있다 나중에
    /// 다시 켠 센서가 포털을 재시작해야만 잡히는 일이 없게 한다.
    /// </summary>
    private Dictionary<string, ZigbeeSensor> _known = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _knownLoadedAt = DateTime.MinValue;
    private static readonly TimeSpan KnownReloadInterval = TimeSpan.FromMinutes(5);

    public ZigbeeMqttService(
        IServiceScopeFactory scopes, ZigbeeSensorStore store,
        IOptions<ZigbeeOptions> options, ILogger<ZigbeeMqttService> log)
    {
        _scopes = scopes;
        _store = store;
        _options = options.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Mqtt.Enabled)
        {
            _log.LogInformation("[zigbee] 구독을 끈 상태입니다(Zigbee:Mqtt:Enabled=false).");
            _store.LastError = "MQTT 구독이 꺼져 있습니다.";
            return;
        }

        // 표가 준비되는 데 시간이 걸린다(EnsureCreated·SchemaUpgrader 는 앱 시작 뒤에 돈다).
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken).ConfigureAwait(false);
        await LoadSensorsAsync(stoppingToken).ConfigureAwait(false);

        var delay = TimeSpan.FromSeconds(Math.Clamp(_options.Mqtt.ReconnectSeconds, 3, 300));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 여기서 잡지 않으면 호스트가 통째로 내려간다. 사유만 남기고 다시 붙는다.
                _store.MqttConnected = false;
                _store.LastError = "MQTT 브로커에 연결하지 못했습니다.";
                _log.LogWarning(ex, "[zigbee] MQTT 연결 실패 — {Delay}초 뒤 다시 시도합니다.", delay.TotalSeconds);
            }

            try { await Task.Delay(delay, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }

        _store.MqttConnected = false;
    }

    /// <summary>한 번 붙어서 끊길 때까지 받는다. 끊기면 돌아가고, 바깥 고리가 다시 부른다.</summary>
    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var client = new MqttClientFactory().CreateMqttClient();

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Mqtt.Host, _options.Mqtt.Port)
            .WithClientId($"{_options.Mqtt.ClientId}-{Environment.MachineName}")
            .WithCleanSession();
        if (!string.IsNullOrWhiteSpace(_options.Mqtt.Username))
            builder = builder.WithCredentials(_options.Mqtt.Username, _options.Mqtt.Password);

        client.ApplicationMessageReceivedAsync += async e =>
        {
            try { await HandleAsync(e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString(), ct).ConfigureAwait(false); }
            catch (Exception ex) { _log.LogWarning(ex, "[zigbee] 메시지 처리 실패 — {Topic}", e.ApplicationMessage.Topic); }
        };

        await client.ConnectAsync(builder.Build(), ct).ConfigureAwait(false);

        var prefix = _options.Mqtt.TopicPrefix.Trim('/');
        await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic($"{prefix}/+"))
            .WithTopicFilter(f => f.WithTopic($"{prefix}/bridge/state"))
            // bridge/state 는 Z2M 이 켜질 때 한 번만 나온다. 그 순간을 놓쳐도 알 수 있게 10분마다 나오는
            // bridge/health 도 같이 듣는다 — Z2M 을 재시작하지 않아도 표시가 스스로 맞춰진다.
            .WithTopicFilter(f => f.WithTopic($"{prefix}/bridge/health"))
            .Build(), ct).ConfigureAwait(false);

        _store.MqttConnected = true;
        _store.LastError = null;
        _log.LogInformation("[zigbee] MQTT 구독 시작 — {Host}:{Port} / {Prefix}/+", _options.Mqtt.Host, _options.Mqtt.Port, prefix);

        // 연결이 살아 있는 동안 여기서 기다린다. 끊기면 IsConnected 가 내려가고 다시 붙는다.
        // 기다리는 김에 센서 목록도 주기적으로 다시 읽는다 — 재연결·재시작을 기다리지 않아도 된다.
        while (!ct.IsCancellationRequested && client.IsConnected)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            if (DateTime.Now - _knownLoadedAt >= KnownReloadInterval)
                await LoadSensorsAsync(ct).ConfigureAwait(false);
        }

        _store.MqttConnected = false;
        if (!ct.IsCancellationRequested)
        {
            _store.LastError = "MQTT 연결이 끊어졌습니다.";
            _log.LogWarning("[zigbee] MQTT 연결이 끊어졌습니다 — 다시 붙습니다.");
        }

        try { await client.DisconnectAsync(new MqttClientDisconnectOptions(), CancellationToken.None).ConfigureAwait(false); }
        catch { /* 이미 끊긴 뒤라 실패해도 상관없다 */ }
    }

    // ── 메시지 처리 ───────────────────────────────────────────────────────

    private async Task HandleAsync(string topic, string payload, CancellationToken ct)
    {
        var prefix = _options.Mqtt.TopicPrefix.Trim('/');
        if (!topic.StartsWith($"{prefix}/", StringComparison.OrdinalIgnoreCase)) return;

        if (topic.Equals($"{prefix}/bridge/state", StringComparison.OrdinalIgnoreCase))
        {
            var state = ParseBridgeState(payload);
            _store.Zigbee2MqttState = state;
            // 온라인이면 이 메시지 자체가 살아 있다는 증거다. 오프라인('떠난다')이면 마지막
            // 수신 시각을 아주 옛날로 되돌려 즉시 끊김으로 보이게 한다.
            //
            // 예전에는 여기서도 Zigbee2MqttSeenAt 을 '지금'으로 찍었다. Z2M 이 한 번 오프라인을
            // 알린 뒤(예: 재연결 중 잠깐의 LWT) 다시 살아나 센서 값이 멀쩡히 들어와도,
            // Zigbee2MqttAlive 가 State==false 를 최우선으로 보느라 화면은 영원히 회색으로
            // 굳어 있었다 — 정작 센서 값은 잘 들어오는데 표시만 안 바뀌던 것이 이 버그다.
            // 이제는 이 옛 선언을 '가장 오래된 값' 으로 취급해, 그 뒤 아무 메시지나 한 번만
            // 더 오면(진짜로 살아 있다는 증거) 자동으로 되살아난다.
            _store.Zigbee2MqttSeenAt = state == false ? DateTime.MinValue : DateTime.Now;
            return;
        }

        // 무엇이든 왔다는 것은 Z2M 이 살아 있다는 뜻이다. 한 번만 오는 신호에 기대지 않는 근거다.
        _store.Zigbee2MqttSeenAt = DateTime.Now;

        // bridge/health 는 내용까지 볼 필요가 없다 — 왔다는 사실만으로 살아 있음이 증명된다.
        if (topic.StartsWith($"{prefix}/bridge/", StringComparison.OrdinalIgnoreCase)) return;

        var deviceId = topic[(prefix.Length + 1)..];

        // bridge/* 같은 관리 토픽과 등록되지 않은 장치는 버린다.
        if (deviceId.Contains('/') || !_known.ContainsKey(deviceId)) return;

        var reading = ParseReading(payload);
        if (reading is null) return;

        var now = DateTime.Now;
        var live = reading.Value with { ReceivedAt = now };
        _store.Set(deviceId, live);

        var last = _lastSaved.TryGetValue(deviceId, out var prev) ? prev : (ZigbeeSavePolicy.Saved?)null;
        if (ZigbeeSavePolicy.ShouldSave(last, live.Temperature, live.Humidity, now, _options.MinSaveIntervalSeconds))
            await SaveAsync(deviceId, live, ct).ConfigureAwait(false);
    }

    /// <summary>{"state":"online"} 또는 그냥 "online". 둘 다 온다.</summary>
    private static bool? ParseBridgeState(string payload)
    {
        var text = payload.Trim();
        if (text.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("state", out var v) && v.ValueKind == JsonValueKind.String)
                    text = v.GetString() ?? "";
            }
            catch (JsonException) { return null; }
        }
        if (text.Equals("online", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Equals("offline", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    /// <summary>
    /// SNZB-02D 가 올리는 JSON 에서 쓸 값만 뽑는다. 나머지 필드(보정값·OTA 상태 …)는 화면이 쓰지 않는다.
    /// 온도·습도가 둘 다 없으면 센서 값이 아니다(설정 응답 같은 것) — 버린다.
    /// </summary>
    private static ZigbeeSensorStore.Live? ParseReading(string payload)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(payload); }
        catch (JsonException) { return null; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var temp = Num(root, "temperature");
            var humid = Num(root, "humidity");
            if (temp is null && humid is null) return null;

            return new ZigbeeSensorStore.Live(
                temp, humid, (int?)Num(root, "battery"), (int?)Num(root, "linkquality"), DateTime.Now);
        }
    }

    private static double? Num(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    private async Task SaveAsync(string deviceId, ZigbeeSensorStore.Live live, CancellationToken ct)
    {
        try
        {
            // 받을 때마다 새 스코프를 연다 — DbContext 는 스레드를 넘겨 쓰면 안 된다.
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CleanPotalDbContext>();
            db.ZigbeeReadings.Add(new ZigbeeReading
            {
                DeviceId = deviceId,
                Temperature = live.Temperature,
                Humidity = live.Humidity,
                Battery = live.Battery,
                LinkQuality = live.LinkQuality,
                ReceivedAt = live.ReceivedAt,
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            _lastSaved[deviceId] = new ZigbeeSavePolicy.Saved(live.Temperature, live.Humidity, live.ReceivedAt);
        }
        catch (Exception ex)
        {
            // 저장이 막혀도 화면의 최신값은 살아 있다. 이력 한 줄을 잃는 편이 낫다.
            _log.LogWarning(ex, "[zigbee] 이력 저장 실패 — {Device}", deviceId);
        }
    }

    // ── 센서 목록 ─────────────────────────────────────────────────────────

    /// <summary>표에서 센서를 읽고, 화면이 바로 뭔가 볼 수 있게 마지막 값으로 메모리를 채운다.</summary>
    private async Task LoadSensorsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CleanPotalDbContext>();
            var rows = await db.ZigbeeSensors.AsNoTracking().Where(s => s.IsEnabled).ToListAsync(ct).ConfigureAwait(false);
            var known = rows.ToDictionary(s => s.DeviceId, s => s, StringComparer.OrdinalIgnoreCase);
            // 통째로 새 사전을 만들어 한 번에 바꿔치기한다 — 메시지 처리 쪽(HandleAsync)이 다른
            // 스레드에서 이 사전을 읽는 동안에도 절반만 채워진 상태를 볼 일이 없다.
            _known = known;
            _knownLoadedAt = DateTime.Now;

            foreach (var s in rows)
            {
                // 주기 기록 줄(IsSnapshot)은 빼고 실제 수신만 본다. 주기 기록 시각을 '최종 수신'으로
                // 되읽으면 배터리가 다 된 센서도 5분마다 살아나고, 주기 기록이 영원히 멈추지 않는다.
                var last = await db.ZigbeeReadings.AsNoTracking()
                    .Where(r => r.DeviceId == s.DeviceId && !r.IsSnapshot)
                    .OrderByDescending(r => r.ReceivedAt)
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (last is null) continue;

                _store.SeedIfEmpty(s.DeviceId, new ZigbeeSensorStore.Live(
                    last.Temperature, last.Humidity, last.Battery, last.LinkQuality, last.ReceivedAt));
                _lastSaved[s.DeviceId] = new ZigbeeSavePolicy.Saved(last.Temperature, last.Humidity, last.ReceivedAt);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[zigbee] 센서 목록을 읽지 못했습니다.");
        }
    }
}
