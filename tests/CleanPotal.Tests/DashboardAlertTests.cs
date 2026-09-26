using CleanPotal.Api.Controllers;
using CleanPotal.Core.DTOs;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>대시보드 맨 위 이상 알림 — 정상이면 비어 있고, 급한 것(빨강)이 앞에 온다.</summary>
public class DashboardAlertTests
{
    private static readonly DateOnly Today = new(2026, 9, 26);
    private static DashSensorDto Sensor(string name, string status) => new(name, 23, 60, status, null);

    [Fact]
    public void 모두_정상이면_알림이_없다()
    {
        var alerts = DashboardController.Alerts(
            new DashChecklistDto(Today, "주간", 3, 1, 6, 0, 0, 2),
            new DashSensorsDto(true, 2, 2, DateTime.Now, new[] { Sensor("1번", "normal"), Sensor("2번", "normal") }),
            new DashHandoverDto(5, 1, 2, 0),
            new DashProdReqDto(3, 0, 1));
        Assert.Empty(alerts);
    }

    [Fact]
    public void 센서_끊김과_지연은_빨강_NG_와_밀림은_주황_빨강이_앞()
    {
        var alerts = DashboardController.Alerts(
            new DashChecklistDto(Today, "주간", 0, 0, 6, 2, 1, 0),
            new DashSensorsDto(true, 3, 4, DateTime.Now, new[] { Sensor("1번", "normal"), Sensor("2번", "alert"), Sensor("3번", "warn"), Sensor("4번", "offline") }),
            new DashHandoverDto(5, 0, 0, 1),
            new DashProdReqDto(3, 2, 0));

        Assert.Equal(new[] { "bad", "bad", "bad", "warn", "warn", "warn", "warn" }, alerts.Select(a => a.Level).ToArray());
        Assert.Contains(alerts, a => a.Text == "온·습도 센서 1곳 수신 없음");
        Assert.Contains(alerts, a => a.Text == "온·습도 기준 초과: 2번");
        Assert.Contains(alerts, a => a.Text == "기타세정 출고일 지남 1건" && a.Link == "/handover");
        Assert.Contains(alerts, a => a.Text == "체크시트 미조치 NG 2건" && a.Link == "/checklist?tab=ng");
        Assert.Contains(alerts, a => a.Text == "생산팀 요청 마감 지남 2건");
    }

    [Fact]
    public void 수집이_끊기면_센서별이_아니라_수집_끊김으로_알린다()
    {
        var alerts = DashboardController.Alerts(null,
            new DashSensorsDto(false, 0, 4, DateTime.Now.AddHours(-4), new[] { Sensor("1번", "offline") }), null, null);
        Assert.Single(alerts);
        Assert.Equal("온·습도 수집이 끊겼습니다", alerts[0].Text);
    }

    [Fact]
    public void 권한이_없어_빠진_카드는_알림도_없다()
        => Assert.Empty(DashboardController.Alerts(null, null, null, null));
}
