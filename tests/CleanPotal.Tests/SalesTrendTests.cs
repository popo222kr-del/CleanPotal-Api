using CleanPotal.Api.Infrastructure;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>매출 트렌드 — 주 나누기(월~일, 월말 끊기)·영업 구분·작년 같은 기간 대비·증감 원인(DB 없이 계산만).</summary>
public class SalesTrendTests
{
    private static SalesTrend.Sale S(string day, decimal amt, string group = "Memory", string item = "A", decimal qty = 1, string line = "16LINE")
        => new(DateOnly.Parse(day), amt, qty, group, "삼성", line, item, "품목" + item, "EQ1", "B");

    [Fact]
    public void 주는_월요일부터_일요일_월말에서_끊는다()
    {
        // 2026-10-01 은 목요일 — 1주 = 10/1(목)~10/4(일), 2주 = 10/5(월)~10/11(일)
        var w1 = SalesTrend.WeekOf(new DateOnly(2026, 10, 2));
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 4), 1), (w1.Start, w1.End, w1.No));
        var w2 = SalesTrend.WeekOf(new DateOnly(2026, 10, 14));
        Assert.Equal((new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 18), 3), (w2.Start, w2.End, w2.No));
        // 9월 말 주 — 9/28(월)~9/30(수) 에서 끊는다
        var last = SalesTrend.WeekOf(new DateOnly(2026, 9, 30));
        Assert.Equal((new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 30), 9), (last.Start, last.End, last.Month));
        Assert.Equal("10월 1주", SalesTrend.RecentWeeks(new DateOnly(2026, 10, 14), 3)[0].Label);
        Assert.Equal("9월 5주", SalesTrend.RecentWeeks(new DateOnly(2026, 10, 14), 4)[0].Label);
    }

    [Fact]
    public void 영업_구분_설정을_읽고_나머지는_기타()
    {
        var g = SalesTrend.ParseGroups(null);
        Assert.Equal(new[] { "Memory", "Foundry", "A급 Quartz", "기타" }, g.Select(x => x.Name));
        Assert.Equal("Memory", SalesTrend.GroupOf(g, "10001"));
        Assert.Equal("A급 Quartz", SalesTrend.GroupOf(g, "11009"));
        Assert.Equal("기타", SalesTrend.GroupOf(g, "10023"));
        var custom = SalesTrend.ParseGroups("SEC:Memory=10001;OTHER:SEMES=10023");
        Assert.Equal("SEMES", SalesTrend.GroupOf(custom, "10023"));
        Assert.Equal("기타", SalesTrend.GroupOf(custom, "99999"));
    }

    private static SalesTrend.Period Month(int y, int m, string today)
        => SalesTrend.Period.Make(SalesTrend.PeriodKind.Month, new DateOnly(y, m, 1), new DateOnly(y, m, 1).AddMonths(1).AddDays(-1), DateOnly.Parse(today))!;

    [Fact]
    public void 기간은_오늘까지만_세고_직전_기간을_고른다()
    {
        var oct = Month(2026, 10, "2026-10-14");
        Assert.True(oct.Partial);
        Assert.Equal((14, 31), (oct.Elapsed, oct.Days));
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 14)), oct.Previous);
        Assert.Equal((new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31), new DateOnly(2025, 10, 14)), oct.LastYear);
        // 지난 달(6월)은 끝난 기간 — 지난달 = 5월 전체
        var jun = Month(2026, 6, "2026-10-14");
        Assert.False(jun.Partial);
        Assert.Equal((new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), new DateOnly(2026, 5, 31)), jun.Previous);
        // 3/31 까지 센 달의 지난달 같은 날 = 2/28
        Assert.Equal(new DateOnly(2026, 2, 28), Month(2026, 3, "2026-10-14").Previous.To);
        var q2 = SalesTrend.Period.Make(SalesTrend.PeriodKind.Quarter, new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30), new DateOnly(2026, 10, 14))!;
        Assert.Equal("2026년 2분기", q2.Label);
        Assert.Equal((new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)), (q2.Previous.From, q2.Previous.End));
        var r = SalesTrend.Period.Make(SalesTrend.PeriodKind.Range, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 19), new DateOnly(2026, 10, 14))!;
        Assert.Equal((new DateOnly(2026, 5, 31), new DateOnly(2026, 6, 9)), (r.Previous.From, r.Previous.End));
        Assert.Null(SalesTrend.Period.Make(SalesTrend.PeriodKind.Month, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), new DateOnly(2026, 10, 14)));
    }

    [Fact]
    public void 예상은_작년_패턴과_단순_진도를_함께_낸다()
    {
        // 올해 14일까지 300, 작년 14일까지 150·작년 전체 500 → 작년 패턴 = 500 × 300/150 = 1000, 단순 = 300/14×31
        var (pattern, simple) = SalesTrend.Forecast(300, 150, 500, 14, 31);
        Assert.Equal(1000, pattern);
        Assert.Equal(Math.Round(300m / 14 * 31), simple);
        Assert.Null(SalesTrend.Forecast(300, 0, 500, 14, 31).Pattern);   // 작년 같은 날까지 매출 없음 → 단순 진도만
    }

    [Fact]
    public void 이번달_누계는_작년_같은_날까지와_비교하고_월말을_예상한다()
    {
        var sales = new[]
        {
            S("2026-10-01", 100), S("2026-10-14", 200), S("2026-10-20", 999),   // 오늘 뒤는 안 센다
            S("2025-10-10", 150), S("2025-10-25", 500),                         // 작년 10/14 까지 = 150, 전체 650
            S("2026-09-05", 80), S("2026-09-20", 70),                           // 지난달 9/14 까지 = 80
            S("2026-01-05", 1000), S("2025-02-01", 700),
        };
        var r = SalesTrend.Build(sales, Month(2026, 10, "2026-10-14"), SalesTrend.ParseGroups(null));
        var cur = r.Cards.Single(c => c.Key == "cur");
        Assert.Equal(300, cur.Value);
        Assert.Equal(150, cur.Compares[0].Base);
        Assert.Equal(1m, cur.Compares[0].Rate);
        Assert.Equal(80, cur.Compares[1].Base);
        var fc = r.Cards.Single(c => c.Key == "forecast");
        Assert.Equal(650m * 300 / 150, fc.Value);                            // 작년 패턴
        Assert.Equal(Math.Round(300m / 14 * 31), fc.Lines.Single().Value);   // 단순 진도
        var ytd = r.Cards.Single(c => c.Key == "ytd");
        Assert.Equal(1000 + 80 + 70 + 300, ytd.Value);
        Assert.Equal(700 + 150, ytd.Compares[0].Base);
        Assert.All(r.ThisYear.Total.Skip(10), v => Assert.Equal(0, v));
        Assert.Equal(300, r.ThisYear.Total[9]);
        Assert.Equal(650, r.LastYear.Total[9]);
        Assert.Equal(31, r.Daily[0].Days);
        Assert.Equal(14, r.Daily[0].UpTo);
        Assert.Equal(30, r.Daily[2].Days);   // 지난달(9월)
        // 일별 — 10월 31일 전부, 14일까지만 센다. 10/14 = 200, 작년 10/10 = 150
        Assert.Equal(31, r.Days.Count);
        Assert.Equal(200, r.Days[13].Amount);
        Assert.False(r.Days[19].Counted);
        Assert.Equal(0, r.Days[19].Amount);                                  // 10/20 은 오늘 뒤
        Assert.Equal(150, r.Days[9].Ly);
        var day = r.Cards.Single(c => c.Key == "day");
        Assert.Equal(("오늘", 200m), (day.Title, day.Value));

        // 끝난 달(9월)을 고르면 예상 대신 확정
        var sep = SalesTrend.Build(sales, Month(2026, 9, "2026-10-14"), SalesTrend.ParseGroups(null));
        Assert.Equal("9월 확정", sep.Cards.Single(c => c.Key == "forecast").Title);
        Assert.Equal(150, sep.Cards.Single(c => c.Key == "cur").Value);
        Assert.False(sep.Partial);
    }

    [Fact]
    public void 증감_원인은_구분별_합과_물량_단가로_나뉜다()
    {
        var sales = new[]
        {
            S("2025-10-02", 100, item: "A", qty: 10), S("2025-10-03", 50, "Foundry", item: "B", qty: 5), S("2025-10-04", 30, "기타", item: "C", qty: 1),
            S("2026-10-02", 240, item: "A", qty: 20), S("2026-10-03", 40, "Foundry", item: "B", qty: 5), S("2026-10-04", 60, "A급 Quartz", item: "D", qty: 2),
        };
        var r = SalesTrend.Build(sales, Month(2026, 10, "2026-10-14"), SalesTrend.ParseGroups(null));
        Assert.Equal(340 - 180, r.GroupBridge.Where(b => b.Kind == "delta").Sum(b => b.Value));
        Assert.Equal(140, r.GroupBridge.Single(b => b.Label == "Memory").Value);
        var pv = r.PriceVolume.ToDictionary(b => b.Label, b => b.Value);
        Assert.Equal(100, pv["물량 증감"]);            // A: (20-10)×10
        Assert.Equal(40 - 10, pv["단가 변동"]);        // A: (12-10)×20 = 40, B: (8-10)×5 = -10
        Assert.Equal(60, pv["새 품목"]);
        Assert.Equal(-30, pv["끊긴 품목"]);
        Assert.Equal(340 - 180, pv["물량 증감"] + pv["단가 변동"] + pv["새 품목"] + pv["끊긴 품목"]);
        var item = r.Movers.Single(m => m.Dim == "item");
        Assert.Equal("A", item.Up[0].Key);
        Assert.Equal("C", item.Down[0].Key);
        Assert.Contains(r.GroupRows, g => g.Name == "SEC 소계" && g.Cur == 280 && g.Ly == 150);
    }
}
