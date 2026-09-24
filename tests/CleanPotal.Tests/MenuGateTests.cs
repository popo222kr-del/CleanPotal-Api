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
}
