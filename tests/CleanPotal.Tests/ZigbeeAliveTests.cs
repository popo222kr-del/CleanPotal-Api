using CleanPotal.Api.Infrastructure;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// Zigbee2MQTT 가 살아 있는지 판단하는 규칙.
///
/// bridge/state 는 Z2M 이 켜질 때 한 번만 나온다. 그것만 믿으면 그 순간을 놓쳤을 때 영영 '확인 중' 에
/// 머문다(실제로 그랬다). 그래서 "마지막으로 뭔가 온 때" 로도 판단한다.
/// </summary>
public class ZigbeeAliveTests
{
    private static readonly DateTime Now = new(2026, 9, 17, 16, 30, 0);
    private const int Silent = 30;

    [Fact]
    public void 아무것도_못_받았으면_모른다()
        => Assert.Null(new ZigbeeSensorStore().Zigbee2MqttAlive(Now, Silent));

    [Fact]
    public void 최근에_무엇이든_왔으면_살아_있다()
    {
        var store = new ZigbeeSensorStore { Zigbee2MqttSeenAt = Now.AddMinutes(-12) };
        Assert.True(store.Zigbee2MqttAlive(Now, Silent));
    }

    [Fact]
    public void 정해둔_시간_넘게_소식이_없으면_멎은_것으로_본다()
    {
        var store = new ZigbeeSensorStore { Zigbee2MqttSeenAt = Now.AddMinutes(-31) };
        Assert.False(store.Zigbee2MqttAlive(Now, Silent));
    }

    [Fact]
    public void 스스로_내려간다고_알렸으면_그_말을_따른다()
    {
        // 방금 offline 메시지가 왔으므로 '최근에 뭔가 왔다' 는 조건은 맞지만, 내용이 우선이다.
        var store = new ZigbeeSensorStore { Zigbee2MqttState = false, Zigbee2MqttSeenAt = Now };
        Assert.False(store.Zigbee2MqttAlive(Now, Silent));
    }

    [Fact]
    public void online_을_받았고_소식도_끊기지_않았으면_살아_있다()
    {
        var store = new ZigbeeSensorStore { Zigbee2MqttState = true, Zigbee2MqttSeenAt = Now.AddMinutes(-5) };
        Assert.True(store.Zigbee2MqttAlive(Now, Silent));
    }
}
