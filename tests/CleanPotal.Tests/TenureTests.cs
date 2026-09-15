using CleanPotal.Core;
using Xunit;

namespace CleanPotal.Tests;

public class TenureTests
{
    private static readonly DateOnly 기준일 = new(2026, 9, 15);

    [Fact]
    public void 년과_개월을_함께_적는다()
        => Assert.Equal("8년 3개월", Tenure.Format("2018-06-01", 기준일));   // WPF 화면과 같은 값

    [Fact]
    public void 딱_떨어지면_년만_적는다()
        => Assert.Equal("8년", Tenure.Format("2018-09-15", 기준일));

    [Fact]
    public void 일_년_미만이면_개월만_적는다()
        => Assert.Equal("7개월", Tenure.Format("2026-02-15", 기준일));

    [Fact]
    public void 그_달의_일자에_아직_도달하지_않았으면_한_달_빼준다()
    {
        Assert.Equal("6개월", Tenure.Format("2026-02-16", 기준일));   // 하루 모자람
        Assert.Equal("7개월", Tenure.Format("2026-02-15", 기준일));   // 딱 채움
    }

    [Fact]
    public void 입사_직후는_1개월_미만()
        => Assert.Equal("1개월 미만", Tenure.Format("2026-09-01", 기준일));

    [Fact]
    public void 아직_입사_전이면_입사_예정()
        => Assert.Equal("입사 예정", Tenure.Format("2026-12-01", 기준일));

    [Theory]
    [InlineData("2018.06.01")]
    [InlineData("2018/06/01")]
    [InlineData("20180601")]
    [InlineData("2018-6-1")]
    [InlineData("  2018-06-01  ")]
    public void WPF_시절_섞인_표기도_받아들인다(string raw)
        => Assert.Equal("8년 3개월", Tenure.Format(raw, 기준일));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("미정")]
    [InlineData("2018-13-45")]
    public void 해석하지_못하면_빈_문자열_틀린_경력을_지어내지_않는다(string? raw)
        => Assert.Equal("", Tenure.Format(raw, 기준일));
}
