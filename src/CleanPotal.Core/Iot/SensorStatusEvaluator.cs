namespace CleanPotal.Core.Iot;

/// <summary>센서 한 대의 상태. 숫자가 아니라 사람이 보는 네 가지로 줄인 것이다.</summary>
public enum SensorStatus
{
    /// <summary>정상</summary>
    Normal = 0,
    /// <summary>주의 — 정상 구간을 벗어났지만 아직 경고는 아니다.</summary>
    Warning = 1,
    /// <summary>경고 — 손을 써야 한다.</summary>
    Alert = 2,
    /// <summary>통신 끊김 — 값을 못 받고 있다. 값이 나쁜 것과는 다른 문제다.</summary>
    Offline = 3,
}

/// <summary>
/// 센서 값으로 상태를 판정한다. 기준은 설정(ZigbeeOptions)에, 규칙은 여기에 둔다 —
/// 나중에 관리자 화면에서 기준을 바꿔도 이 파일은 그대로다.
///
/// 판정은 서버에서 한다. 화면이 제 나름대로 색을 칠하면, 나중에 붙일 알림(온도 이상·미수신)이
/// 화면과 다른 기준으로 울리게 된다.
/// </summary>
public static class SensorStatusEvaluator
{
    /// <summary>상태와 그렇게 본 이유. 이유는 화면 설명이자 나중에 보낼 알림 문구다.</summary>
    public readonly record struct Verdict(SensorStatus Status, string? Reason);

    public static string Label(SensorStatus status) => status switch
    {
        SensorStatus.Normal => "정상",
        SensorStatus.Warning => "주의",
        SensorStatus.Alert => "경고",
        _ => "통신 끊김",
    };

    /// <summary>값이 아예 없거나 너무 오래됐으면 통신 끊김, 그 밖에는 온도·습도 중 나쁜 쪽을 따른다.</summary>
    public static Verdict Evaluate(
        double? temperature, double? humidity, DateTime? receivedAt, DateTime now, ZigbeeOptions options)
    {
        if (receivedAt is null)
            return new Verdict(SensorStatus.Offline, "아직 수신된 값이 없습니다.");

        var idle = now - receivedAt.Value;
        if (idle > TimeSpan.FromMinutes(options.OfflineAfterMinutes))
            return new Verdict(SensorStatus.Offline, $"{options.OfflineAfterMinutes}분 넘게 값이 들어오지 않았습니다({Ago(idle)} 전 수신).");

        if (temperature is null && humidity is null)
            return new Verdict(SensorStatus.Offline, "온도·습도 값이 비어 있습니다.");

        var temp = Judge(temperature, options.Temperature, "온도", "℃");
        var humid = Judge(humidity, options.Humidity, "습도", "%");

        // 둘 중 나쁜 쪽이 그 센서의 상태다. 같은 등급이면 온도를 먼저 말한다.
        if (temp.Status >= humid.Status) return temp.Status == SensorStatus.Normal ? new Verdict(SensorStatus.Normal, null) : temp;
        return humid;
    }

    private static Verdict Judge(double? value, ZigbeeBand band, string what, string unit)
    {
        if (value is null) return new Verdict(SensorStatus.Normal, null);
        var v = value.Value;

        if (v > band.WarnMax) return new Verdict(SensorStatus.Alert, $"{what} {v}{unit} — {band.WarnMax}{unit} 초과");
        if (v < band.WarnMin) return new Verdict(SensorStatus.Alert, $"{what} {v}{unit} — {band.WarnMin}{unit} 미만");
        if (v > band.NormalMax) return new Verdict(SensorStatus.Warning, $"{what} {v}{unit} — 정상 상한 {band.NormalMax}{unit} 초과");
        if (v < band.NormalMin) return new Verdict(SensorStatus.Warning, $"{what} {v}{unit} — 정상 하한 {band.NormalMin}{unit} 미만");
        return new Verdict(SensorStatus.Normal, null);
    }

    private static string Ago(TimeSpan idle)
        => idle.TotalHours >= 1 ? $"{(int)idle.TotalHours}시간 {idle.Minutes}분" : $"{(int)idle.TotalMinutes}분";
}
