using CleanPotal.Api.Controllers;
using CleanPotal.Core.DTOs;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>대시보드 맨 위 이상 알림 — 정상이면 비어 있고, 급한 것(빨강)이 앞에 온다.</summary>
public class DashboardAlertTests
{
    private static readonly DateOnly Today = new(2026, 9, 26);

    [Fact]
    public void 모두_정상이면_알림이_없다()
    {
        var alerts = DashboardController.Alerts(
            new DashChecklistDto(Today, "주간", 3, 1, 6, 0, 0, 2),
            new DashHandoverDto(5, 1, 2, 0),
            new DashProdReqDto(3, 0, 1));
        Assert.Empty(alerts);
    }

    [Fact]
    public void 출고_지연은_빨강_NG_밀림_요청_지연은_주황_빨강이_앞()
    {
        var alerts = DashboardController.Alerts(
            new DashChecklistDto(Today, "주간", 0, 0, 6, 2, 1, 0),
            new DashHandoverDto(5, 0, 0, 1),
            new DashProdReqDto(3, 2, 0));

        Assert.Equal(new[] { "bad", "warn", "warn", "warn" }, alerts.Select(a => a.Level).ToArray());
        Assert.Equal("기타세정 출고일 지남 1건", alerts[0].Text);
        Assert.Equal("/handover", alerts[0].Link);
        Assert.Contains(alerts, a => a.Text == "체크시트 미조치 NG 2건" && a.Link == "/checklist?tab=ng");
        Assert.Contains(alerts, a => a.Text == "체크시트 주 1회 점검 밀림 1건");
        Assert.Contains(alerts, a => a.Text == "생산팀 요청 마감 지남 2건");
    }

    [Fact]
    public void 권한이_없어_빠진_카드는_알림도_없다()
        => Assert.Empty(DashboardController.Alerts(null, null, null));
}
