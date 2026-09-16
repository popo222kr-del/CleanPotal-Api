using System.Security.Claims;
using CleanPotal.Api.Infrastructure;
using CleanPotal.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 영역×등급 권한 판정. 모든 API 가 이 한 곳을 지난다 — 여기가 어긋나면 전 영역이 함께 열리거나 닫힌다.
///
/// 판정은 매 요청 DB 를 보므로 등급을 바꾸면 재로그인 없이 반영된다. 그 성질까지 같이 고정한다.
/// </summary>
public class DbPermissionHandlerTests
{
    private static ClaimsPrincipal Principal(int? uid) =>
        new(new ClaimsIdentity(
            uid is null ? Array.Empty<Claim>() : new[] { new Claim("uid", uid.Value.ToString()) }, "test"));

    private static User Member(int id = 1) => new()
    {
        Id = id, Username = $"u{id}", RealName = "직원", PasswordHash = "x",
        AccessSchedule = 0, AccessRoster = 0, AccessHandover = 0, AccessField = 0, AccessOffice = 0, AccessMes = 0,
    };

    /// <summary>한 번의 요청을 흉내 낸다 — 통과했으면 true.</summary>
    private static async Task<bool> AllowsAsync(TestDb t, ClaimsPrincipal user, string area, int minLevel)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var handler = new DbPermissionHandler(t.Db, accessor);
        var requirement = new DbPermissionRequirement(area, minLevel);
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task 토큰에_계정_번호가_없으면_막는다()
    {
        using var t = new TestDb();
        Assert.False(await AllowsAsync(t, Principal(null), "mes", 1));
    }

    [Fact]
    public async Task 없는_계정이면_막는다()
    {
        using var t = new TestDb();
        Assert.False(await AllowsAsync(t, Principal(999), "mes", 1));
    }

    [Fact]
    public async Task 퇴사자는_관리자여도_막는다()
    {
        using var t = new TestDb();
        var u = Member();
        u.IsAdmin = true;
        u.IsResigned = true;
        t.Db.Users.Add(u);
        await t.Db.SaveChangesAsync();

        Assert.False(await AllowsAsync(t, Principal(u.Id), "mes", 1));
        Assert.False(await AllowsAsync(t, Principal(u.Id), "admin", 1));
    }

    [Theory]
    [InlineData("schedule")]
    [InlineData("roster")]
    [InlineData("handover")]
    [InlineData("field")]
    [InlineData("office")]
    [InlineData("mes")]
    [InlineData("reports")]
    [InlineData("admin")]
    public async Task 관리자는_모든_영역을_통과한다(string area)
    {
        using var t = new TestDb();
        var u = Member();
        u.IsAdmin = true;
        t.Db.Users.Add(u);
        await t.Db.SaveChangesAsync();

        Assert.True(await AllowsAsync(t, Principal(u.Id), area, 2));
    }

    [Theory]
    [InlineData(0, false, false)]   // 없음 — 조회도 안 된다
    [InlineData(1, true, false)]    // 조회 — 편집은 안 된다
    [InlineData(2, true, true)]     // 편집
    public async Task MES_등급대로_조회와_편집이_갈린다(int level, bool canView, bool canEdit)
    {
        using var t = new TestDb();
        var u = Member();
        u.AccessMes = level;
        t.Db.Users.Add(u);
        await t.Db.SaveChangesAsync();

        Assert.Equal(canView, await AllowsAsync(t, Principal(u.Id), "mes", 1));
        Assert.Equal(canEdit, await AllowsAsync(t, Principal(u.Id), "mes", 2));
    }

    [Fact]
    public async Task 한_영역을_줘도_다른_영역은_열리지_않는다()
    {
        using var t = new TestDb();
        var u = Member();
        u.AccessMes = 2;
        t.Db.Users.Add(u);
        await t.Db.SaveChangesAsync();

        Assert.True(await AllowsAsync(t, Principal(u.Id), "mes", 2));
        foreach (var other in new[] { "schedule", "roster", "handover", "field", "office", "admin" })
            Assert.False(await AllowsAsync(t, Principal(u.Id), other, 1));
    }

    [Fact]
    public async Task 관리자_전용은_등급으로_열_수_없다()
    {
        using var t = new TestDb();
        var u = Member();
        // 모든 영역을 편집으로 올려도 admin 은 IsAdmin 으로만 통과한다.
        u.AccessSchedule = u.AccessRoster = u.AccessHandover = u.AccessField = u.AccessOffice = u.AccessMes = 2;
        t.Db.Users.Add(u);
        await t.Db.SaveChangesAsync();

        Assert.False(await AllowsAsync(t, Principal(u.Id), "admin", 1));
    }

    [Theory]
    [InlineData(1, 0, true)]    // 인수인계만 있어도
    [InlineData(0, 1, true)]    // OFFICE 만 있어도
    [InlineData(0, 0, false)]
    public async Task 보고서는_인수인계와_OFFICE_중_하나면_된다(int handover, int office, bool allowed)
    {
        using var t = new TestDb();
        var u = Member();
        u.AccessHandover = handover;
        u.AccessOffice = office;
        t.Db.Users.Add(u);
        await t.Db.SaveChangesAsync();

        Assert.Equal(allowed, await AllowsAsync(t, Principal(u.Id), "reports", 1));
    }

    [Fact]
    public async Task 모르는_영역_이름은_막는다()
    {
        // 정책을 새로 만들면서 영역 이름을 잘못 적으면 조용히 열리는 것이 아니라 막혀야 한다.
        using var t = new TestDb();
        var u = Member();
        u.IsAdmin = false;
        u.AccessMes = 2;
        t.Db.Users.Add(u);
        await t.Db.SaveChangesAsync();

        Assert.False(await AllowsAsync(t, Principal(u.Id), "mes-setup", 1));
        Assert.False(await AllowsAsync(t, Principal(u.Id), "", 1));
    }

    [Fact]
    public async Task 등급을_바꾸면_재로그인_없이_반영된다()
    {
        using var t = new TestDb();
        var u = Member();
        t.Db.Users.Add(u);
        await t.Db.SaveChangesAsync();

        Assert.False(await AllowsAsync(t, Principal(u.Id), "mes", 1));

        u.AccessMes = 1;
        await t.Db.SaveChangesAsync();

        // 같은 토큰(같은 Principal)으로 바로 통과한다 — 판정이 매 요청 DB 를 보기 때문이다.
        Assert.True(await AllowsAsync(t, Principal(u.Id), "mes", 1));
    }
}
