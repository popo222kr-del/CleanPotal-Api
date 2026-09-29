using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>업무 파일 통합 관리 — 설비 목록·약액 교체 기록·업무보고.</summary>
public class WorkLogServiceTests
{
    private static readonly DateOnly D = new(2026, 9, 29);

    [Theory]
    [InlineData("MSC1-1", "MSC01-1")]
    [InlineData(" mdc 1 ", "MDC01")]
    [InlineData("NBO03-2", "NBO03-2")]
    public void 설비_코드는_같은_모양으로_맞춘다(string raw, string expected)
        => Assert.Equal(expected, WorkLogService.NormalizeCode(raw));

    [Theory]
    [InlineData("S2 100%,HF100%", "S2 100%, HF 100%")]
    [InlineData("S2100%", "S2 100%")]
    [InlineData("S2 50% , HF 100 %", "S2 50%, HF 100%")]
    public void 교체_내용은_띄어쓰기를_맞춘다(string raw, string expected)
        => Assert.Equal(expected, WorkLogService.NormalizeContent(raw));

    [Fact]
    public async Task 처음_열면_업무보고의_설비_목록으로_채운다()
    {
        using var t = new TestDb();
        var eq = await new WorkLogService(t.Db).GetEquipmentAsync();
        Assert.Equal("MDC01", eq[0].Code);
        Assert.Equal("POLY(L10)", eq[0].Process);
        Assert.Equal("BAKE", eq.Single(e => e.Code == "NBO03-2").Kind);
        Assert.Equal("N-METAL", eq.Single(e => e.Code == "NDC05").Line);
    }

