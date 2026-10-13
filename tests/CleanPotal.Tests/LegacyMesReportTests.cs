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
        Customer = cust, Price = price, AetsOutDay = outDay, Factory = factory,
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
}
