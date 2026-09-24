namespace CleanPotal.Core.Iot;

/// <summary>
/// Zigbee 온·습도 수집 설정. appsettings 의 "Zigbee" 구역을 그대로 받는다.
///
/// 브로커 주소·판정 기준·센서 목록을 한곳에 모아 둔다. 코드 어디에도 127.0.0.1 이나 dongtan_1 을
/// 박아 두지 않는다.
///
/// Sensors 는 처음 실행할 때 ZigbeeSensors 표로 옮겨 심는다(없는 것만 추가). 그 뒤로는 표가 기준이라
/// 창고가 늘어나면 표에 줄을 넣거나 이 목록에 적고 다시 켜면 된다.
///
/// 판정 기준은 관리자 화면에서 바꿀 수 있다(ZigbeeThresholds 표). 여기 값은 표에 아무것도 없을 때의
/// 기본값이다 — 규칙은 SensorStatusEvaluator 에, 어느 기준을 쓸지는 ZigbeeLimitResolver 에 있다.
/// </summary>
public sealed class ZigbeeOptions
{
    public const string SectionName = "Zigbee";

    /// <summary>Mosquitto(MQTT 브로커) 접속 정보. 포털이 여기에 직접 붙어 센서 값을 받는다.</summary>
    public ZigbeeMqttOptions Mqtt { get; set; } = new();

    /// <summary>이 시간(분) 넘게 새 값이 없으면 통신 끊김으로 본다. 표(ZigbeeThresholds)에 값이 있으면 그쪽이 이긴다.</summary>
    public int OfflineAfterMinutes { get; set; } = 5;

    /// <summary>
    /// Z2M 에서 이 시간(분) 동안 아무 소식이 없으면 멎은 것으로 본다.
    /// Z2M 은 bridge/health 를 10분마다 보내므로 그 두세 배로 잡는다.
    /// </summary>
    public int Zigbee2MqttSilentMinutes { get; set; } = 30;

    /// <summary>이 값(%) 이하이면 배터리 부족으로 표시한다.</summary>
    public int LowBatteryPercent { get; set; } = 20;

    /// <summary>
    /// 이력을 남기는 최소 간격(초). 값이 바뀌지 않았는데도 올라오는 메시지까지 전부 쌓으면
    /// 표가 몇 달 만에 수백만 줄이 된다. 값이 바뀌면 간격과 상관없이 남긴다.
    /// </summary>
    public int MinSaveIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// 센서가 조용해도 마지막 값을 이 간격(분)마다 이력에 한 줄씩 남긴다. 0 이면 끈다.
    /// 관리자 화면에서 바꿀 수 있고(전체 기본 줄), 여기 값은 표에 아무것도 없을 때의 기본값이다.
    /// </summary>
    public int SnapshotIntervalMinutes { get; set; } = 1;

    /// <summary>
    /// 마지막 실제 수신이 이 시간(분)보다 오래됐으면 주기 기록을 멈춘다.
    /// 죽은 센서의 값을 끝없이 베껴 적으면 그래프가 거짓말을 한다.
    /// </summary>
    public int SnapshotMaxAgeMinutes { get; set; } = 120;

    /// <summary>
    /// 주기 기록(IsSnapshot) 줄을 며칠 동안 남길지. 0 이면 지우지 않는다(기본). 실제 수신 줄은 이 설정과 관계없이 남는다.
    /// 주기 기록은 센서마다 1분에 한 줄이라 한 해 수십만 줄이 된다 — 표가 커지면 이 값을 정해 둔다(예: 180).
    /// </summary>
    public int SnapshotRetentionDays { get; set; } = 0;

    public ZigbeeBand Temperature { get; set; } = new() { NormalMin = 18, NormalMax = 28, WarnMin = 15, WarnMax = 30 };
    public ZigbeeBand Humidity { get; set; } = new() { NormalMin = 40, NormalMax = 60, WarnMin = 30, WarnMax = 70 };

    /// <summary>처음 실행 때 표에 심을 센서. 표에 이미 있으면 건드리지 않는다.</summary>
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

/// <summary>
/// Mosquitto 접속. 브로커는 창고 PC 안에서만 열려 있어 보통 그대로 두면 된다.
/// </summary>
public sealed class ZigbeeMqttOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 1883;

    /// <summary>브로커에 보이는 이름. 같은 이름으로 두 번 붙으면 서로를 끊으므로 포털 전용 이름을 쓴다.</summary>
    public string ClientId { get; set; } = "cleanpotal-portal";

    /// <summary>Zigbee2MQTT 의 base_topic. 토픽은 {TopicPrefix}/{friendly name} 이다.</summary>
    public string TopicPrefix { get; set; } = "zigbee2mqtt";

    /// <summary>브로커에 인증을 걸었을 때만 채운다. 값은 appsettings.local.json 에 둔다.</summary>
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>끊겼을 때 다시 붙기까지 기다리는 시간(초).</summary>
    public int ReconnectSeconds { get; set; } = 10;

    /// <summary>false 로 두면 구독을 아예 시작하지 않는다(개발 PC 처럼 브로커가 없는 곳).</summary>
    public bool Enabled { get; set; } = true;
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
