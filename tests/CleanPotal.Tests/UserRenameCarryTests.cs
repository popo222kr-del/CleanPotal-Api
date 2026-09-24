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
        // 이름이 겹치는 계정은 이제 만들 수 없지만, 예전에 이미 겹쳐 있던 계정은 남아 있을 수 있다.
        t.Db.Users.Add(new User { Username = "200", RealName = "김같" });
        t.Db.ShiftSchedules.Add(new ShiftSchedule { MemberName = "김같", TargetDate = D, ShiftType = "연차" });
        await t.Db.SaveChangesAsync();

        await svc.UpdateAsync(u.Id, Req("100", "김다름"), "관리자");

        Assert.Equal("김같", (await t.Db.ShiftSchedules.AsNoTracking().SingleAsync()).MemberName);
    }

    [Fact]
    public async Task 이름이_겹치는_계정은_만들거나_바꿀_수_없다()
    {
        using var t = new TestDb();
        var svc = new UserService(t.Db);
        await svc.CreateAsync(Req("100", "김철수"), "관리자");
        var other = await svc.CreateAsync(Req("200", "이영희"), "관리자");

        var ex = await Assert.ThrowsAsync<CleanPotal.Core.BusinessRuleException>(() => svc.CreateAsync(Req("300", " 김철수 "), "관리자"));
        Assert.Contains("김철수(B)", ex.Message);
        await Assert.ThrowsAsync<CleanPotal.Core.BusinessRuleException>(() => svc.UpdateAsync(other.Id, Req("200", "김철수"), "관리자"));

        // 구분자를 붙이면 된다. 이름을 바꾸지 않는 수정은 막지 않는다.
        await svc.CreateAsync(Req("300", "김철수(B)"), "관리자");
        await svc.UpdateAsync(other.Id, Req("200", "이영희"), "관리자");
    }

    [Fact]
    public async Task 퇴사자_이름이면_복직을_안내한다()
    {
        using var t = new TestDb();
        t.Db.Users.Add(new User { Username = "old", RealName = "박퇴사", IsResigned = true });
        await t.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<CleanPotal.Core.BusinessRuleException>(
            () => new UserService(t.Db).CreateAsync(Req("new", "박퇴사"), "관리자"));
        Assert.Contains("복직", ex.Message);
    }
}
