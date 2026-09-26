using CleanPotal.Api.Controllers;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 회의록(생산팀 인수인계)과 주간보고는 한 API 를 쓰지만 영역이 다르다 — 종류마다 그 영역 정책으로 다시 확인한다.
/// 예전에는 인수인계 편집자가 주간보고(OFFICE)를 읽고 고칠 수 있었다.
/// </summary>
public class ReportsAreaTests
{
    [Theory]
    [InlineData("weekly", false, "ViewOffice")]
    [InlineData("weekly", true, "EditOffice")]
    [InlineData("meeting", false, "ViewHandover")]
    [InlineData("meeting", true, "EditHandover")]
    [InlineData(null, true, "EditHandover")]      // 종류를 안 주면 예전처럼 회의록
    [InlineData("other", false, "ViewHandover")]
    public void 보고서_종류마다_그_영역_정책을_쓴다(string? type, bool edit, string policy)
        => Assert.Equal(policy, ReportsController.PolicyFor(type, edit));

    [Theory]
    [InlineData("weekly", "weekly")]
    [InlineData("meeting", "meeting")]
    [InlineData(null, "meeting")]
    [InlineData("anything", "meeting")]
    public void 종류는_두_가지뿐이다(string? given, string stored)
        => Assert.Equal(stored, CleanPotal.Infrastructure.Services.ReportService.NormalizeType(given));
}
