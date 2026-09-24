using CleanPotal.Core.Entities;
using CleanPotal.Core.Iot;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 센서가 조용해도 이력이 끊기지 않게, 마지막으로 받은 값을 정해진 주기마다 한 줄씩 적어 둔다.
///
/// 왜 필요한가: SNZB-02D 는 값이 안 변하면 한 시간에 한 번만 보고한다. 그대로 두면 그래프에 점이
/// 한 시간에 하나뿐이라 "그동안 창고가 어땠는지" 를 읽을 수 없다. 센서 보고 주기를 줄이면 촘촘해지지만
/// 배터리가 몇 달 만에 닳는다. 그래서 센서는 그대로 두고 포털이 적는다.
///
/// 적는 줄에는 <see cref="ZigbeeReading.IsSnapshot"/> 을 세워 둔다. 그 시각에 새로 잰 값이 아니라
/// 마지막 측정값의 반복이기 때문이다 — 통신 끊김 판정은 실제 수신만 보고 한다.
///
/// 마지막 실제 수신이 너무 오래됐으면 멈춘다. 죽은 센서의 값을 끝없이 베껴 적으면 그래프가 거짓말을 한다.
/// </summary>
public class ZigbeeSnapshotService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ZigbeeSensorStore _store;
    private readonly ZigbeeOptions _options;
    private readonly ILogger<ZigbeeSnapshotService> _log;

    public ZigbeeSnapshotService(
        IServiceScopeFactory scopes, ZigbeeSensorStore store,
        IOptions<ZigbeeOptions> options, ILogger<ZigbeeSnapshotService> log)
    {
        _scopes = scopes;
        _store = store;
        _options = options.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 표가 준비되고 MQTT 가 한 번 붙을 틈을 준다.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // 여기서 잡지 않으면 호스트가 통째로 내려간다. 한 번 못 적어도 다음 차례에 적으면 된다.
                _log.LogWarning(ex, "[zigbee] 주기 기록 실패");
            }

            // 주기를 바꿔도 다음 차례부터 따라오도록 매번 30초씩만 잔다.
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CleanPotalDbContext>();

        var rows = (await db.ZigbeeThresholds.AsNoTracking().ToListAsync(ct))
            .Select(t => new ZigbeeLimitResolver.Row(
                t.Scope, t.ScopeKey,
                t.TempNormalMin, t.TempNormalMax, t.TempWarnMin, t.TempWarnMax,
                t.HumidNormalMin, t.HumidNormalMax, t.HumidWarnMin, t.HumidWarnMax,
                t.OfflineAfterMinutes, t.LowBatteryPercent, t.SnapshotIntervalMinutes))
            .ToList();

        var minutes = ZigbeeLimitResolver.SnapshotMinutes(rows, _options);
        if (minutes <= 0) return;                       // 꺼 둔 상태

        var now = DateTime.Now;
        var interval = TimeSpan.FromMinutes(minutes);
        var maxAge = TimeSpan.FromMinutes(Math.Max(1, _options.SnapshotMaxAgeMinutes));

        var sensors = await db.ZigbeeSensors.AsNoTracking().Where(s => s.IsEnabled).ToListAsync(ct);
        if (sensors.Count == 0) return;

        // 센서별 마지막 줄의 시각 — 실제 수신이든 주기 기록이든 마지막으로 적은 때다.
        // 센서마다 따로 묻는다: (DeviceId, ReceivedAt) 인덱스를 한 번씩만 짚으면 된다. 예전의 GroupBy 는
        // 30초마다 이력 인덱스 전체를 훑어, 이력이 쌓일수록 느려졌다.
        var lastWritten = new Dictionary<string, DateTime>();
        foreach (var s in sensors)
        {
            var id = s.DeviceId;
            var at = await db.ZigbeeReadings.AsNoTracking()
                .Where(r => r.DeviceId == id)
                .MaxAsync(r => (DateTime?)r.ReceivedAt, ct);
            if (at is { } v) lastWritten[id] = v;
        }

        var added = 0;
        foreach (var s in sensors)
        {
            if (_store.Get(s.DeviceId) is not { } live) continue;        // 아직 한 번도 못 받았다
            if (now - live.ReceivedAt > maxAge) continue;                // 너무 오래 조용하다 — 베껴 적지 않는다
            if (lastWritten.TryGetValue(s.DeviceId, out var at) && now - at < interval) continue;

            db.ZigbeeReadings.Add(new ZigbeeReading
            {
                DeviceId = s.DeviceId,
                Temperature = live.Temperature,
                Humidity = live.Humidity,
                Battery = live.Battery,
                LinkQuality = live.LinkQuality,
                ReceivedAt = now,
                IsSnapshot = true,
            });
            added++;
        }

        if (added > 0) await db.SaveChangesAsync(ct);

        await PurgeOldSnapshotsAsync(db, now, ct);
    }

    private DateTime _lastPurge = DateTime.MinValue;

    /// <summary>
    /// 오래된 주기 기록을 하루 한 번 지운다(설정 SnapshotRetentionDays, 0 이면 끔). 주기 기록은 센서마다
    /// 1분에 한 줄이라 가장 빨리 쌓인다. 실제 수신 줄은 품질 기록이라 지우지 않는다.
    /// </summary>
    private async Task PurgeOldSnapshotsAsync(CleanPotalDbContext db, DateTime now, CancellationToken ct)
    {
        var days = _options.SnapshotRetentionDays;
        if (days <= 0 || now - _lastPurge < TimeSpan.FromDays(1)) return;
        _lastPurge = now;
        var cutoff = now.AddDays(-days);
        // 처음 켤 때 몇 달 치가 쌓여 있으면 한 번에 지우다 시간 초과로 매일 실패한다 — 하루치씩 지운다.
        var removed = 0;
        var oldest = await db.ZigbeeReadings.Where(r => r.IsSnapshot && r.ReceivedAt < cutoff)
            .MinAsync(r => (DateTime?)r.ReceivedAt, ct);
        for (var upTo = oldest?.Date.AddDays(1); upTo is not null; upTo = upTo.Value.AddDays(1))
        {
            var bound = upTo.Value < cutoff ? upTo.Value : cutoff;
            removed += await db.ZigbeeReadings.Where(r => r.IsSnapshot && r.ReceivedAt < bound).ExecuteDeleteAsync(ct);
            if (bound >= cutoff) break;
        }
        if (removed > 0)
            _log.LogInformation("[zigbee] {Days}일 지난 주기 기록 {Count}줄을 정리했습니다.", days, removed);
    }
}
