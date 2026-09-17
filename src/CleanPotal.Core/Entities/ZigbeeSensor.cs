namespace CleanPotal.Core.Entities;

/// <summary>
/// 온·습도 센서 마스터. Zigbee2MQTT 의 friendly name(dongtan_1 …)과 화면에 쓸 이름을 잇는다.
///
/// 센서를 코드가 아니라 표에 두는 이유는 사업장이 늘기 때문이다 — 천안·본사가 붙어도
/// 여기 줄만 생기면 카드·그래프·이력이 따라온다.
/// </summary>
public class ZigbeeSensor
{
    public int Id { get; set; }

    /// <summary>MQTT friendly name. 토픽 zigbee2mqtt/{DeviceId} 의 뒷부분과 같다.</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>사업장(동탄·천안·본사 …).</summary>
    public string Site { get; set; } = "";

    /// <summary>화면에 쓰는 이름(동탄창고 1번).</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>끄면 화면에서 감춘다. 지우지 않는 이유는 이력이 남아 있기 때문이다.</summary>
    public bool IsEnabled { get; set; } = true;

    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 센서가 올려 보낸 값 한 줄. MQTT 메시지가 올 때마다 전부 쌓지는 않는다
/// — 값이 바뀌었거나 마지막 저장에서 정해 둔 시간이 지났을 때만 남긴다(ZigbeeOptions.MinSaveIntervalSeconds).
/// </summary>
public class ZigbeeReading
{
    public int Id { get; set; }
    public string DeviceId { get; set; } = "";
    public double? Temperature { get; set; }
    public double? Humidity { get; set; }
    public int? Battery { get; set; }
    public int? LinkQuality { get; set; }
    public DateTime ReceivedAt { get; set; }
}
