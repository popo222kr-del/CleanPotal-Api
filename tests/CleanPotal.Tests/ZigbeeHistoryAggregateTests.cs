using CleanPotal.Api.Controllers;
using CleanPotal.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 긴 구간 그래프. 예전에는 오래된 순으로 앞 2만 줄만 읽어 최근 며칠이 표시 없이 빠졌다.
/// 이제 줄이 많으면 DB 에서 시간 칸별 평균을 내므로 구간 끝까지 점이 있어야 한다.
/// </summary>
public class ZigbeeHistoryAggregateTests
{
    [Fact]
    public async Task 구간_끝까지_시간_칸별_평균이_나온다()
    {
        using var t = new TestDb();
        var start = new DateTime(2026, 6, 1);
        var end = start.AddDays(30);
        // 30일 동안 1분마다 = 43,200 줄 (상한 2만을 넘는다)
        var rows = Enumerable.Range(0, 30 * 24 * 60).Select(i => new ZigbeeReading
        {
            DeviceId = "d1", ReceivedAt = start.AddMinutes(i), Temperature = 20 + (i % 2), Humidity = 50,
        }).ToList();
        t.Db.ZigbeeReadings.AddRange(rows);
        await t.Db.SaveChangesAsync();

        var q = t.Db.ZigbeeReadings.AsNoTracking().Where(r => r.DeviceId == "d1" && r.ReceivedAt >= start && r.ReceivedAt <= end);
        Assert.True(await q.CountAsync() > IotController.MaxRawPoints);

        var (points, bucket) = await IotController.AggregateAsync(q, start, end, CancellationToken.None);

        Assert.Equal(60, bucket);                                  // 30일 / 1200점 ≈ 36분 → 한 시간 칸
        Assert.Equal(30 * 24, points.Count);
        Assert.Equal(start, points[0].ReceivedAt);
        Assert.Equal(end.AddHours(-1), points[^1].ReceivedAt);     // 마지막 날까지 빠짐없이
        Assert.All(points, p => Assert.Equal(20.5, p.Temperature));
    }
}
