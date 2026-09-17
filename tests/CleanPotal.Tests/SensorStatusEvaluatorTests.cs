using CleanPotal.Core.Iot;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 온·습도 상태 판정. 이 판정이 나중에 붙일 알림의 기준이 되므로, 경계값이 흔들리면 여기서 걸린다.
/// </summary>
public class SensorStatusEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 9, 17, 14, 30, 0);

    private static ZigbeeOptions Options() => new()
    {
        OfflineAfterMinutes = 5,
        Temperature = new ZigbeeBand { NormalMin = 18, NormalMax = 28, WarnMin = 15, WarnMax = 30 },
        Humidity = new ZigbeeBand { NormalMin = 40, NormalMax = 60, WarnMin = 30, WarnMax = 70 },
    };

    private static SensorStatus Status(double? temp, double? humid, DateTime? at = null)
        => SensorStatusEvaluator.Evaluate(temp, humid, at ?? Now.AddMinutes(-1), Now, Options()).Status;

    [Theory]
    [InlineData(18, SensorStatus.Normal)]     // 정상 하한
    [InlineData(28, SensorStatus.Normal)]     // 정상 상한
    [InlineData(28.5, SensorStatus.Warning)]  // 정상 초과 ~ 주의 상한
    [InlineData(30, SensorStatus.Warning)]    // 주의 상한까지는 주의
    [InlineData(30.1, SensorStatus.Alert)]    // 주의 상한 초과
    [InlineData(17, SensorStatus.Warning)]    // 정상 하한 미만
    [InlineData(14, SensorStatus.Alert)]      // 주의 하한 미만
    public void 온도_구간대로_판정한다(double temp, SensorStatus expected)
        => Assert.Equal(expected, Status(temp, 50));

    [Theory]
    [InlineData(40, SensorStatus.Normal)]
    [InlineData(60, SensorStatus.Normal)]
    [InlineData(65, SensorStatus.Warning)]
    [InlineData(70, SensorStatus.Warning)]
    [InlineData(70.5, SensorStatus.Alert)]
    [InlineData(25, SensorStatus.Alert)]
    public void 습도_구간대로_판정한다(double humid, SensorStatus expected)
        => Assert.Equal(expected, Status(24, humid));

    [Fact]
    public void 온도와_습도_중_나쁜_쪽을_따른다()
        => Assert.Equal(SensorStatus.Alert, Status(24, 75));   // 온도는 정상, 습도는 경고

    [Fact]
    public void 한_번도_수신한_적이_없으면_통신_끊김이다()
        => Assert.Equal(SensorStatus.Offline,
            SensorStatusEvaluator.Evaluate(null, null, null, Now, Options()).Status);

    [Fact]
    public void 정해둔_시간을_넘겨_수신이_없으면_값이_좋아도_통신_끊김이다()
        => Assert.Equal(SensorStatus.Offline, Status(24, 50, Now.AddMinutes(-6)));

    [Fact]
    public void 기준_시간_안이면_그대로_판정한다()
        => Assert.Equal(SensorStatus.Normal, Status(24, 50, Now.AddMinutes(-4)));

    [Fact]
    public void 수신은_됐는데_값이_비어_있으면_통신_끊김으로_본다()
        => Assert.Equal(SensorStatus.Offline, Status(null, null, Now.AddMinutes(-1)));

    [Fact]
    public void 한쪽_값만_있어도_그_값으로_판정한다()
    {
        Assert.Equal(SensorStatus.Normal, Status(24, null));
        Assert.Equal(SensorStatus.Alert, Status(null, 80));
    }

    [Fact]
    public void 정상이_아니면_이유를_함께_준다()
    {
        var verdict = SensorStatusEvaluator.Evaluate(31, 50, Now.AddMinutes(-1), Now, Options());
        Assert.Equal(SensorStatus.Alert, verdict.Status);
        Assert.False(string.IsNullOrWhiteSpace(verdict.Reason));
        Assert.Contains("온도", verdict.Reason);
    }

    [Fact]
    public void 정상이면_이유가_없다()
        => Assert.Null(SensorStatusEvaluator.Evaluate(24, 50, Now.AddMinutes(-1), Now, Options()).Reason);
}
