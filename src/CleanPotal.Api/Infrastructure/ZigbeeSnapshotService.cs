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

        var ids = sensors.Select(s => s.DeviceId).ToList();
        // 센서별 마지막 줄의 시각 — 실제 수신이든 주기 기록이든 마지막으로 적은 때다.
        var lastWritten = await db.ZigbeeReadings.AsNoTracking()
            .Where(r => ids.Contains(r.DeviceId))
            .GroupBy(r => r.DeviceId)
            .Select(g => new { DeviceId = g.Key, At = g.Max(r => r.ReceivedAt) })
            .ToDictionaryAsync(x => x.DeviceId, x => x.At, ct);

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
    }
}
