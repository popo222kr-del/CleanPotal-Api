using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CleanPotal.Api.Infrastructure;
using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Security;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>외부 접속 보안 — 사내/사외 판정, 사외 차단 메뉴, 관리자 영역 차단.</summary>
public class ExternalAccessTests
{
    private static readonly string[] Private = { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" };

    [Theory]
    [InlineData("10.10.40.61", true)]
    [InlineData("10.10.10.13", true)]
    [InlineData("172.20.1.5", true)]
    [InlineData("172.32.0.1", false)]     // 172.16/12 밖
    [InlineData("192.168.0.10", true)]
    [InlineData("211.234.10.20", false)]  // 통신사 공인 IP(모바일 데이터)
    [InlineData("127.0.0.1", true)]       // 서버 자신은 늘 사내
    [InlineData("::1", true)]
    [InlineData("::ffff:10.10.10.13", true)]   // IPv6 로 들어온 IPv4
    [InlineData("::ffff:8.8.8.8", false)]
    [InlineData("2001:db8::1", false)]
    public void 사설_대역_판정(string ip, bool expected)
        => Assert.Equal(expected, ExternalAccessPolicy.IsInternal(IPAddress.Parse(ip), Private));

    [Fact]
    public void 주소를_모르면_사외()
        => Assert.False(ExternalAccessPolicy.IsInternal(null, Private));

    [Theory]
    [InlineData("10.10.0.0/16", "10.10.255.1", true)]
    [InlineData("10.10.0.0/16", "10.11.0.1", false)]
    [InlineData("10.10.10.13", "10.10.10.13", true)]    // IP 하나
    [InlineData("10.10.10.13", "10.10.10.14", false)]
    [InlineData("10.10.10.0/25", "10.10.10.127", true)] // 바이트 중간에서 끊기는 대역
    [InlineData("10.10.10.0/25", "10.10.10.128", false)]
    [InlineData("0.0.0.0/0", "8.8.8.8", true)]
    public void 대역_하나_판정(string range, string ip, bool expected)
        => Assert.Equal(expected, ExternalAccessPolicy.IsInternal(IPAddress.Parse(ip), new[] { range }));

    [Theory]
    [InlineData("10.0.0.0/8", true)]
    [InlineData(" 192.168.1.10 ", true)]
    [InlineData("10.0.0.0/33", false)]
    [InlineData("10.0.0/8", false)]
    [InlineData("abc", false)]
    [InlineData("10.0.0.0/x", false)]
    [InlineData("", false)]
    public void 대역_글자_해석(string text, bool ok)
        => Assert.Equal(ok, ExternalAccessPolicy.TryParseRange(text, out _, out _));

    [Fact]
    public void 설정이_없거나_깨지면_기본값_제한은_꺼짐()
    {
        foreach (var raw in new[] { null, "", "not json" })
        {
            var c = ExternalAccessPolicy.Parse(raw);
            Assert.False(c.Enforce);
            Assert.Contains("10.0.0.0/8", c.InternalRanges);
        }
        var p = ExternalAccessPolicy.Parse("{\"enforce\":true,\"internalRanges\":[\" 10.10.0.0/16 \",\"\"],\"externalHidden\":[\"/quotation\",\"bad\"]}");
        Assert.True(p.Enforce);
        Assert.Equal(new[] { "10.10.0.0/16" }, p.InternalRanges);
        Assert.Equal(new[] { "/quotation" }, p.ExternalHidden);
    }

    private static UserDto Dto(bool admin, string hidden) => new(
        1, "u", "이름", "", "", "", "", "", "", "", "", "", false, "", admin, 1, 1, 1, 1, 1, 1, "", hidden);

    [Fact]
    public void 사외이면_화면용_숨김에_사외_차단_메뉴를_더한다()
    {
        var u = ExternalAccessPolicy.ForClient(Dto(false, "[\"/icpms\"]"), new[] { "/quotation" });
        Assert.True(u.IsExternal);
        Assert.Equal(new[] { "/icpms", "/quotation" }, JsonSerializer.Deserialize<string[]>(u.HiddenMenus));

        // 관리자는 원래 숨김을 쓰지 않는다 — 사외 차단 메뉴만
        var a = ExternalAccessPolicy.ForClient(Dto(true, "[\"/icpms\"]"), new[] { "/quotation" });
        Assert.Equal(new[] { "/quotation" }, JsonSerializer.Deserialize<string[]>(a.HiddenMenus));

        // 사내(제한 없음)면 그대로
        var same = Dto(false, "[]");
        Assert.Same(same, ExternalAccessPolicy.ForClient(same, null));
    }

    private static async Task<bool> GateAsync(TestDb t, User user, string route, HashSet<string>? extHidden)
    {
        t.Db.Users.Add(user);
        await t.Db.SaveChangesAsync();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", user.Id.ToString()) }, "test")),
        };
        if (extHidden is not null) http.Items[ExternalAccessPolicy.ItemKey] = extHidden;
        var descriptor = new ActionDescriptor { EndpointMetadata = new List<object> { new MenuGateAttribute(route) } };
        var ctx = new ActionExecutingContext(new ActionContext(http, new RouteData(), descriptor),
            new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
        var called = false;
        await new MenuGateFilter(t.Db).OnActionExecutionAsync(ctx, () =>
        {
            called = true;
            return Task.FromResult(new ActionExecutedContext(ctx, new List<IFilterMetadata>(), new object()));
        });
        return called;
    }

    [Fact]
    public async Task 사외_차단_메뉴는_관리자도_서버에서_막힌다()
    {
        using var t = new TestDb();
        var ext = new HashSet<string> { "/quotation" };
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            GateAsync(t, new User { Username = "adm", RealName = "관리", IsAdmin = true }, "/quotation", ext));
        Assert.True(await GateAsync(t, new User { Username = "kim", RealName = "김" }, "/inventory", ext));
        // 사내에서는 그대로
        Assert.True(await GateAsync(t, new User { Username = "adm2", RealName = "관리2", IsAdmin = true }, "/quotation", null));
    }

