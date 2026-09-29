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
            W(new DateOnly(2019, 2, 1), "야", 3300, 3500, 4.2m, 0.5m),   // KOH 보충 200, 폐액 수거 3.7
            W(new DateOnly(2019, 2, 2), "낮", 1, 1, 1, 1),   // 교대 모름 → 건너뜀
        };
        var r = await svc.ImportWasteAsync(rows, overwrite: false, "엑셀");
        Assert.Equal((4, 1), (r.Added, r.Skipped));
        Assert.Equal(0, (await svc.ImportWasteAsync(rows, overwrite: false, "엑셀")).Added);

        var trend = await svc.GetWasteTrendAsync();
        Assert.Equal(2, trend.Count);
        Assert.Equal((50m, 0.2m, 1, 1), (trend[0].CausticUsed, trend[0].WasteIncrease, trend[0].Days, trend[0].Changes));
        // 보충·수거는 사용·증가에서 빼지 않고 따로 센다
        Assert.Equal((100m, 1.0m, 2), (trend[1].CausticUsed, trend[1].WasteIncrease, trend[1].Changes));
        Assert.Equal((200m, 3.7m), (trend[1].CausticRefill, trend[1].WasteRemoved));
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

    private static BakeSaveRequest B(DateOnly d, string shift, int round, string eq, string status = "",
                                     string sn = "", string soot = "X", string quartz = "無", string note = "정상")
        => status.Length > 0
            ? new(d, shift, round, eq, status, null, null, null, null, null, null, null, null, null, null)
            : new(d, shift, round, eq, "", d.ToDateTime(new TimeOnly(8, 0)), d.ToDateTime(new TimeOnly(16, 0)),
                  "(C)BS_BOAT_SiN", sn, soot, "PN2 30", "CN2 0", "PN2 30", quartz, note);

    [Theory]
    [InlineData("X", false)]
    [InlineData("", false)]
    [InlineData("PN2 30", false)]        // 한 줄 밀려 적힌 온도 값
    [InlineData("TC18-7-239", false)]    // 한 줄 밀려 적힌 S/N
    [InlineData("O", true)]
    [InlineData("상판 테두리 그을음", true)]
    public void 그을음_판정(string soot, bool expected) => Assert.Equal(expected, WorkLogService.IsSoot(soot));

    [Fact]
    public async Task 그을음은_교대_회차_오븐별로_두고_앞_기록을_같이_준다()
    {
        using var t = new TestDb();
        var svc = new WorkLogService(t.Db);
        await svc.SaveBakeAsync(B(D.AddDays(-1), "야", 1, "MBO01-1", "HOLD"), "x");
        await svc.SaveBakeAsync(B(D.AddDays(-1), "주", 1, "MBO01-1", sn: "SM-1"), "x");
        var run = await svc.SaveBakeAsync(B(D, "주", 1, "mbo1-2", sn: "SM-2", soot: "상판 그을음"), "x");
        Assert.Equal("MBO01-2", run!.EqCode);
        Assert.True(run.HasSoot);
        await svc.SaveBakeAsync(B(D, "주", 2, "MBO01-2", sn: "SM-3"), "x");

        var day = await svc.GetBakeDayAsync(D);
        Assert.Equal(new[] { 1, 2 }, day.Rows.Select(r => r.Round));
        Assert.Equal("HOLD", day.PrevRows.Single().Status);              // 전날 마지막 교대(야)
        Assert.Contains(day.Ovens, o => o.Code == "NBO03-2");
        Assert.DoesNotContain(day.Ovens, o => o.Code == "MDC01");

        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveBakeAsync(
            new BakeSaveRequest(D, "주", 1, "MBO01-1", "", D.ToDateTime(new TimeOnly(9, 0)), D.ToDateTime(new TimeOnly(8, 0)),
                null, null, null, null, null, null, null, null), "x"));

        // 모두 비우면 지우고, 회차는 통째로 지울 수 있다
        Assert.Null(await svc.SaveBakeAsync(new BakeSaveRequest(D, "주", 2, "MBO01-2", "", null, null, "", "", "", "", "", "", "", ""), "x"));
        Assert.Single((await svc.GetBakeDayAsync(D)).Rows);
        Assert.Equal(1, await svc.DeleteBakeRoundAsync(D, "주간", 1));
    }

    [Fact]
    public async Task 그을음_가져오기와_보트_이력_찾기()
    {
        using var t = new TestDb();
        var svc = new WorkLogService(t.Db);
        var rows = new List<BakeSaveRequest>
        {
            B(new DateOnly(2019, 1, 31), "야", 1, "MBO01-1", sn: "TC17-1-126"),
            B(new DateOnly(2019, 1, 31), "야", 2, "MBO01-1", sn: "TC17-1-126", soot: "하판 상부 그을음"),
            B(new DateOnly(2019, 1, 31), "야", 1, "NBO04-1", "비가동"),
            B(new DateOnly(2023, 3, 1), "주", 1, "MBO02-1", sn: "SM-B65", quartz: "有"),
            B(new DateOnly(2023, 3, 1), "낮", 1, "MBO02-1", sn: "x"),   // 교대 모름 → 건너뜀
        };
        var r = await svc.ImportBakeAsync(rows, overwrite: false, "엑셀");
        Assert.Equal((4, 1), (r.Added, r.Skipped));
        Assert.Equal(new[] { "NBO04-1" }, r.NewEquipment);
        Assert.Equal(0, (await svc.ImportBakeAsync(rows, overwrite: false, "엑셀")).Added);

        var hist = await svc.SearchBakeAsync("TC17-1", issuesOnly: false);
        Assert.Equal(new[] { 2, 1 }, hist.Rows.Select(x => x.Round));      // 최근 것 먼저
        var issues = await svc.SearchBakeAsync(null, issuesOnly: true);
        Assert.Equal(2, issues.Total);
        Assert.True(issues.Rows[0].HasQuartz);
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SearchBakeAsync(" ", issuesOnly: false));
    }

    [Fact]
    public async Task 양식_목록은_보낸_순서로_저장하고_빠진_것은_지운다()
    {
        using var t = new TestDb();
        var svc = new WorkLogService(t.Db);
        var saved = await svc.SaveFormsAsync(new[]
        {
            new WorkFormSaveItem(0, "4", "세정 작업일지", "", "att:12|세정 작업일지.xlsx|file"),
            new WorkFormSaveItem(0, "6", "설비 점검표", "매월 1일", ""),
        }, "홍길동");
        Assert.Equal(new[] { "4", "6" }, saved.Select(f => f.No));
        Assert.Equal("홍길동", saved[0].UpdatedBy);

        saved = await svc.SaveFormsAsync(new[] { new WorkFormSaveItem(saved[1].Id, saved[1].No, "설비 점검표(개정)", saved[1].Description, saved[1].FileRef) }, "x");
        Assert.Equal("설비 점검표(개정)", saved.Single().Title);
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveFormsAsync(new[] { new WorkFormSaveItem(0, "", "나쁜 파일", "", "C:\\a.xlsx") }, "x"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveFormsAsync(new[] { new WorkFormSaveItem(0, "1", " ", "", "") }, "x"));
    }
}
