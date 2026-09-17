using CleanPotal.Core;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 업체 관리의 업체를 MES 업체로 한 번에 올릴 때 쓰는 코드 만들기.
/// 반출번호 약어는 서류에 찍히고 업체 코드는 겹치면 저장이 막히니, 규칙이 흔들리면 여기서 걸린다.
/// </summary>
public class VendorMesCodesTests
{
    [Theory]
    [InlineData("금강쿼츠", "KKKC")]
    [InlineData("삼성전자", "SSJJ")]
    [InlineData("주엔", "JE")]
    [InlineData("이엔지쿼츠", "IEJK")]   // ㅇ 은 소리가 없어 중성으로 대신한다
    [InlineData("ABC Corp", "AC")]        // 영문은 낱말 첫 글자만
    [InlineData("  ", "V")]               // 빈 이름도 약어는 있어야 한다
    public void 약어는_이름의_초성을_따른다(string name, string expected)
        => Assert.Equal(expected, VendorMesCodes.SuggestPrefix(name));

    [Fact]
    public void 약어가_겹치면_숫자를_붙여_비켜간다()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal("KKKC", VendorMesCodes.UniquePrefix("금강쿼츠", taken));
        Assert.Equal("KKKC2", VendorMesCodes.UniquePrefix("금강쿼츠", taken));
        Assert.Equal("KKKC3", VendorMesCodes.UniquePrefix("금강 쿼츠", taken));
    }

    [Fact]
    public void 코드는_이미_쓰는_번호를_건너뛴다()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "001", "002" };
        var number = 0;
        Assert.Equal("003", VendorMesCodes.NextCode(ref number, taken));
        Assert.Equal("004", VendorMesCodes.NextCode(ref number, taken));
    }

    [Fact]
    public void 세자리를_넘으면_자릿수가_늘어난다()
    {
        Assert.Equal("001", VendorMesCodes.SequenceCode(1));
        Assert.Equal("062", VendorMesCodes.SequenceCode(62));
        Assert.Equal("1000", VendorMesCodes.SequenceCode(1000));
    }

    [Fact]
    public void 가나다가_먼저고_그다음이_ABC다()
    {
        var names = new[] { "ZETA", "금강쿼츠", "ABC", "주엔", "나래" };
        Array.Sort(names, VendorMesCodes.NameOrder);
        Assert.Equal(new[] { "금강쿼츠", "나래", "주엔", "ABC", "ZETA" }, names);
    }
}
