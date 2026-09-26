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

    private static readonly string[] Cards = { "checklist", "handover", "weekly", "prodReq", "dispatch", "broken" };

    [Fact]
    public async Task 현장_조회_등급은_체크시트만_본다()
    {
        var d = await SummaryAsync("field-view");
        Assert.True(Has(d, "checklist"));
        foreach (var card in Cards.Except(new[] { "checklist" })) Assert.False(Has(d, card), card);
        foreach (var gone in new[] { "sensors", "mes", "icpms", "inventory", "reports" })
            Assert.False(d.TryGetProperty(gone, out _), gone);   // 써 보고 뺀 카드
    }

    [Fact]
    public async Task 관리자는_모든_카드를_본다()
    {
        var d = await SummaryAsync("admin");
        foreach (var card in Cards) Assert.True(Has(d, card), card);
        Assert.Equal(JsonValueKind.Array, d.GetProperty("alerts").ValueKind);
    }

    [Fact]
    public async Task 현장_인수인계_OFFICE_권한이_없으면_카드가_없다()
    {
        var d = await SummaryAsync("mes-view");
        foreach (var card in Cards) Assert.False(Has(d, card), card);
    }

    [Fact]
    public async Task 인수인계_권한이면_기타세정_주간세정_요청_배차를_보고_BROKEN_은_못_본다()
    {
        var d = await SummaryAsync("field-only");   // 현장 점검·인수인계 편집, OFFICE 없음
        foreach (var card in new[] { "checklist", "handover", "weekly", "prodReq", "dispatch" }) Assert.True(Has(d, card), card);
        Assert.False(Has(d, "broken"));
    }

    [Fact]
    public async Task 토큰이_없으면_401()
    {
        using var client = _app.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dashboard/summary")).StatusCode);
    }
}
