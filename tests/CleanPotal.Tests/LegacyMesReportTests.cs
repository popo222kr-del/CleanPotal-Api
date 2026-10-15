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
    public void 출하는_구분_라인_월별로_더하고_창고_재고를_옆에()
    {
        var shipped = new[]
        {
            Lot("A", "", "15 LINE", price: 500, outDay: "20260115"),
            Lot("A", "", "15 LINE", price: 500, outDay: "2026-01-20"),
            Lot("B", "", "P2 LINE", price: 1200, outDay: "2026/03/02 10:00"),
            Lot("C", "", "P2 LINE", cust: "10002", price: null, outDay: "20260302"),
            Lot("D", "", "", outDay: ""),            // 출고일 없음
            Lot("E", "", "", outDay: "20251231"),    // 다른 해
        };
        // 실제 출하 시각(SHIP_TIME)이 있으면 그걸로 — LOT_CMF_6(예정일)이 다른 달이어도
        var real = new LegacyLot { LotId = "R", MatId = "A", Line = "15 LINE", Customer = "10001", Price = 100, DueTime = "20260530", ShipTime = "20260601083000", Factory = "CUSTOMER" };
        var stock = new[] { Lot("S1", "8100", "15 LINE", price: 70), Lot("S2", "8000", "P2 LINE", price: 30), Lot("S3", "3000", "15 LINE") };
        var r = LegacyMesReports.Shipments(shipped.Append(real), stock, 2026,
            l => l.Customer == "10001" ? ("MEMORY", "MEMORY", 0) : l.Customer == "10002" ? ("FOUNDRY", "FOUNDRY", 1) : ("c:" + l.Customer, l.Customer, 100));

        Assert.Equal(5, r.QtyTotal);
        Assert.Equal(2300, r.AmountTotal);
        Assert.Equal(2, r.NoDate);
        Assert.Equal(new[] { "MEMORY", "FOUNDRY" }, r.Groups.Select(g => g.Name));
        var mem = r.Groups[0];
        var l15 = mem.Lines.Single(l => l.Line == "15 LINE");
        Assert.Equal(2, l15.Qty[0]);
        Assert.Equal(1000, l15.Amount[0]);
        Assert.Equal(1, l15.Qty[5]);                     // 6월 — SHIP_TIME 기준
        Assert.Equal(0, l15.Qty[4]);
        Assert.Equal(new decimal[] { 1, 70, 0, 0 }, l15.Stock);   // 동탄창고 1개 70원
        Assert.Equal(new decimal[] { 1, 70, 1, 30 }, r.Stock);
        Assert.Equal(0, r.Groups[1].AmountTotal);        // 단가 없는 품목은 금액 0
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

    [Fact]
    public void 단가표는_출하일에_적용_중인_단가_없으면_품목_마스터()
    {
        var book = new PriceBook(new[] { ("610005478", "20230424", 43500m), ("610005478", "20260301", 45000m), ("X", "bad", 1m) });
        Assert.Equal(43500m, book.At("610005478", "20260115"));
        Assert.Equal(45000m, book.At("610005478", "20260301083000"));
        Assert.Null(book.At("610005478", "20230101"));      // 첫 적용일보다 이름
        Assert.Null(book.At("NONE", "20260101"));

        var lot = new LegacyLot { LotId = "L", MatId = "610005478", Line = "15 LINE", Customer = "10001", Price = 40000m, CreateTime = "20260520100000", ShipTime = "20260601083000", Factory = "CUSTOMER" };
        var other = new LegacyLot { LotId = "M", MatId = "NOPRICE", Line = "15 LINE", Customer = "10001", Price = 1000m, CreateTime = "20260525", ShipTime = "20260602", Factory = "CUSTOMER" };
        var r = LegacyMesReports.Shipments(new[] { lot, other }, Array.Empty<LegacyLot>(), 2026, _ => ("MEMORY", "MEMORY", 0), prices: book);
        Assert.Equal(46000m, r.Amount[5]);                   // 45000(단가표) + 1000(마스터)
    }

    [Fact]
    public void 단가는_입고일에_적용_중인_값_기존_MES_와_같게()
    {
        // WOOAM_TUBE-TANK CIRCULATION: 2021-11-01 100,000 → 2026-01-13 35,000
        var book = new PriceBook(new[] { ("700021237", "20211101", 100000m), ("700021237", "20260113", 35000m) });
        LegacyLot L(string id, string created, string shipped) => new() { LotId = id, MatId = "700021237", Line = "WOOAM", Customer = "C", Qty = 1, CreateTime = created, ShipTime = shipped, Factory = "CUSTOMER" };
        var r = LegacyMesReports.Shipments(new[] { L("1SA260112069P", "20260112090000", "20260114100000"), L("1SA260113173P", "20260113090000", "20260115100000") },
            Array.Empty<LegacyLot>(), 2026, _ => ("C", "C", 0), prices: book);
        Assert.Equal(135000m, r.Amount[0]);                  // 01-12 입고 100,000 + 01-13 입고 35,000
    }

    [Fact]
    public void 출하_수량과_금액은_LOT_안의_개수_QTY_1_기준()
    {
        // 2026-01 대조: 1SA260122155P AEROSOL NOZZLE TIP — QTY_1 60, 단가 70,000 → MES 4,200,000
        var nozzle = new LegacyLot { LotId = "1SA260122155P", MatId = "900060011", Line = "SEM", Customer = "C", Price = 70000m, Qty = 60m, ShipTime = "20260122", Factory = "CUSTOMER" };
        var plate = new LegacyLot { LotId = "P", MatId = "600054050", Line = "7LINE", Customer = "10001", Price = 10000m, Qty = 1m, ShipTime = "20260105", Factory = "CUSTOMER" };
        var noQty = new LegacyLot { LotId = "Z", MatId = "X", Line = "7LINE", Customer = "10001", Price = 500m, Qty = 0m, ShipTime = "20260106", Factory = "CUSTOMER" };
        var r = LegacyMesReports.Shipments(new[] { nozzle, plate, noQty }, Array.Empty<LegacyLot>(), 2026, l => (l.Customer, l.Customer, 0));
        Assert.Equal(62m, r.Qty[0]);
        Assert.Equal(4_200_000m + 10_000m + 500m, r.Amount[0]);
    }

    [Theory]
    [InlineData("무상세정", true)]
    [InlineData("유상세정", false)]
    [InlineData("", false)]
    public void 무상세정_판별(string paid, bool free)
        => Assert.Equal(free, LegacyMesReports.IsFree(new LegacyLot { Paid = paid }));

    [Fact]
    public void 무상세정_LOT_은_단가_0원()
    {
        Assert.Equal(0, LegacyMesReports.UnitPrice(new LegacyLot { Paid = "무상세정", Price = 300000 }, null, "2026-01-01"));
        Assert.Equal(300000, LegacyMesReports.UnitPrice(new LegacyLot { Paid = "유상세정", Price = 300000 }, null, "2026-01-01"));
    }

    [Fact]
    public void MES_화면과_다른_이유는_라인_코드표에_없는_라인과_무상세정()
    {
        LegacyLot L(string id, string line, string ship, decimal price, string paid = "유상세정", string kind = "")
            => new() { LotId = id, MatId = id, Line = line, Customer = "10011", Price = price, ShipTime = ship, Paid = paid, Factory = "CUSTOMER", Kind = kind };
        var lots = new[]
        {
            L("A", "ZEUS", "20260108090000", 480000), L("B", "ZEUS", "20260408090000", 200000),
            L("C", "제우스", "20260115090000", 1920000),                      // 코드표에 있음 → MES 와 같음
            L("D", "U3LINE", "20260120090000", 1000), L("E", "U3LINE", "20251220090000", 1000),   // 지난해 출하는 안 센다
            L("F", "제우스", "20260201090000", 300000, "무상세정"),
            L("G", "", "20260301090000", 80000, kind: LegacyLot.KindChemical),                   // 케미칼은 라인이 없어도 MES 에 있다
        };
        var gap = LegacyMesReports.Gap(lots, 2026, new HashSet<string> { "제우스", "WOOAM" }, c => c == "10011" ? "제우스" : c, null);
        Assert.Equal(new[] { "ZEUS", "U3LINE" }, gap.Lines.Select(l => l.Line));
        Assert.Equal(480000, gap.Lines[0].Amount[0]);
        Assert.Equal(200000, gap.Lines[0].Amount[3]);
        Assert.Equal("제우스", gap.Lines[0].Customers);
        Assert.Equal(1, gap.Lines[1].Lots);
        Assert.Equal(681000, gap.AmountTotal);
        Assert.Equal(3, gap.Lots);
        Assert.Equal((1, 1m), (gap.FreeLots, gap.FreeQty));
        Assert.Null(gap.Skip);

        // 삼성 세정 SKIP 은 따로 한 줄 — 라인 코드표에 없는 라인이어도 SKIP 쪽에서만 센다(두 번 세지 않음)
        var withSkip = LegacyMesReports.Gap(lots, 2026, new HashSet<string> { "제우스", "WOOAM" }, c => c, null, l => l.LotId is "A" or "C");
        Assert.Equal((2, 2400000m), (withSkip.Skip!.Lots, withSkip.Skip.AmountTotal));
        Assert.Equal(new[] { "ZEUS", "U3LINE" }, withSkip.Lines.Select(l => l.Line));
        Assert.Equal(1, withSkip.Lines[0].Lots);
        Assert.Equal(2400000 + 200000 + 1000, withSkip.AmountTotal);
    }

    [Fact]
    public void 단가_이력이_없는_LOT_은_MES_차이에_따로()
    {
        var book = new PriceBook(new[] { ("A", "20250101", 100m) });
        LegacyLot L(string id, string mat, decimal? price, string paid = "유상세정")
            => new() { LotId = id, MatId = mat, Line = "SEM", Customer = "10023", Price = price, CreateTime = "20260616", ShipTime = "20260623090000", Paid = paid, Factory = "CUSTOMER" };
        var lots = new[] { L("1", "A", 100), L("2", "HEATER", 200000), L("3", "HEATER", 200000), L("4", "HEATER", 200000, "무상세정"), L("5", "NEW", null) };
        Assert.True(LegacyMesReports.NoPriceHistory(lots[1], book));
        Assert.False(LegacyMesReports.NoPriceHistory(lots[0], book));
        Assert.False(LegacyMesReports.NoPriceHistory(lots[3], book));   // 무상은 금액 0
        Assert.False(LegacyMesReports.NoPriceHistory(lots[4], book));   // 단가가 아예 없으면 웹도 0
        var gap = LegacyMesReports.Gap(lots, 2026, new HashSet<string> { "SEM" }, c => c, book);
        Assert.Equal((2, 400000m), (gap.NoPrice!.Lots, gap.NoPrice.AmountTotal));
        Assert.Empty(gap.Lines);
        Assert.Equal(400000, gap.AmountTotal);
    }

    [Fact]
    public void 미세정_SKIP_은_수량만_세고_금액_0원_MES_차이에도_안_나온다()
    {
        var lot = new LegacyLot { LotId = "R", MatId = "700021115", Line = "ZEUS", Customer = "10001", Price = 35000, ShipTime = "20260826090000", Factory = "CUSTOMER", NoCharge = true };
        Assert.Equal(0, LegacyMesReports.UnitPrice(lot, null, "20260826"));
        var r = LegacyMesReports.Shipments(new[] { lot }, Array.Empty<LegacyLot>(), 2026, _ => ("MEMORY", "MEMORY", 0));
        Assert.Equal((1m, 0m), (r.QtyTotal, r.AmountTotal));
        var gap = LegacyMesReports.Gap(new[] { lot }, 2026, new HashSet<string>(), c => c, null);
        Assert.Empty(gap.Lines);
        Assert.Equal(0, gap.AmountTotal);
    }

    [Fact]
    public void Daily_출하_실적은_구분별_TYPE_수량과_당일_월_년_누계_작년()
    {
        LegacyMesReports.DailySale S(string day, string g, string t, decimal amt, decimal q = 1) => new(DateOnly.Parse(day), g, t, q, amt);
        var sales = new[]
        {
            S("2026-10-15", "Memory", "U", 100), S("2026-10-15", "Memory", "S", 50, 2), S("2026-10-15", "SEMES", "A", 30),
            S("2026-10-02", "Memory", "N", 10), S("2026-03-01", "Foundry", "D", 7), S("2026-10-16", "Memory", "U", 999),   // 그 뒤는 안 센다
            S("2025-10-10", "Memory", "U", 40), S("2025-12-01", "Memory", "U", 60),
        };
        var r = LegacyMesReports.DailyShip(sales, new DateOnly(2026, 10, 15), SalesTrend.ParseGroups(null));
        var mem = r.Rows.Single(x => x.Name == "Memory");
        Assert.Equal(new decimal[] { 1, 0, 2, 0, 0 }, mem.Types);
        Assert.Equal((3m, 150m, 160m, 160m, 40m, 100m), (mem.Qty, mem.Day, mem.Month, mem.Year, mem.LyYtd, mem.LyYear));
        Assert.Equal(1, r.Rows.Single(x => x.Name == "SEMES").Types[3]);
        var sec = r.Rows.Single(x => x.Name == "SEC 소계");
        Assert.Equal(167m, sec.Year);
        var tot = r.Rows.Last();
        Assert.True(tot.Total);
        Assert.Equal((4m, 180m), (tot.Qty, tot.Day));
        Assert.Equal(2025, r.LastYear);
    }
}
