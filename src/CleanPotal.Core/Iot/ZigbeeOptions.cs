namespace CleanPotal.Core.Iot;

/// <summary>
/// Zigbee 온·습도 수집 설정. appsettings 의 "Zigbee" 구역을 그대로 받는다.
///
/// 브리지 주소·판정 기준·센서 목록을 한곳에 모아 둔다. 창고가 늘어나면(천안·본사 …) 센서를 이 목록에
/// 추가하기만 하면 되고, 브리지가 다른 PC 로 옮겨 가면 BridgeUrl 한 줄만 바꾸면 된다. 코드 어디에도
/// 127.0.0.1:5002 나 dongtan_1 을 박아 두지 않는 이유다.
///
/// 판정 기준은 나중에 관리자 화면에서 바꿀 수 있게 값으로만 두었다 — 규칙은 SensorStatusEvaluator 에 있고
/// 여기에는 숫자만 있다.
/// </summary>
public sealed class ZigbeeOptions
{
    public const string SectionName = "Zigbee";

    /// <summary>AETS Zigbee Bridge(Flask) 주소. 포털만 이 주소를 알고, 브라우저에는 노출하지 않는다.</summary>
    public string BridgeUrl { get; set; } = "http://127.0.0.1:5002";

    /// <summary>브리지 호출 제한 시간(초). 브리지가 멎어도 화면이 오래 붙잡히지 않게 짧게 둔다.</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>이 시간(분) 넘게 새 값이 없으면 통신 끊김으로 본다.</summary>
    public int OfflineAfterMinutes { get; set; } = 5;

    /// <summary>이 값(%) 이하이면 배터리 부족으로 표시한다.</summary>
    public int LowBatteryPercent { get; set; } = 20;

    public ZigbeeBand Temperature { get; set; } = new() { NormalMin = 18, NormalMax = 28, WarnMin = 15, WarnMax = 30 };
    public ZigbeeBand Humidity { get; set; } = new() { NormalMin = 40, NormalMax = 60, WarnMin = 30, WarnMax = 70 };

    /// <summary>화면에 보여 줄 센서. 여기 있는데 값이 안 들어오면 '통신 끊김' 칸으로 남는다.</summary>
    public List<ZigbeeSensorOptions> Sensors { get; set; } = [];
}

/// <summary>
/// 정상 구간과 그 바깥의 주의 구간. 주의 구간마저 벗어나면 경고다.
/// 예) 온도 NormalMin 18 · NormalMax 28 · WarnMin 15 · WarnMax 30
///     → 18~28 정상, 15~18 과 28~30 주의, 15 미만과 30 초과 경고.
/// </summary>
public sealed class ZigbeeBand
{
    public double NormalMin { get; set; }
    public double NormalMax { get; set; }
    public double WarnMin { get; set; }
    public double WarnMax { get; set; }
}

/// <summary>센서 한 대. DeviceId 는 Zigbee2MQTT 에서 붙인 이름과 같아야 한다.</summary>
public sealed class ZigbeeSensorOptions
{
    public string DeviceId { get; set; } = "";
    /// <summary>사업장. 창고가 늘어나면 이 값으로 묶어 보여 준다.</summary>
    public string Site { get; set; } = "";
    /// <summary>화면에 쓰는 이름. 비어 있으면 브리지가 준 이름을 쓴다.</summary>
    public string Name { get; set; } = "";
}
