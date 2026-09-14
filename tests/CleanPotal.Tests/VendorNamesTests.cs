using CleanPotal.Core;
using Xunit;

namespace CleanPotal.Tests;

public class VendorNamesTests
{
    [Theory]
    [InlineData("국제", "국제엘렉트릭코리아")]
    [InlineData("국제엘레트릭코리아", "국제엘렉트릭코리아")]
    [InlineData("영신", "영신쿼츠")]
    [InlineData("DB하이텍", "동부하이텍")]
    [InlineData("semes", "세메스")]          // 대소문자 무시
    [InlineData("  영신  ", "영신쿼츠")]      // 앞뒤 공백 무시
    public void 별칭은_대표명으로_통일된다(string input, string expected)
        => Assert.Equal(expected, VendorNames.Normalize(input));

    [Theory]
    [InlineData("영신쿼츠", "영신쿼츠")]       // 이미 대표명
    [InlineData("처음보는업체", "처음보는업체")] // 사전에 없으면 그대로
    public void 사전에_없으면_원래_이름을_유지한다(string input, string expected)
        => Assert.Equal(expected, VendorNames.Normalize(input));
}
