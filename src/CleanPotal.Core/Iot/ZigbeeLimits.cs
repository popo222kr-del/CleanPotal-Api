namespace CleanPotal.Core.Iot;

/// <summary>
/// 센서 한 대에 실제로 걸리는 판정 기준. 설정 파일이든 표든, 판정하는 쪽은 이 모양만 안다.
/// </summary>
/// <param name="Source">이 기준이 어디서 왔는지(화면에 "동탄 기준" 처럼 보여 준다).</param>
public readonly record struct ZigbeeLimits(
    ZigbeeBand Temperature, ZigbeeBand Humidity,
    int OfflineAfterMinutes, int LowBatteryPercent, string Source);

/// <summary>
/// 어느 기준을 쓸지 고른다. <b>좁은 쪽이 이긴다</b> — 센서 → 사업장 → 전체 → 설정 파일.
///
/// 표에 아무것도 없으면 설정 파일 값이 그대로 쓰인다. 그래서 기준을 한 번도 손대지 않은 곳에서는
/// 이 기능을 넣기 전과 똑같이 동작한다.
/// </summary>
public static class ZigbeeLimitResolver
{
    public const string ScopeGlobal = "global";
    public const string ScopeSite = "site";
    public const string ScopeDevice = "device";

    /// <summary>표 한 줄을 판정 기준 모양으로 옮긴 것. 어떤 표를 쓰든 이 모양으로 넘겨준다.</summary>
    public readonly record struct Row(
        string Scope, string ScopeKey,
        double TempNormalMin, double TempNormalMax, double TempWarnMin, double TempWarnMax,
        double HumidNormalMin, double HumidNormalMax, double HumidWarnMin, double HumidWarnMax,
        int OfflineAfterMinutes, int LowBatteryPercent, int SnapshotIntervalMinutes = 0);

    /// <summary>
    /// 이력 주기 기록 간격(분). 이 값만은 전체 공통이라 global 줄에서만 읽는다 —
    /// 창고마다 다른 주기로 적으면 그래프를 겹쳐 볼 때 점 개수가 달라 비교가 안 된다.
    /// </summary>
    public static int SnapshotMinutes(IReadOnlyList<Row> rows, ZigbeeOptions options)
    {
        var global = Find(rows, ScopeGlobal, "");
        return global is { } g ? Math.Max(0, g.SnapshotIntervalMinutes) : Math.Max(0, options.SnapshotIntervalMinutes);
    }

    /// <summary>설정 파일 값. 표에 아무 줄도 없을 때 쓰인다.</summary>
    public static ZigbeeLimits FromOptions(ZigbeeOptions options) => new(
        options.Temperature, options.Humidity,
        options.OfflineAfterMinutes, options.LowBatteryPercent, "기본(설정 파일)");

    public static ZigbeeLimits Resolve(
        string deviceId, string site, IReadOnlyList<Row> rows, ZigbeeOptions options)
    {
        var device = Find(rows, ScopeDevice, deviceId);
        if (device is { } d) return ToLimits(d, "센서 지정");

        if (!string.IsNullOrWhiteSpace(site) && Find(rows, ScopeSite, site) is { } s)
            return ToLimits(s, $"{site} 기준");

        if (Find(rows, ScopeGlobal, "") is { } g) return ToLimits(g, "전체 기본");

        return FromOptions(options);
    }

    private static Row? Find(IReadOnlyList<Row> rows, string scope, string key)
    {
        foreach (var r in rows)
            if (string.Equals(r.Scope, scope, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.ScopeKey ?? "", key ?? "", StringComparison.OrdinalIgnoreCase))
                return r;
        return null;
    }

    private static ZigbeeLimits ToLimits(Row r, string source) => new(
        new ZigbeeBand { NormalMin = r.TempNormalMin, NormalMax = r.TempNormalMax, WarnMin = r.TempWarnMin, WarnMax = r.TempWarnMax },
        new ZigbeeBand { NormalMin = r.HumidNormalMin, NormalMax = r.HumidNormalMax, WarnMin = r.HumidWarnMin, WarnMax = r.HumidWarnMax },
        r.OfflineAfterMinutes, r.LowBatteryPercent, source);
}
