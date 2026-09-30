using CleanPotal.Api.Controllers;
using CleanPotal.Core.DTOs;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>대시보드 이상 알림과 BROKEN 집계 — 정상이면 알림이 비어 있고, 급한 것(빨강)이 앞에 온다.</summary>
public class DashboardAlertTests
{
    private static readonly DateOnly Today = new(2026, 9, 26);

    [Fact]
    public void 모두_정상이면_알림이_없다()
    {
        var alerts = DashboardController.Alerts(
            new DashChecklistDto(Today, "주간", 3, 1, 6, 0, 0, 2),
            new DashHandoverDto(5, 1, 2, 0),
            new DashProdReqDto(3, 0, 1),
            new DashHandoverDto(2, 0, 0, 0));
        Assert.Empty(alerts);
    }

    [Fact]
    public void 출고_지연은_빨강_NG_밀림_요청_지연은_주황_빨강이_앞()
    {
        var alerts = DashboardController.Alerts(
            new DashChecklistDto(Today, "주간", 0, 0, 6, 2, 1, 0),
            new DashHandoverDto(5, 0, 0, 1),
            new DashProdReqDto(3, 2, 0),
            new DashHandoverDto(4, 0, 0, 2));

        Assert.Equal(new[] { "bad", "bad", "warn", "warn", "warn" }, alerts.Select(a => a.Level).ToArray());
        Assert.Equal("기타세정 출고일 지남 1건", alerts[0].Text);
        Assert.Equal("주간세정 출고일 지남 2건", alerts[1].Text);
        Assert.Equal("/weekly", alerts[1].Link);
        Assert.Contains(alerts, a => a.Text == "체크시트(현장) 미조치 NG 2건" && a.Link == "/checklist?tab=ng");
        Assert.Contains(alerts, a => a.Text == "체크시트(현장) 주 1회 점검 밀림 1건");
        Assert.Contains(alerts, a => a.Text == "생산팀 요청 마감 지남 2건");
    }

    [Fact]
    public void 권한이_없어_빠진_카드는_알림도_없다()
        => Assert.Empty(DashboardController.Alerts(null, null, null));

    [Fact]
    public void BROKEN_은_발생일로_이번_달_올해와_올해_공식을_센다()
    {
        var rows = new List<DashboardController.BrokenRow>
        {
            new(new DateOnly(2026, 9, 20), new DateTime(2026, 9, 20), true),
            new(new DateOnly(2026, 9, 24), new DateTime(2026, 9, 25), false),
            new(new DateOnly(2026, 3, 2), new DateTime(2026, 3, 2), true),
            new(new DateOnly(2025, 12, 30), new DateTime(2025, 12, 30), true),   // 작년
            new(null, new DateTime(2026, 9, 10), false),                         // 발생일 없음 → 등록일
        };
        var b = DashboardController.BrokenSummary(rows, Today);
        Assert.Equal(3, b.ThisMonth);
        Assert.Equal(4, b.ThisYear);
        Assert.Equal(2, b.OfficialThisYear);
        Assert.Equal(0, DashboardController.BrokenSummary(Array.Empty<DashboardController.BrokenRow>(), Today).ThisYear);
    }

    [Fact]
    public void 근무일은_07시_전이면_전날이다()
    {
        Assert.Equal(new DateOnly(2026, 9, 30), DashboardController.WorkDate(new DateTime(2026, 10, 1, 6, 59, 0)));
        Assert.Equal(new DateOnly(2026, 10, 1), DashboardController.WorkDate(new DateTime(2026, 10, 1, 7, 0, 0)));
    }

    [Fact]
    public void 설비_체크시트_요약은_항목_없는_주기를_빼고_센다()
    {
        EqCheckCellDto C(string s) => new(s, 0, 0, 0, "");
        EqCheckStatusRowDto R(string d, string w, string m, int ng) => new("X", "METAL", "", "", C(d), C(w), C(m), 0, ng);
        var st = new EqCheckStatusDto(Today, "2026-10-02", new DateOnly(2026, 10, 2), "2026-09", new DateOnly(2026, 9, 30), 26,
            new[] { R("done", "late", "none", 1), R("partial", "done", "todo", 0), R("none", "todo", "done", 2) });
        var e = DashboardController.EqCheckSummary(st);
        Assert.Equal((1, 2), (e.DailyDone, e.DailyUnits));
        Assert.Equal((1, 3, 1), (e.WeeklyDone, e.WeeklyUnits, e.WeeklyLate));
        Assert.Equal((1, 2), (e.MonthlyDone, e.MonthlyUnits));
        Assert.Equal(3, e.OpenNg);

        var alerts = DashboardController.Alerts(null, null, null, null, e);
        Assert.Contains(alerts, a => a.Text == "체크시트(설비) 미조치 NG 3건" && a.Link == "/eq-check?tab=ng");
        Assert.Contains(alerts, a => a.Text == "체크시트(설비) 주간 점검 밀림 1대");
    }

    [Fact]
    public void KOH_폐액_요약은_주야를_합치고_안_적었으면_null()
    {
        WasteLogDto W(DateOnly d, string shift, decimal? used, decimal? inc) =>
            new(d, shift, null, null, used, null, null, inc, "", "", null, "", "");
        var rows = new[] { W(Today, "주간", 1.5m, 2m), W(Today, "야간", 1m, null), W(Today.AddDays(-1), "주간", null, null) };
        var w = DashboardController.WasteSummary(rows, Today);
        Assert.Equal(2.5m, w.CausticUsed);
        Assert.Equal(2m, w.WasteIncrease);
        Assert.Null(w.PrevCausticUsed);
        Assert.Equal(2, w.Shifts);
    }

    [Fact]
    public void BAKE_요약은_가동한_오븐과_이상_건수를_센다()
    {
        BakeLogDto B(string eq, string status, bool soot, bool qtz) =>
            new(Today, "주간", 1, eq, status, null, null, "", "", "", "", "", "", "", "", soot, qtz, "");
        var r = new WorkReportDto(Today,
            new[] { new WorkReportRowDto("METAL", "BAKE", "OV1", "", "", "", null, ""), new WorkReportRowDto("METAL", "BAKE", "OV2", "", "", "", null, ""),
                    new WorkReportRowDto("METAL", "세정", "NBO05", "", "S2 교체", "", null, "") },
            1, new[] { B("OV1", "", true, false), B("OV1", "", false, true), B("OV2", "비가동", false, false) });
        var b = DashboardController.BakeSummary(r);
        Assert.Equal((1, 2, 1, 1), (b.Running, b.Ovens, b.Soot, b.Quartz));
        var c = DashboardController.ChemicalSummary(r);
        Assert.Equal(1, c.Count);
        Assert.Equal("NBO05", c.Codes[0]);
    }
}
