using System.Net;
using System.Text.Json;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>대시보드 요약 — 카드는 그 메뉴를 볼 수 있는 사람에게만 온다.</summary>
[Collection(PortalAppCollection.Name)]
public class DashboardEndpointTests
{
    private readonly PortalAppFixture _app;
    public DashboardEndpointTests(PortalAppFixture app) => _app = app;

    private async Task<JsonElement> SummaryAsync(string who)
    {
        using var client = await _app.SignInAsync(who);
        var res = await client.GetAsync("/api/dashboard/summary");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").Clone();
    }

    private static bool Has(JsonElement d, string card) => d.GetProperty(card).ValueKind == JsonValueKind.Object;

    [Fact]
    public async Task 현장_조회_등급은_체크시트만_본다()
    {
        var d = await SummaryAsync("field-view");
        Assert.True(Has(d, "checklist"));
        Assert.False(d.TryGetProperty("sensors", out _));   // 온·습도 카드는 뺐다
        Assert.True(Has(d, "icpms"));
        foreach (var card in new[] { "mes", "dispatch", "reports" }) Assert.False(Has(d, card), card);
        Assert.False(Has(d, "handover"));
        Assert.False(Has(d, "prodReq"));
    }

    [Fact]
    public async Task 관리자는_모든_카드를_본다()
    {
        var d = await SummaryAsync("admin");
        foreach (var card in new[] { "checklist", "handover", "prodReq", "mes", "dispatch", "icpms", "reports" }) Assert.True(Has(d, card), card);
        Assert.True(d.GetProperty("reports").GetProperty("meetingVisible").GetBoolean());
        Assert.True(d.GetProperty("reports").GetProperty("weeklyVisible").GetBoolean());
        Assert.Equal(JsonValueKind.Array, d.GetProperty("alerts").ValueKind);
    }

    [Fact]
    public async Task MES_권한만_있으면_MES_카드만_본다()
    {
        var d = await SummaryAsync("mes-view");
        Assert.True(Has(d, "mes"));
        foreach (var card in new[] { "checklist", "handover", "prodReq", "dispatch", "icpms", "reports" }) Assert.False(Has(d, card), card);
    }

    [Fact]
    public async Task 인수인계_권한이면_배차와_인수인계_작성_여부를_보고_주간보고는_못_본다()
    {
        var d = await SummaryAsync("field-only");   // 현장 점검·인수인계 편집, OFFICE 없음
        Assert.True(Has(d, "dispatch"));
        var r = d.GetProperty("reports");
        Assert.True(r.GetProperty("meetingVisible").GetBoolean());
        Assert.False(r.GetProperty("weeklyVisible").GetBoolean());
        Assert.False(Has(d, "mes"));
    }

    [Fact]
    public async Task 토큰이_없으면_401()
    {
        using var client = _app.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dashboard/summary")).StatusCode);
    }
}
