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

    // ── 칸별 실제 작성자(변경 이력에서) ──
    private static CleanPotal.Core.DTOs.ReportUpsertRequest Req(string main, string night, string memo, int ver) =>
        new("meeting", "2026년 9월", "14", "14", "2026.09.14", memo, "", main, "", night, "", "", "", "", "", [], ver);

    [Fact]
    public async Task 칸마다_실제로_쓴_사람을_모은다_비우면_지운다()
    {
        using var t = new TestDb();
        var r = Meeting("14");
        t.Db.Reports.Add(r);
        await t.Db.SaveChangesAsync();
        ReportService As(int id, string name) => new(t.Db, FakeCurrentUser.Person(id, name));

        var d = await As(1, "김주간").UpdateAsync(r.Id, Req("주간 작업", "", "", 0));
        d = await As(2, "박야간").UpdateAsync(r.Id, Req("주간 작업", "야간 작업", "", d!.RowVersion));
        d = await As(3, "이사무").UpdateAsync(r.Id, Req("주간 작업 보충", "야간 작업", "공지", d!.RowVersion));
        d = await As(1, "김주간").UpdateAsync(r.Id, Req("주간 작업 보충2", "야간 작업", "공지", d!.RowVersion));
        Assert.Equal(["김주간", "이사무"], d!.Writers!.Main);
        Assert.Equal(["박야간"], d.Writers.Night);
        Assert.Equal(["이사무"], d.Writers.Memo);

        // 다른 칸만 고친 사람은 붙지 않고, 칸을 비우면 이름도 빠진다
        d = await As(2, "박야간").UpdateAsync(r.Id, Req("주간 작업 보충2", "야간 작업", "", d.RowVersion));
        Assert.Empty(d!.Writers!.Memo);
        Assert.Equal(["김주간", "이사무"], (await As(9, "아무개").GetAsync(r.Id))!.Writers!.Main);
    }

    [Fact]
    public void 이력_글에_구분자가_섞여도_칸_이름으로_나눈다()
    {
        var w = ReportService.WritersFrom([
            ("제목: a → b / 주간: (비어 있음) → A → B 진행 / 메모: (비어 있음) → x", "갑"),
            ("야간: (비어 있음) → 야간 / 항목 수: 0 → 1", "을"),
            ("주간: A → B 진행 → (비어 있음)", "병"),
        ]);
        Assert.Empty(w.Main);
        Assert.Equal(["을"], w.Night);
        Assert.Equal(["갑"], w.Memo);
    }
}
