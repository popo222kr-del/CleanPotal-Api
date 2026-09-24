using CleanPotal.Api.Infrastructure;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// Zigbee2MQTT 가 살아 있는지 판단하는 규칙.
///
/// bridge/state 는 Z2M 이 켜질 때 한 번만 나온다. 그것만 믿으면 그 순간을 놓쳤을 때 영영 '확인 중' 에
/// 머문다(실제로 그랬다). 그래서 "마지막으로 뭔가 온 때" 로도 판단한다.
///
/// Zigbee2MqttAlive 자체는 이제 이 마지막 수신 시각 하나만 본다. 'offline 을 알렸으면 그 말을
/// 우선한다' 는 판단은 ZigbeeMqttService 가 offline 수신 시각을 아주 옛날로 되돌려 두는 방식으로
/// 옮겼다 — 예전에는 이 클래스가 State==false 를 무조건 최우선으로 봐서, 한 번 오프라인을 알린 뒤
/// 실제로 센서 값이 계속 들어와도(Zigbee2MqttSeenAt 이 계속 최신으로 바뀌어도) 화면이 영원히
/// 회색으로 굳어 있는 버그가 있었다.
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
    public void 스스로_내려간다고_알린_뒤_아직_아무_소식도_없으면_끊긴_것으로_본다()
    {
        // ZigbeeMqttService 는 offline 을 받으면 수신 시각을 DateTime.MinValue 로 되돌려 둔다.
        var store = new ZigbeeSensorStore { Zigbee2MqttState = false, Zigbee2MqttSeenAt = DateTime.MinValue };
        Assert.False(store.Zigbee2MqttAlive(Now, Silent));
    }

    [Fact]
    public void 한_번_오프라인을_알렸어도_그_뒤에_뭔가_오면_다시_살아난다()
    {
        // 실제로 있었던 버그: 옛날에 받은 offline 선언(State==false)이 그 뒤 계속 들어오는
        // 센서 값(Zigbee2MqttSeenAt 갱신)보다 우선시돼 화면이 영원히 회색이었다.
        // 최근 수신이 있으면 옛 선언보다 그것을 믿어야 한다.
        var store = new ZigbeeSensorStore { Zigbee2MqttState = false, Zigbee2MqttSeenAt = Now.AddMinutes(-1) };
        Assert.True(store.Zigbee2MqttAlive(Now, Silent));
    }

    [Fact]
    public void online_을_받았고_소식도_끊기지_않았으면_살아_있다()
    {
        var store = new ZigbeeSensorStore { Zigbee2MqttState = true, Zigbee2MqttSeenAt = Now.AddMinutes(-5) };
        Assert.True(store.Zigbee2MqttAlive(Now, Silent));
    }
}