    private static async Task<bool> AdminAreaAsync(TestDb t, bool external)
    {
        var admin = new User { Username = "a" + Guid.NewGuid().ToString("N")[..6], RealName = "관리", IsAdmin = true, PasswordHash = "x" };
        t.Db.Users.Add(admin);
        await t.Db.SaveChangesAsync();
        var http = new DefaultHttpContext();
        if (external) http.Items[ExternalAccessPolicy.ItemKey] = new HashSet<string>();
        var handler = new DbPermissionHandler(t.Db, new HttpContextAccessor { HttpContext = http });
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", admin.Id.ToString()) }, "test"));
        var context = new AuthorizationHandlerContext(new[] { new DbPermissionRequirement("admin", 1) }, principal, null);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task 관리자_영역은_사외에서_막힌다()
    {
        using var t = new TestDb();
        Assert.True(await AdminAreaAsync(t, external: false));
        Assert.False(await AdminAreaAsync(t, external: true));
    }
}

/// <summary>실제 요청으로 — 로그인 기록과 강제 로그아웃. (테스트 호스트는 접속 IP 가 없어 사외로 잡힌다. 제한은 꺼진 기본값.)</summary>
[Collection(PortalAppCollection.Name)]
public class ExternalAccessEndpointTests
{
    private readonly PortalAppFixture _app;
    public ExternalAccessEndpointTests(PortalAppFixture app) => _app = app;

    private async Task<(int id, string name)> NewUserAsync()
    {
        var name = $"sec-{Guid.NewGuid():N}"[..16];
        using var scope = _app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CleanPotalDbContext>();
        var u = new User { Username = name, RealName = "보안시험", PasswordHash = PasswordHasher.Hash(PortalAppFixture.Password), EmployeeNumber = name };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return (u.Id, name);
    }

    private async Task<HttpClient> LoginAsync(string username)
    {
        var client = _app.Factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { username, password = PortalAppFixture.Password });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            doc.RootElement.GetProperty("data").GetProperty("token").GetString());
        return client;
    }

    [Fact]
    public async Task 강제_로그아웃하면_옛_토큰은_401_다시_로그인하면_된다()
    {
        var (id, name) = await NewUserAsync();
        var user = await LoginAsync(name);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/auth/me")).StatusCode);

        var admin = await _app.SignInAsync("admin");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/security/users/{id}/revoke", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/auth/me")).StatusCode);
        var again = await LoginAsync(name);
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task 로그인_성공과_실패가_기록된다()
    {
        var (_, name) = await NewUserAsync();
        var anon = _app.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PostAsJsonAsync("/api/auth/login", new { username = name, password = "wrong" })).StatusCode);
        await LoginAsync(name);

        using var scope = _app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CleanPotalDbContext>();
        var rows = await db.AccessLogs.Where(l => l.Username == name).OrderBy(l => l.Id).ToListAsync();
        Assert.Equal(new[] { "fail", "ok" }, rows.Select(r => r.Result));
        Assert.NotNull(rows[1].UserId);
    }

    [Fact]
    public async Task 보안_API는_관리자만()
    {
        var (_, name) = await NewUserAsync();
        var user = await LoginAsync(name);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/security/config")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/security/users/allow", new { ids = new[] { 1 }, allow = true })).StatusCode);
    }

    [Fact]
    public async Task 사내로_잡히지_않는_곳에서는_제한을_켤_수_없다()
    {
        var admin = await _app.SignInAsync("admin");
        var res = await admin.PutAsJsonAsync("/api/security/config",
            new { enforce = true, internalRanges = new[] { "10.0.0.0/8" }, externalHidden = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var bad = await admin.PutAsJsonAsync("/api/security/config",
            new { enforce = false, internalRanges = new[] { "10.0.0/8" }, externalHidden = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }
}
