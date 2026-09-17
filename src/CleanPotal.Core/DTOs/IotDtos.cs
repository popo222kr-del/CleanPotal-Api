namespace CleanPotal.Core.DTOs;

/// <summary>
/// 온·습도 모니터링 화면이 받는 자료. 상태 판정까지 서버가 끝내고 내려보낸다
/// — 나중에 붙일 알림이 화면과 같은 기준으로 울려야 하기 때문이다.
/// </summary>
/// <param name="Status">normal · warn · alert · offline</param>
public record SensorReadingDto(
    string DeviceId, string DeviceName, string Site,
    double? Temperature, double? Humidity, int? Battery, int? LinkQuality,
    DateTime? ReceivedAt,
    string Status, string StatusLabel, string? StatusReason, bool BatteryLow);

/// <summary>추이 그래프의 한 점. 값이 없으면 그 자리는 선을 끊는다.</summary>
public record SensorHistoryPointDto(DateTime ReceivedAt, double? Temperature, double? Humidity);

public record SensorHistoryDto(string DeviceId, string DeviceName, IReadOnlyList<SensorHistoryPointDto> Points);

/// <summary>
/// 수집 계통 상태. 브로커가 멎으면 MqttOnline 이 false 로 내려오고 화면은 센서 칸만 회색이 된다.
/// Zigbee2MqttOnline 은 bridge/state 토픽에서 오며, 아직 한 번도 못 받았으면 null 이다.
/// </summary>
public record ZigbeeStatusDto(
    bool MqttOnline, bool? Zigbee2MqttOnline, int SensorsOnline, int SensorsTotal, string? Message);

/// <summary>화면 한 번 그릴 자료를 한 번에 — 센서 목록과 계통 상태를 따로 부르지 않게.</summary>
public record SensorSnapshotDto(IReadOnlyList<SensorReadingDto> Sensors, ZigbeeStatusDto Status);
