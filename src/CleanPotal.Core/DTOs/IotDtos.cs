namespace CleanPotal.Core.DTOs;

/// <summary>
/// 온·습도 모니터링 화면이 받는 자료. 상태 판정까지 서버가 끝내고 내려보낸다
/// — 나중에 붙일 알림이 화면과 같은 기준으로 울려야 하기 때문이다.
/// </summary>
/// <param name="Status">normal · warn · alert · offline</param>
/// <param name="LimitSource">이 센서에 걸린 판정 기준이 어디서 왔는지(센서 지정 · 동탄 기준 · 전체 기본 …).</param>
public record SensorReadingDto(
    string DeviceId, string DeviceName, string Site,
    double? Temperature, double? Humidity, int? Battery, int? LinkQuality,
    DateTime? ReceivedAt,
    string Status, string StatusLabel, string? StatusReason, bool BatteryLow,
    string LimitSource);

/// <summary>추이 그래프의 한 점. 값이 없으면 그 자리는 선을 끊는다.</summary>
public record SensorHistoryPointDto(DateTime ReceivedAt, double? Temperature, double? Humidity);

/// <param name="BucketMinutes">여러 줄을 묶어 평균 낸 간격(분). 0 이면 원본 그대로다.</param>
public record SensorHistoryDto(
    string DeviceId, string DeviceName, IReadOnlyList<SensorHistoryPointDto> Points,
    DateTime From, DateTime To, int BucketMinutes, bool RealOnly);

/// <summary>
/// 수집 계통 상태. 브로커가 멎으면 MqttOnline 이 false 로 내려오고 화면은 센서 칸만 회색이 된다.
/// Zigbee2MqttOnline 은 bridge/state 토픽에서 오며, 아직 한 번도 못 받았으면 null 이다.
/// </summary>
public record ZigbeeStatusDto(
    bool MqttOnline, bool? Zigbee2MqttOnline, int SensorsOnline, int SensorsTotal, string? Message);

/// <summary>화면 한 번 그릴 자료를 한 번에 — 센서 목록과 계통 상태를 따로 부르지 않게.</summary>
public record SensorSnapshotDto(IReadOnlyList<SensorReadingDto> Sensors, ZigbeeStatusDto Status);

// ── 판정 기준(관리자) ──

/// <summary>기준 한 벌. Scope 는 global · site · device, ScopeKey 는 사업장 이름 또는 센서 DeviceId.</summary>
public record ZigbeeThresholdDto(
    string Scope, string ScopeKey, string Label,
    double TempNormalMin, double TempNormalMax, double TempWarnMin, double TempWarnMax,
    double HumidNormalMin, double HumidNormalMax, double HumidWarnMin, double HumidWarnMax,
    int OfflineAfterMinutes, int LowBatteryPercent,
    /// <summary>이력 주기 기록 간격(분). 전체 공통이라 global 줄에서만 쓰인다. 0 이면 끈다.</summary>
    int SnapshotIntervalMinutes,
    bool IsStored, DateTime? UpdatedAt, string? UpdatedBy);

/// <summary>기준 화면이 한 번에 받는 것 — 지금 걸려 있는 기준들과, 고를 수 있는 사업장·센서 목록.</summary>
public record ZigbeeThresholdPageDto(
    ZigbeeThresholdDto Default,
    IReadOnlyList<ZigbeeThresholdDto> Rows,
    IReadOnlyList<ZigbeeScopeOptionDto> Sites,
    IReadOnlyList<ZigbeeScopeOptionDto> Devices);

public record ZigbeeScopeOptionDto(string Key, string Label);

public record ZigbeeThresholdSaveRequest(
    string Scope, string? ScopeKey,
    double TempNormalMin, double TempNormalMax, double TempWarnMin, double TempWarnMax,
    double HumidNormalMin, double HumidNormalMax, double HumidWarnMin, double HumidWarnMax,
    int OfflineAfterMinutes, int LowBatteryPercent, int SnapshotIntervalMinutes);

public record ZigbeeThresholdResultDto(bool Success, string Message);

// ── 기간 조회 ──

/// <summary>
/// 한 센서의 구간 요약. 기준을 벗어난 시간은 줄 수로 세고 구간 길이로 환산한 값이라 근사치다
/// — 값이 안 올라온 동안은 셀 수가 없기 때문이다.
/// </summary>
public record SensorSummaryDto(
    string DeviceId, string DeviceName, string Site, string LimitSource,
    int Count, DateTime? FirstAt, DateTime? LastAt,
    double? TempMin, double? TempMax, double? TempAvg,
    double? HumidMin, double? HumidMax, double? HumidAvg,
    int NormalMinutes, int WarnMinutes, int AlertMinutes);

public record SensorSummaryPageDto(DateTime From, DateTime To, IReadOnlyList<SensorSummaryDto> Sensors);

/// <summary>내보내기용 한 줄. 화면이 이것으로 엑셀을 만든다.</summary>
public record SensorExportRowDto(
    DateTime ReceivedAt, string DeviceId, string DeviceName, string Site,
    double? Temperature, double? Humidity, int? Battery, int? LinkQuality,
    bool IsSnapshot, string StatusLabel);

/// <param name="Truncated">한도에 걸려 뒷부분이 잘렸는지. 잘렸으면 화면이 알려 준다.</param>
public record SensorExportDto(
    DateTime From, DateTime To, bool RealOnly, bool Truncated,
    IReadOnlyList<SensorExportRowDto> Rows);
