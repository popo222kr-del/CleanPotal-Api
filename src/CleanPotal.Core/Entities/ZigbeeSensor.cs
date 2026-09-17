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
/// 이력 한 줄.
///
/// 두 가지가 섞여 있다.
/// - <b>실제 수신</b>(IsSnapshot=false): 센서가 올려 보낸 값. 값이 바뀌었거나 정해 둔 간격이 지났을 때만 남긴다.
/// - <b>주기 기록</b>(IsSnapshot=true): 센서가 조용해도 그래프가 끊기지 않게, 마지막으로 받은 값을
///   정해진 주기마다 한 줄씩 적어 둔 것. 그 시각에 새로 잰 값이 아니다.
///
/// 둘을 구분해 두는 이유는 "언제 실제로 받았는가" 가 통신 끊김 판정의 근거이기 때문이다.
/// 주기 기록까지 수신으로 치면 죽은 센서도 살아 있는 것처럼 보인다.
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

    /// <summary>주기 기록이면 true. 실제로 센서가 보낸 줄은 false 다.</summary>
    public bool IsSnapshot { get; set; }
}
