using CleanPotal.Api.Infrastructure;
using CleanPotal.Core.Iot;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>상태 점검(/api/health) — DB·온습도 수집이 살아 있으면 정상, 아니면 무엇이 문제인지.</summary>
public class PortalHealthTests
{
    private static readonly DateTime Now = new(2026, 9, 26, 10, 0, 0);

    [Fact]
    public async Task 수집을_끈_서버는_DB만_보면_정상()
    {
        using var t = new TestDb();
        var r = await PortalHealth.CheckAsync(t.Db, new ZigbeeSensorStore(), new ZigbeeOptions { Mqtt = { Enabled = false } }, Now, default);
        Assert.True(r.Ok);
        Assert.Equal("ok", r.Db);
    }

    [Fact]
    public async Task 브로커에_안_붙었거나_센서_값이_끊기면_문제로_본다()
    {
        using var t = new TestDb();
        var store = new ZigbeeSensorStore();
        var opts = new ZigbeeOptions { Mqtt = { Enabled = true } };

        var r1 = await PortalHealth.CheckAsync(t.Db, store, opts, Now, default);
        Assert.False(r1.Ok);                       // 연결 안 됨

        store.MqttConnected = true;
        store.Set("dongtan_1", new ZigbeeSensorStore.Live(22, 50, 100, 80, Now.AddMinutes(-45)));
        var r2 = await PortalHealth.CheckAsync(t.Db, store, opts, Now, default);
        Assert.False(r2.Ok);                       // 45분째 값 없음
        Assert.Equal(45, r2.LastReadingMinutesAgo);

        store.Set("dongtan_2", new ZigbeeSensorStore.Live(22, 50, 100, 80, Now.AddMinutes(-3)));
        var r3 = await PortalHealth.CheckAsync(t.Db, store, opts, Now, default);
        Assert.True(r3.Ok);
        Assert.Equal(3, r3.LastReadingMinutesAgo);
    }
}
