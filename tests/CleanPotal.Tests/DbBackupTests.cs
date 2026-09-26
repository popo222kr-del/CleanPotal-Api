using CleanPotal.Api.Infrastructure;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>DB 백업 파일 이름 — 요일별 7개를 돌려 써서 지우는 작업 없이 1주일치가 남고, 매월 1일 것은 따로 남는다.</summary>
public sealed class DbBackupTests
{
    [Fact]
    public void 평일은_요일_파일_하나만_쓴다()
    {
        var names = DbBackup.FileNames("JUEON", new DateTime(2026, 9, 26, 3, 30, 0));   // 토요일
        Assert.Equal(["JUEON_Sat.bak"], names);
    }

    [Fact]
    public void 매월_1일은_월별_파일도_남긴다()
    {
        var names = DbBackup.FileNames("JUEON", new DateTime(2026, 10, 1, 3, 30, 0));   // 목요일
        Assert.Equal(["JUEON_Thu.bak", "JUEON_2026-10.bak"], names);
    }

    [Fact]
    public void 일주일_동안_서로_다른_7개_이름을_쓴다()
    {
        var start = new DateTime(2026, 9, 21);
        var slots = Enumerable.Range(0, 7).Select(i => DbBackup.DaySlot(start.AddDays(i))).Distinct().Count();
        Assert.Equal(7, slots);
        Assert.Equal(DbBackup.DaySlot(start), DbBackup.DaySlot(start.AddDays(7)));
    }
}
