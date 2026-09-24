using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using CleanPotal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>관리자가 화면에서 고친 공휴일이 기본 목록 위에 덮어써진다.</summary>
public class HolidayOverrideTests
{
    private static HolidayService Svc(TestDb t)
    {
        var services = new ServiceCollection();
        services.AddScoped<CleanPotalDbContext>(_ => t.NewContext());
        return new HolidayService(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
    }

    private static void Add(TestDb t, int y, int m, int d, string name, bool isOff)
    {
        t.Db.HolidayOverrides.Add(new HolidayOverride { Date = new DateOnly(y, m, d), Name = name, IsOff = isOff, UpdatedBy = "관리자" });
        t.Db.SaveChanges();
    }

    [Fact]
    public void 추가_이름변경_평일로_되돌림이_반영된다()
    {
        using var t = new TestDb();
        Add(t, 2026, 10, 2, "임시공휴일", isOff: true);
        Add(t, 2026, 12, 25, "성탄절", isOff: true);
        Add(t, 2026, 6, 3, "지방선거", isOff: false);
        var svc = Svc(t);

        var map = svc.GetMap(2026);
        Assert.Equal("임시공휴일", map[new DateOnly(2026, 10, 2)]);
        Assert.Equal("성탄절", map[new DateOnly(2026, 12, 25)]);
        Assert.False(svc.IsHoliday(new DateOnly(2026, 6, 3)));
        Assert.True(svc.IsHoliday(new DateOnly(2026, 10, 2)));
        // 기본 목록 자체는 그대로다.
        Assert.True(svc.GetBuiltInMap(2026).ContainsKey(new DateOnly(2026, 6, 3)));
    }

    [Fact]
    public void 기본_목록이_없는_해도_추가한_날은_공휴일이다()
    {
        using var t = new TestDb();
        Add(t, 2028, 1, 1, "신정", isOff: true);
        Assert.True(Svc(t).IsHoliday(new DateOnly(2028, 1, 1)));
    }

    [Fact]
    public void 고친_뒤_InvalidateOverrides_하면_바로_읽는다()
    {
        using var t = new TestDb();
        var svc = Svc(t);
        Assert.False(svc.IsHoliday(new DateOnly(2026, 10, 2)));

        Add(t, 2026, 10, 2, "임시공휴일", isOff: true);
        Assert.False(svc.IsHoliday(new DateOnly(2026, 10, 2)));   // 아직 기억해 둔 값
        svc.InvalidateOverrides();
        Assert.True(svc.IsHoliday(new DateOnly(2026, 10, 2)));
    }

    [Fact]
    public void 표가_없어도_기본_목록으로_돌아간다()
    {
        using var t = new TestDb();
        t.Db.Database.ExecuteSqlRaw(@"DROP TABLE ""HolidayOverrides""");
        var svc = Svc(t);
        Assert.True(svc.IsHoliday(new DateOnly(2026, 9, 25)));
    }
}

/// <summary>공휴일 관리 API — 기본과 같은 값은 수정분을 남기지 않고, 기본 목록에 없는 날은 평일로 되돌릴 수 없다.</summary>
public class HolidayManageControllerTests
{
    private static (CleanPotal.Api.Controllers.HolidaysController Ctl, HolidayService Svc) Make(TestDb t)
    {
        var services = new ServiceCollection();
        services.AddScoped<CleanPotalDbContext>(_ => t.NewContext());
        var svc = new HolidayService(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
        var ctl = new CleanPotal.Api.Controllers.HolidaysController(svc, t.Db)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                        new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "관리자") }, "test")),
                },
            },
        };
        return (ctl, svc);
    }

    private static CleanPotal.Core.DTOs.HolidayManagePageDto Page(Microsoft.AspNetCore.Mvc.ActionResult<CleanPotal.Core.DTOs.HolidayManagePageDto> r)
        => (CleanPotal.Core.DTOs.HolidayManagePageDto)((Microsoft.AspNetCore.Mvc.OkObjectResult)r.Result!).Value!;

    [Fact]
    public async Task 추가_평일로_기본으로_되돌리기()
    {
        using var t = new TestDb();
        var (ctl, svc) = Make(t);

        var p = Page(await ctl.Save(new(new DateOnly(2026, 10, 2), "임시공휴일", true), default));
        Assert.Contains(p.Rows, r => r.Date == new DateOnly(2026, 10, 2) && r.Source == "added");
        Assert.True(svc.IsHoliday(new DateOnly(2026, 10, 2)));

        p = Page(await ctl.Save(new(new DateOnly(2026, 6, 3), null, false), default));
        Assert.Contains(p.Rows, r => r.Date == new DateOnly(2026, 6, 3) && r.Source == "removed");
        Assert.False(svc.IsHoliday(new DateOnly(2026, 6, 3)));

        p = Page(await ctl.Reset(new DateOnly(2026, 6, 3), default));
        Assert.Contains(p.Rows, r => r.Date == new DateOnly(2026, 6, 3) && r.Source == "builtin");
        Assert.True(svc.IsHoliday(new DateOnly(2026, 6, 3)));
    }

    [Fact]
    public async Task 기본과_같은_이름이면_수정분을_남기지_않는다()
    {
        using var t = new TestDb();
        var (ctl, _) = Make(t);
        await ctl.Save(new(new DateOnly(2026, 12, 25), "성탄절", true), default);
        await ctl.Save(new(new DateOnly(2026, 12, 25), "크리스마스", true), default);
        Assert.Equal(0, t.NewContext().HolidayOverrides.Count());
    }

    [Fact]
    public async Task 잘못된_요청은_거절한다()
    {
        using var t = new TestDb();
        var (ctl, _) = Make(t);
        await Assert.ThrowsAsync<CleanPotal.Core.BusinessRuleException>(() => ctl.Save(new(new DateOnly(2026, 10, 2), " ", true), default));
        await Assert.ThrowsAsync<CleanPotal.Core.BusinessRuleException>(() => ctl.Save(new(new DateOnly(2026, 10, 2), null, false), default));
    }
}
