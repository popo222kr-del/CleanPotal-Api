using System.Reflection;
using System.Security.Claims;
using CleanPotal.Api.Controllers;
using CleanPotal.Api.Infrastructure;
using CleanPotal.Core;
using CleanPotal.Core.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>관리자가 숨긴 메뉴 전용 API 는 서버도 막는다(예전에는 사이드바에서만 가렸다).</summary>
public class MenuGateTests
{
    private static async Task<bool> RunAsync(TestDb t, User user, string route)
    {
        t.Db.Users.Add(user);
        await t.Db.SaveChangesAsync();

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", user.Id.ToString()) }, "test")),
        };
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
    public async Task 숨긴_메뉴의_API는_403()
    {
        using var t = new TestDb();
        var u = new User { Username = "kim", RealName = "김", HiddenMenus = "[\"/inventory\"]" };
        await Assert.ThrowsAsync<ForbiddenException>(() => RunAsync(t, u, "/inventory"));
    }

    [Fact]
    public async Task 숨기지_않은_메뉴와_관리자는_통과()
    {
        using var t = new TestDb();
        Assert.True(await RunAsync(t, new User { Username = "lee", RealName = "이", HiddenMenus = "[\"/icpms\"]" }, "/inventory"));
        Assert.True(await RunAsync(t, new User { Username = "adm", RealName = "관리", IsAdmin = true, HiddenMenus = "[\"/inventory\"]" }, "/inventory"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("not json", false)]
    [InlineData("[\"/prodreq\"]", true)]
    [InlineData("[\"/prodreq/options\"]", false)]
    public void 숨김_목록_해석(string? json, bool expected)
        => Assert.Equal(expected, MenuGateFilter.IsHidden(json, "/prodreq"));

    [Fact]
    public void 메뉴_전용_컨트롤러에는_표시가_붙어_있다()
    {
        string? Gate(Type c) => c.GetCustomAttribute<MenuGateAttribute>()?.Route;
        Assert.Equal("/prodreq", Gate(typeof(ProdReqController)));
        Assert.Equal("/inventory", Gate(typeof(InventoryController)));
        Assert.Equal("/temp-humidity", Gate(typeof(IotController)));
        // 여러 화면이 같이 쓰는 API 에는 붙이지 않는다.
        Assert.Null(Gate(typeof(VendorController)));
        Assert.Null(Gate(typeof(ReportsController)));
        Assert.Null(Gate(typeof(HandoverController)));
    }

    // ── 메뉴 '조회만' — 영역 등급이 편집이어도 그 메뉴의 편집 동작은 막는다 ──

    private static async Task<bool> RunMetaAsync(TestDb t, User user, params object[] meta)
    {
        if (user.Id == 0) { t.Db.Users.Add(user); await t.Db.SaveChangesAsync(); }
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", user.Id.ToString()) }, "test")),
        };
        var descriptor = new ActionDescriptor { EndpointMetadata = meta.ToList() };
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

    private static Microsoft.AspNetCore.Authorization.AuthorizeAttribute Policy(string p) => new() { Policy = p };

    [Fact]
    public async Task 조회만_메뉴는_편집_동작만_막고_조회와_조회등급_쓰기는_통과()
    {
        using var t = new TestDb();
        var u = new User { Username = "ro", RealName = "조회", ReadOnlyMenus = "[\"/icpms\",\"/notice\",\"/roster\"]" };

        Assert.True(await RunMetaAsync(t, u, new MenuGateAttribute("/icpms"), Policy("ViewField")));   // 조회
        await Assert.ThrowsAsync<ForbiddenException>(() => RunMetaAsync(t, u, new MenuGateAttribute("/icpms"), Policy("EditField")));
        Assert.True(await RunMetaAsync(t, u, new MenuGateAttribute("/checklist"), Policy("EditField")));   // 다른 메뉴는 등급대로
        // 숨김은 걸지 않고 편집만 막는 표시(공지·배차·업체 등)
        await Assert.ThrowsAsync<ForbiddenException>(() => RunMetaAsync(t, u, new EditGateAttribute("/notice"), Policy("EditHandover")));
        Assert.True(await RunMetaAsync(t, u, new EditGateAttribute("/notice"), Policy("ViewHandover")));
        // 표시가 없는 근무표 도장은 정책으로 메뉴를 짐작한다
        await Assert.ThrowsAsync<ForbiddenException>(() => RunMetaAsync(t, u, Policy("EditRoster")));
        Assert.True(await RunMetaAsync(t, u, Policy("EditSchedule")));
    }

    [Fact]
    public async Task 관리자는_조회만이_적혀_있어도_편집한다()
    {
        using var t = new TestDb();
        var adm = new User { Username = "adm2", RealName = "관리", IsAdmin = true, ReadOnlyMenus = "[\"/icpms\"]" };
        Assert.True(await RunMetaAsync(t, adm, new MenuGateAttribute("/icpms"), Policy("EditField")));
    }
}
