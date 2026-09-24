using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 근무표·교육 일정은 사람을 이름으로 가리킨다. 이름을 바꾸면 예전에는 과거 줄이 옛 이름에 남아 아무도 보지 못했다.
/// </summary>
public class UserRenameCarryTests
{
    private static UserUpsertRequest Req(string username, string realName) =>
        new(username, "pw1234", RealName: realName, Department: "나노세정", TeamName: "1팀",
            Rank: "", JobTitle: "", Email: "", PhoneNumber: "", EmployeeNumber: username,
            HireDate: "", IsResigned: false, ResignDate: "", IsAdmin: false,
            AccessSchedule: 1, AccessRoster: 1, AccessHandover: 1, AccessField: 1, AccessOffice: 0,
            AccessMes: 1, MesPermissions: null, HiddenMenus: null);

    private static readonly DateOnly D = new(2026, 9, 1);

    [Fact]
    public async Task 이름을_바꾸면_근무표와_교육이_따라온다()
    {
        using var t = new TestDb();
        var svc = new UserService(t.Db);
        var u = await svc.CreateAsync(Req("100", "김옛"), "관리자");
        t.Db.ShiftSchedules.Add(new ShiftSchedule { MemberName = "김옛", TargetDate = D, ShiftType = "연차" });
        t.Db.EducationPlans.Add(new EducationPlan { MemberName = "김옛", CourseName = "안전" });
        await t.Db.SaveChangesAsync();

        await svc.UpdateAsync(u.Id, Req("100", "김새"), "관리자");

        Assert.Equal("김새", (await t.Db.ShiftSchedules.AsNoTracking().SingleAsync()).MemberName);
        Assert.Equal("김새", (await t.Db.EducationPlans.AsNoTracking().SingleAsync()).MemberName);
        Assert.Contains(await svc.GetAuditAsync(), a => a.Action == "이름 변경");
    }

    [Fact]
    public async Task 동명이인이_있으면_누구_것인지_몰라_건드리지_않는다()
    {
        using var t = new TestDb();
        var svc = new UserService(t.Db);
        var u = await svc.CreateAsync(Req("100", "김같"), "관리자");
        await svc.CreateAsync(Req("200", "김같"), "관리자");
        t.Db.ShiftSchedules.Add(new ShiftSchedule { MemberName = "김같", TargetDate = D, ShiftType = "연차" });
        await t.Db.SaveChangesAsync();

        await svc.UpdateAsync(u.Id, Req("100", "김다름"), "관리자");

        Assert.Equal("김같", (await t.Db.ShiftSchedules.AsNoTracking().SingleAsync()).MemberName);
    }
}
