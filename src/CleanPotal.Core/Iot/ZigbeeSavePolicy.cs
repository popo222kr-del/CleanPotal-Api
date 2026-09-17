namespace CleanPotal.Core.Iot;

/// <summary>
/// 이력을 남길지 말지. 센서는 값이 그대로여도 주기적으로 올려 보내는데, 그것까지 전부 쌓으면
/// 표가 몇 달 만에 수백만 줄이 된다. 그렇다고 값이 바뀔 때만 남기면 "언제까지 살아 있었는지" 가 사라진다.
///
/// 그래서 둘을 합친다 — 값이 바뀌면 바로, 안 바뀌었으면 정해 둔 간격이 지났을 때만 남긴다.
/// </summary>
public static class ZigbeeSavePolicy
{
    /// <summary>마지막으로 남긴 값.</summary>
    public readonly record struct Saved(double? Temperature, double? Humidity, DateTime At);

    public static bool ShouldSave(Saved? last, double? temperature, double? humidity, DateTime now, int minIntervalSeconds)
    {
        if (last is not { } prev) return true;                                  // 첫 값은 무조건 남긴다
        if (prev.Temperature != temperature || prev.Humidity != humidity) return true;
        return (now - prev.At).TotalSeconds >= Math.Max(0, minIntervalSeconds);
    }
}
