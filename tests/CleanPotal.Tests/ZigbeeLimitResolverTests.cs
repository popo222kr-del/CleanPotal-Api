using CleanPotal.Core.Iot;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 어느 기준을 쓸지 고르는 규칙. 좁은 쪽이 이긴다 — 센서 → 사업장 → 전체 → 설정 파일.
/// 여기가 틀리면 관리자가 바꾼 값이 조용히 무시되거나, 엉뚱한 창고 기준으로 경고가 뜬다.
/// </summary>
public class ZigbeeLimitResolverTests
{
    private static ZigbeeOptions Options() => new()
    {
        OfflineAfterMinutes = 5,
        LowBatteryPercent = 20,
        Temperature = new ZigbeeBand { NormalMin = 18, NormalMax = 28, WarnMin = 15, WarnMax = 30 },
        Humidity = new ZigbeeBand { NormalMin = 40, NormalMax = 60, WarnMin = 30, WarnMax = 70 },
    };

    private static ZigbeeLimitResolver.Row Row(string scope, string key, double tempNormalMax) => new(
        scope, key,
        TempNormalMin: 18, TempNormalMax: tempNormalMax, TempWarnMin: 15, TempWarnMax: 30,
        HumidNormalMin: 40, HumidNormalMax: 60, HumidWarnMin: 30, HumidWarnMax: 70,
        OfflineAfterMinutes: 5, LowBatteryPercent: 20, SnapshotIntervalMinutes: 0);

    [Fact]
    public void 표가_비어_있으면_설정_파일을_쓴다()
    {
        var limits = ZigbeeLimitResolver.Resolve("dongtan_1", "동탄", [], Options());
        Assert.Equal(28, limits.Temperature.NormalMax);
        Assert.Equal("기본(설정 파일)", limits.Source);
    }

    [Fact]
    public void 전체_기본이_설정_파일을_이긴다()
    {
        var limits = ZigbeeLimitResolver.Resolve("dongtan_1", "동탄", [Row("global", "", 26)], Options());
        Assert.Equal(26, limits.Temperature.NormalMax);
        Assert.Equal("전체 기본", limits.Source);
    }

    [Fact]
    public void 사업장_기준이_전체_기본을_이긴다()
    {
        var rows = new[] { Row("global", "", 26), Row("site", "동탄", 24) };
        var limits = ZigbeeLimitResolver.Resolve("dongtan_1", "동탄", rows, Options());
        Assert.Equal(24, limits.Temperature.NormalMax);
        Assert.Equal("동탄 기준", limits.Source);
    }

    [Fact]
    public void 센서_기준이_사업장을_이긴다()
    {
        var rows = new[] { Row("global", "", 26), Row("site", "동탄", 24), Row("device", "dongtan_1", 22) };
        var limits = ZigbeeLimitResolver.Resolve("dongtan_1", "동탄", rows, Options());
        Assert.Equal(22, limits.Temperature.NormalMax);
        Assert.Equal("센서 지정", limits.Source);
    }

    [Fact]
    public void 다른_센서는_자기_사업장_기준을_따른다()
    {
        var rows = new[] { Row("site", "동탄", 24), Row("device", "dongtan_1", 22) };
        var limits = ZigbeeLimitResolver.Resolve("dongtan_2", "동탄", rows, Options());
        Assert.Equal(24, limits.Temperature.NormalMax);
    }

    [Fact]
    public void 다른_사업장_기준에는_걸리지_않는다()
    {
        var rows = new[] { Row("global", "", 26), Row("site", "동탄", 24) };
        var limits = ZigbeeLimitResolver.Resolve("cheonan_1", "천안", rows, Options());
        Assert.Equal(26, limits.Temperature.NormalMax);
        Assert.Equal("전체 기본", limits.Source);
    }

    [Fact]
    public void 사업장이_비어_있으면_전체_기본으로_내려간다()
    {
        var rows = new[] { Row("global", "", 26), Row("site", "동탄", 24) };
        var limits = ZigbeeLimitResolver.Resolve("unknown_1", "", rows, Options());
        Assert.Equal(26, limits.Temperature.NormalMax);
    }

    [Fact]
    public void 기록_주기는_전체_기본_줄에서만_읽는다()
    {
        var global = Row("global", "", 26) with { SnapshotIntervalMinutes = 5 };
        var site = Row("site", "동탄", 24) with { SnapshotIntervalMinutes = 99 };
        Assert.Equal(5, ZigbeeLimitResolver.SnapshotMinutes([global, site], Options()));
    }

    [Fact]
    public void 전체_기본_줄이_없으면_기록_주기는_설정_파일을_쓴다()
    {
        var options = Options();
        options.SnapshotIntervalMinutes = 1;
        Assert.Equal(1, ZigbeeLimitResolver.SnapshotMinutes([Row("site", "동탄", 24)], options));
    }
}
