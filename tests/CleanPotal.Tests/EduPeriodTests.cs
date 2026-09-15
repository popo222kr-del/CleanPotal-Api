using CleanPotal.Core;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 교육 일자 표기 — WPF 화면과 같은 문자열이 나오는지.
/// 웹은 예전에 WPF 에 존재하지도 않는 EduDate 컬럼만 그려서 298건 전부 빈칸으로 보였다.
/// </summary>
public class EduPeriodTests
{
    [Fact]
    public void 하루짜리는_날짜_하나만()
        => Assert.Equal("2018-06-04", EduPeriod.Format("2018-06-04", ""));

    [Fact]
    public void 같은_달_안에서_끝나면_뒤는_일만_적는다()
        => Assert.Equal("2018-06-04~07", EduPeriod.Format("2018-06-04", "2018-06-07"));   // WPF 표기

    [Fact]
    public void 달이_넘어가면_월_일을_적는다()
        => Assert.Equal("2018-06-28~07-02", EduPeriod.Format("2018-06-28", "2018-07-02"));

    [Fact]
    public void 해가_넘어가면_전체를_적는다()
        => Assert.Equal("2018-12-28~2019-01-03", EduPeriod.Format("2018-12-28", "2019-01-03"));

    [Fact]
    public void 시작과_종료가_같으면_하나만()
        => Assert.Equal("2018-06-04", EduPeriod.Format("2018-06-04", "2018-06-04"));

    [Fact]
    public void 아직_이수하지_않았으면_빈칸()
        => Assert.Equal("", EduPeriod.Format("", ""));

    [Fact]
    public void 웹에서_직접_입력한_옛_기록은_EduDate_를_쓴다()
    {
        // 임포트 데이터는 EduDate 가 비어 있지만, 웹에서 만든 기록에는 값이 있을 수 있다.
        Assert.Equal("2026-01-15", EduPeriod.Format("", "", "2026-01-15"));
        // 시작일이 있으면 그쪽이 우선이다.
        Assert.Equal("2018-06-04", EduPeriod.Format("2018-06-04", "", "2026-01-15"));
    }

    [Fact]
    public void 형식이_다르면_원문을_잃지_않고_이어_붙인다()
    {
        Assert.Equal("2018.06.04~2018.06.07", EduPeriod.Format("2018.06.04", "2018.06.07"));
        Assert.Equal("미정", EduPeriod.Format("미정", ""));
    }

    [Fact]
    public void 앞뒤_공백과_null_을_견딘다()
    {
        Assert.Equal("2018-06-04", EduPeriod.Format(" 2018-06-04 ", "  "));
        Assert.Equal("", EduPeriod.Format(null, null, null));
    }

    [Fact]
    public void 시작일만_비어_있으면_종료일을_쓴다()
        => Assert.Equal("2018-06-07", EduPeriod.Format("", "2018-06-07"));
}
