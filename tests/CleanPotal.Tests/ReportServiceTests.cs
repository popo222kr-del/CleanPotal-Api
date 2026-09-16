using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 생산팀 인수인계(회의록) 전체 검색.
/// 주간보고와 달리 블록 구조가 없어 주간/야간/Office 메모 텍스트를 직접 관통해야 한다.
/// </summary>
public class ReportServiceTests
{
    private static ReportService Svc(TestDb t) => new(t.Db, FakeCurrentUser.Admin());

    private static Report Meeting(string shortTitle, string main = "", string night = "", string memo = "") => new()
    {
        ReportType = "meeting", MonthTitle = "2026년 9월", Title = shortTitle, ShortTitle = shortTitle,
        DateRange = "2026.09." + shortTitle, MainContent = main, NightContent = night, Memo = memo,
    };

    [Fact]
    public async Task 주간_야간_메모_어디에_있든_찾는다()
    {
        using var t = new TestDb();
        t.Db.Reports.Add(Meeting("14", main: "NDC03 바울 영신 완료"));
        t.Db.Reports.Add(Meeting("13", night: "NDC03 청소 진행"));
        t.Db.Reports.Add(Meeting("12", memo: "NDC03 사용 관련 공지"));
        t.Db.Reports.Add(Meeting("11", main: "무관한 내용"));
        await t.Db.SaveChangesAsync();

        var hits = await Svc(t).SearchMeetingAsync("NDC03");

        Assert.Equal(3, hits.Count);
        Assert.Contains(hits, h => h.FieldLabel == "주간");
        Assert.Contains(hits, h => h.FieldLabel == "야간");
        Assert.Contains(hits, h => h.FieldLabel == "Office 메모");
    }

    [Fact]
    public async Task 한_보고서에_여러_칸이_걸리면_칸마다_결과를_낸다()
    {
        using var t = new TestDb();
        t.Db.Reports.Add(Meeting("14", main: "부적합반입 LIST 확인", memo: "부적합반입 LIST 정렬 요청"));
        await t.Db.SaveChangesAsync();

        var hits = await Svc(t).SearchMeetingAsync("부적합반입");

        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Equal(hits[0].ReportId, h.ReportId));   // 같은 보고서의 두 칸
    }

    [Fact]
    public async Task 대소문자를_가리지_않는다()
    {
        using var t = new TestDb();
        t.Db.Reports.Add(Meeting("14", main: "NDC08 사용관련"));
        await t.Db.SaveChangesAsync();

        Assert.Single(await Svc(t).SearchMeetingAsync("ndc08"));
    }

    [Fact]
    public async Task 주간보고_타입은_섞이지_않는다()
    {
        using var t = new TestDb();
        var weekly = Meeting("주간1", main: "NDC03 관련");
        weekly.ReportType = "weekly";
        t.Db.Reports.Add(weekly);
        await t.Db.SaveChangesAsync();

        Assert.Empty(await Svc(t).SearchMeetingAsync("NDC03"));
    }

    [Fact]
    public async Task 빈_검색어는_빈_결과를_준다()
    {
        using var t = new TestDb();
        t.Db.Reports.Add(Meeting("14", main: "아무 내용"));
        await t.Db.SaveChangesAsync();

        Assert.Empty(await Svc(t).SearchMeetingAsync(""));
        Assert.Empty(await Svc(t).SearchMeetingAsync("   "));
    }
}
