using CleanPotal.Api.Infrastructure;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>MES 조회 — 기존 MES DB 에서 읽은 LOT 로 재공·출하 표 만들기(DB 없이 계산만).</summary>
public class LegacyMesReportTests
{
    private static LegacyLot Lot(string mat, string oper, string line = "", string type = "Accessory", string cust = "10001",
        decimal? price = 1000, string outDay = "", string factory = "AF01") => new()
    {
        LotId = Guid.NewGuid().ToString("N")[..8], MatId = mat, MatDesc = "(C)" + mat, Oper = oper, Line = line, MatType = type,
        Customer = cust, Price = price, DueTime = outDay, Factory = factory,
    };

    [Fact]
    public void 재공은_품목별_공정별_라인별로_센다()
    {
        var r = LegacyMesReports.Wip(new[]
        {
            Lot("A", "4000"), Lot("A", "4000", "17LINE"), Lot("A", "3000"),
            Lot("B", "8100", type: "QTZ Boat"), Lot("C", "9999", "16LINE"),
        });
        Assert.Equal(5, r.Total);
        Assert.Equal(3, r.Rows.Single(x => x.MatId == "A").Total);
        Assert.Equal(2, r.Rows.Single(x => x.MatId == "A").Opers["4000"]);
        Assert.Equal(1, r.OperTotals["8100"]);
        Assert.Equal(new[] { "16LINE", "17LINE" }, r.Lines);
        Assert.Contains(r.Columns, c => c is { Oper: "9999", Label: "9999" });   // 모르는 공정은 코드로 뒤에
        Assert.Equal("INPUT", r.Columns[0].Label);
    }

    [Fact]
    public void 출하는_고객사_라인_월별로_수량과_금액을_더한다()
    {
        var r = LegacyMesReports.Shipments(new[]
        {
            Lot("A", "8100", "15 LINE", price: 500, outDay: "20260115"),
            Lot("A", "8100", "15 LINE", price: 500, outDay: "2026-01-20"),
            Lot("B", "8100", "P2 LINE", price: 1200, outDay: "2026/03/02 10:00"),
            Lot("C", "8100", "P2 LINE", cust: "10002", price: null, outDay: "20260302"),
            Lot("D", "8100", "", outDay: ""),            // 출고일 없음
            Lot("E", "8100", "", outDay: "20251231"),    // 다른 해
        }, 2026, c => c == "10001" ? "MEMORY" : c);

        Assert.Equal(4, r.QtyTotal);
        Assert.Equal(2200, r.AmountTotal);
        Assert.Equal(2, r.NoDate);
        var mem = r.Customers.Single(c => c.Code == "10001");
        Assert.Equal("MEMORY", mem.Name);
        var l15 = mem.Lines.Single(l => l.Line == "15 LINE");
        Assert.Equal(2, l15.Qty[0]);
        Assert.Equal(1000, l15.Amount[0]);
        Assert.Equal(1, mem.Lines.Single(l => l.Line == "P2 LINE").Qty[2]);
        Assert.Equal(0, r.Customers.Single(c => c.Code == "10002").AmountTotal);   // 단가 없는 품목은 금액 0
    }

    [Theory]
    [InlineData("20261012153000", "2026-10-12")]
    [InlineData("2026-10-12", "2026-10-12")]
    [InlineData("2026/10/12 15:30", "2026-10-12")]
    [InlineData("", null)]
    [InlineData("abc", null)]
    public void MES_날짜_읽기(string s, string? expected)
        => Assert.Equal(expected, LegacyMesDb.ParseDay(s)?.ToString("yyyy-MM-dd"));

    [Theory]
    [InlineData("330,000", 330000.0)]
    [InlineData(" 1250.5 ", 1250.5)]
    [InlineData("", null)]
    public void 단가_읽기(string s, double? expected)
        => Assert.Equal(expected is null ? null : (decimal?)(decimal)expected.Value, LegacyMesDb.ParsePrice(s));

    [Theory]
    [InlineData("AOVG2303-3165", "AOVG2303-3165")]
    [InlineData("50%", "50[%]")]
    [InlineData("C_1[a]", "C[_]1[[]a]")]
    public void LIKE_특수문자는_글자_그대로(string v, string expected)
        => Assert.Equal(expected, LegacyMesDb.EscapeLike(v));

    [Fact]
    public void 조건_있음_판단()
    {
        Assert.False(new LotSearch().Any);
        Assert.False(new LotSearch(Sn: "  ").Any);
        Assert.True(new LotSearch(Sn: "AOVG2303").Any);
    }

    [Fact]
    public void AETS_출고일은_출하된_LOT_만_SHIP_TIME_우선()
    {
        Assert.Equal("", new LegacyLot { Factory = "AF01", DueTime = "20261015040711" }.AetsOutDay);
        Assert.Equal("20261020100000", new LegacyLot { Factory = "CUSTOMER", DueTime = "20261015040711", ShipTime = "20261020100000" }.AetsOutDay);
        Assert.Equal("20261015040711", new LegacyLot { Factory = "CUSTOMER", DueTime = "20261015040711" }.AetsOutDay);
    }

    [Theory]
    [InlineData("20261013154210", "2026-10-13 15:42")]
    [InlineData("2026-10-12 14:29:15.913", "2026-10-12 14:29")]
    [InlineData("20261013", null)]
    public void MES_시각_읽기(string s, string? expected)
        => Assert.Equal(expected, LegacyMesDb.ParseMinute(s)?.ToString("yyyy-MM-dd HH:mm"));

    [Fact]
    public void 세정_이력_칸_펼치기_실제_LOT_기준()
    {
        // 기존 MES 에서 출하까지 끝난 LOT 의 이력(사용자가 보낸 값) — 세정 MDC03 · DRY MBS01
        var raw = new Dictionary<string, string>
        {
            ["in"] = "20261006163239|2311594|",
            ["insp"] = "20261006193556|2509781|",
            ["cleanS"] = "20261008042634|0805|MDC03", ["cleanE"] = "20261008085001|0626|MDC03",
            ["dryS"] = "20261008085706|0626|MBS01", ["dryE"] = "20261009171802|2509781|MBS01",
            ["final"] = "20261009182948|2509781|",
            ["wIn"] = "7410", ["wOut"] = "7390",
            ["result"] = "0000000012|OK",
        };
        var names = new Dictionary<string, string> { ["2509781"] = "엄준철" };
        var d = CleanPotal.Api.Controllers.LegacyMesReportController.HistFields(raw, id => names.TryGetValue(id, out var n) ? n : id);
        Assert.Equal("2026-10-06 16:32", d["tIn"]);
        Assert.Equal("엄준철", d["uInsp"]);
        Assert.Equal("MDC03", d["cleanEq"]);
        Assert.Equal("2026-10-08 04:26", d["cleanIn"]);
        Assert.Equal("0626", d["cleanOutUser"]);
        Assert.Equal("4.39", d["cleanHours"]);
        Assert.Equal("MBS01", d["dryEq"]);
        Assert.Null(d["bakeIn"]);
        Assert.Equal("20", d["etch"]);
        Assert.Equal("OK", d["cleanState"]);
    }

    [Theory]
    [InlineData("6LINE 3층 C반입구", "6LINE")]
    [InlineData("17LINE 3층 B반입구", "17LINE")]
    [InlineData("P1 LINE 7층 #10 Core", "P1 LINE")]
    [InlineData("P4 LINE", "P4 LINE")]
    [InlineData("", "")]
    public void 반입_위치에서_라인만(string shipTo, string expected)
        => Assert.Equal(expected, LegacyMesDb.ShipLine(shipTo));
}