    [Fact]
    public async Task 칸을_저장하고_내용과_메모를_비우면_지운다()
    {
        using var t = new TestDb();
        var svc = new WorkLogService(t.Db);
        await svc.GetEquipmentAsync();
        var saved = await svc.SaveChemicalAsync(new ChemicalSaveRequest(D, "mdc05", "S2 100%,HF100%", "사용횟수 : 111회"), "홍길동");
        Assert.Equal("MDC05", saved!.EqCode);
        Assert.Equal("S2 100%, HF 100%", saved.Content);

        var month = await svc.GetChemicalMonthAsync(2026, 9);
        Assert.Single(month.Cells);

        Assert.Null(await svc.SaveChemicalAsync(new ChemicalSaveRequest(D, "MDC05", "", " "), "홍길동"));
        Assert.Empty((await svc.GetChemicalMonthAsync(2026, 9)).Cells);
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveChemicalAsync(new ChemicalSaveRequest(D, "없는설비1", "S2 100%", ""), "x"));
    }

    [Fact]
    public async Task 엑셀을_가져오면_모르는_설비는_목록에_넣고_덮어쓰기를_고른다()
    {
        using var t = new TestDb();
        var svc = new WorkLogService(t.Db);
        await svc.SaveChemicalAsync(new ChemicalSaveRequest(D, "MDC01", "S2 50%", ""), "웹");

        var cells = new List<ChemicalImportCell>
        {
            new(D, "MDC01", "S2 100%,HF100%", ""),
            new(D, "MSC1-1", "S2 100%", "사용횟수 : 6회"),
            new(D.AddDays(-1), "MDC02", "", ""),   // 빈 칸
        };
        var r = await svc.ImportChemicalAsync(cells, overwrite: false, "엑셀");
        Assert.Equal(1, r.Added);
        Assert.Equal(2, r.Skipped);
        Assert.Equal(new[] { "MSC01-1" }, r.NewEquipment);
        Assert.Equal("S2 50%", (await svc.GetChemicalMonthAsync(2026, 9)).Cells.Single(c => c.EqCode == "MDC01").Content);

        r = await svc.ImportChemicalAsync(cells, overwrite: true, "엑셀");
        Assert.Equal(2, r.Updated);
        Assert.Equal("S2 100%, HF 100%", (await svc.GetChemicalMonthAsync(2026, 9)).Cells.Single(c => c.EqCode == "MDC01").Content);
    }

    [Fact]
    public async Task 업무보고는_그날_교체와_마지막_교체일을_붙인다()
    {
        using var t = new TestDb();
        var svc = new WorkLogService(t.Db);
        await svc.GetEquipmentAsync();
        await svc.SaveChemicalAsync(new ChemicalSaveRequest(D, "MDC04", "S2 50%, HF 100%", ""), "x");
        await svc.SaveChemicalAsync(new ChemicalSaveRequest(D.AddDays(-10), "MDC06", "S2 100%", ""), "x");
        await svc.SaveChemicalAsync(new ChemicalSaveRequest(D.AddDays(-3), "MDC06", "", "Heater 교체"), "x");   // 메모만 — 교체 아님

        var report = await svc.GetReportAsync(D);
        Assert.Equal(1, report.ChangedCount);
        Assert.Equal("S2 50%, HF 100%", report.Rows.Single(x => x.Code == "MDC04").Content);
        var mdc06 = report.Rows.Single(x => x.Code == "MDC06");
        Assert.Equal("", mdc06.Content);
        Assert.Equal(D.AddDays(-10), mdc06.LastChangeDate);
    }

    private static WasteSaveRequest W(DateOnly d, string shift, decimal? cb, decimal? ca, decimal? wb, decimal? wa, string dip = "")
        => new(d, shift, cb, ca, wb, wa, dip, "", null, "");

    [Fact]
    public async Task 폐액은_날짜_교대별로_두고_감소량_증가량을_계산한다()
    {
        using var t = new TestDb();
        var svc = new WorkLogService(t.Db);
        await svc.SaveWasteAsync(W(new DateOnly(2026, 8, 31), "야", 2400, 2300, 6.0m, 7.1m), "x");
        var day = await svc.SaveWasteAsync(W(D, "주", 2300, 2250, 7.1m, 9.6m, "NDC02, MDC01"), "x");
        Assert.Equal(50, day!.CausticUsed);
        Assert.Equal(2.5m, day.WasteIncrease);
        await svc.SaveWasteAsync(W(D, "야간", 2250, null, 9.6m, 9.6m), "x");   // "야간" 도 야로

        var m = await svc.GetWasteMonthAsync(2026, 9);
        Assert.Equal(new[] { "주", "야" }, m.Rows.Select(r => r.Shift));
        Assert.Null(m.Rows[1].CausticUsed);                  // 現 이 빠지면 계산하지 않는다
        Assert.Equal(2300, m.PrevCausticAfter);              // 달 첫 줄 前 값 채우기용
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveWasteAsync(W(D, "오후", 1, 1, 1, 1), "x"));

        // 모두 비우면 지운다
        Assert.Null(await svc.SaveWasteAsync(W(D, "야", null, null, null, null), "x"));
        Assert.Single((await svc.GetWasteMonthAsync(2026, 9)).Rows);
    }

    [Fact]
    public async Task 폐액_엑셀_가져오기와_월별_추이()
    {
        using var t = new TestDb();
        var svc = new WorkLogService(t.Db);
        var rows = new List<WasteSaveRequest>
        {
            W(new DateOnly(2019, 1, 1), "주", 3450, 3450, 3, 3),
            W(new DateOnly(2019, 1, 1), "야", 3450, 3400, 3, 3.2m, "MSC02-1"),
            W(new DateOnly(2019, 2, 1), "주", 3400, 3300, 3.2m, 4.2m, "MDC01, NDC02"),
            W(new DateOnly(2019, 2, 1), "낮", 1, 1, 1, 1),   // 교대 모름 → 건너뜀
        };
        var r = await svc.ImportWasteAsync(rows, overwrite: false, "엑셀");
        Assert.Equal((3, 1), (r.Added, r.Skipped));
        Assert.Equal(0, (await svc.ImportWasteAsync(rows, overwrite: false, "엑셀")).Added);

        var trend = await svc.GetWasteTrendAsync();
        Assert.Equal(2, trend.Count);
        Assert.Equal((50m, 0.2m, 1, 1), (trend[0].CausticUsed, trend[0].WasteIncrease, trend[0].Days, trend[0].Changes));
        Assert.Equal((100m, 1.0m, 2), (trend[1].CausticUsed, trend[1].WasteIncrease, trend[1].Changes));
    }

    [Fact]
    public async Task 설비_코드를_바꾸면_지난_기록도_따라가고_빠진_설비는_끈다()
    {
        using var t = new TestDb();
        var svc = new WorkLogService(t.Db);
        var eq = await svc.GetEquipmentAsync();
        await svc.SaveChemicalAsync(new ChemicalSaveRequest(D, "MSC02-1", "S2 100%", ""), "x");

        var items = eq.Select(e => e.Code == "MSC02-1" ? e with { Code = "MSC01-1" } : e).Where(e => e.Code != "NDC08").ToList();
        var saved = await svc.SaveEquipmentAsync(items);
        Assert.Contains(saved, e => e.Code == "MSC01-1");
        Assert.False(saved.Single(e => e.Code == "NDC08").IsActive);
        Assert.Equal("MSC01-1", (await svc.GetChemicalMonthAsync(2026, 9)).Cells.Single().EqCode);
    }
}
