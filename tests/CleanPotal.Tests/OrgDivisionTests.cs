using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 본부(사업본부) 계층과, 같은 이름 팀이 여러 부서에 있을 때의 안전장치.
/// Office 처럼 본부마다 있는 팀 이름이 겹쳐도 서로를 건드리면 안 된다.
/// </summary>
public class OrgDivisionTests
{
    private static readonly DateOnly 어떤날 = new(2026, 6, 10);

    private static User Person(string name, string dept, string team) =>
        new() { Username = name, RealName = name, Department = dept, TeamName = team, PasswordHash = "x" };

    // ── 본부 계층 ──

    [Fact]
    public async Task 본부를_만들고_부서를_붙이면_트리에_반영된다()
    {
        using var t = new TestDb();
        t.Db.Users.Add(Person("박주언", "나노세정", "Office"));
        await t.Db.SaveChangesAsync();
        var svc = new UserService(t.Db);

        Assert.Null(await svc.AddOrgAsync("division", "반도체 사업본부", null, "tester"));
        Assert.Null(await svc.SetDeptDivisionAsync("나노세정", "반도체 사업본부", "tester"));

        var tree = await svc.GetOrgAsync();
        Assert.Equal(new[] { "반도체 사업본부" }, tree.Divisions);
        Assert.Equal("반도체 사업본부", tree.Depts.Single(d => d.Name == "나노세정").Division);
    }

    [Fact]
    public async Task 등록되지_않은_본부로는_부서를_옮길_수_없다()
    {
        using var t = new TestDb();
        t.Db.Users.Add(Person("박주언", "나노세정", "Office"));
        await t.Db.SaveChangesAsync();

        Assert.NotNull(await new UserService(t.Db).SetDeptDivisionAsync("나노세정", "없는본부", "tester"));
    }

    [Fact]
    public async Task 본부명을_바꾸면_소속_부서도_따라간다()
    {
        using var t = new TestDb();
        t.Db.Users.Add(Person("박주언", "나노세정", "Office"));
        await t.Db.SaveChangesAsync();
        var svc = new UserService(t.Db);
        await svc.AddOrgAsync("division", "반도체본부", null, "tester");
        await svc.SetDeptDivisionAsync("나노세정", "반도체본부", "tester");

        Assert.Null(await svc.RenameDivisionAsync("반도체본부", "반도체 사업본부", "tester"));

        var tree = await svc.GetOrgAsync();
        Assert.Equal(new[] { "반도체 사업본부" }, tree.Divisions);
        Assert.Equal("반도체 사업본부", tree.Depts.Single(d => d.Name == "나노세정").Division);
    }

    [Fact]
    public async Task 소속_부서가_있는_본부는_삭제되지_않는다()
    {
        using var t = new TestDb();
        t.Db.Users.Add(Person("박주언", "나노세정", "Office"));
        await t.Db.SaveChangesAsync();
        var svc = new UserService(t.Db);
        await svc.AddOrgAsync("division", "반도체 사업본부", null, "tester");
        await svc.SetDeptDivisionAsync("나노세정", "반도체 사업본부", "tester");

        Assert.NotNull(await svc.DeleteOrgAsync("division", "반도체 사업본부", null, "tester"));

        // 부서를 빼면 삭제된다
        Assert.Null(await svc.SetDeptDivisionAsync("나노세정", "", "tester"));
        Assert.Null(await svc.DeleteOrgAsync("division", "반도체 사업본부", null, "tester"));
    }

    // ── 같은 이름 팀이 여러 부서에 있을 때 ──

    private static TestDb SeedTwoOffices()
    {
        var t = new TestDb();
        t.Db.Users.Add(Person("박주언", "나노세정", "Office"));
        t.Db.Users.Add(Person("설병석", "Wafer 제조팀", "Office"));
        t.Db.SaveChanges();
        return t;
    }

    [Fact]
    public async Task 팀명을_바꿔도_다른_부서의_같은_이름_팀은_그대로다()
    {
        // 예전에는 팀 이름만 보고 바꿔서, 나노세정 Office 를 고치면 wafer Office 까지 바뀌었다.
        using var t = SeedTwoOffices();

        await new UserService(t.Db).TeamBulkAsync(
            new TeamBulkRequest("Office", "세정Office", null, Department: "나노세정"), "tester");

        using var fresh = t.NewContext();
        Assert.Equal("세정Office", fresh.Users.Single(u => u.RealName == "박주언").TeamName);
        Assert.Equal("Office", fresh.Users.Single(u => u.RealName == "설병석").TeamName);
    }

    [Fact]
    public async Task 부서를_주지_않으면_예전처럼_같은_이름_팀_전체가_바뀐다()
    {
        // 기존 호출부(부서를 모르는 경로)의 동작을 보존한다.
        using var t = SeedTwoOffices();

        await new UserService(t.Db).TeamBulkAsync(new TeamBulkRequest("Office", "사무", null), "tester");

        using var fresh = t.NewContext();
        Assert.All(fresh.Users.ToList(), u => Assert.Equal("사무", u.TeamName));
    }

    [Fact]
    public async Task 팀명_변경이_다른_부서의_근무표_도장을_건드리지_않는다()
    {
        using var t = SeedTwoOffices();
        t.Db.ShiftSchedules.Add(new ShiftSchedule { MemberName = "박주언", TargetDate = 어떤날, ShiftType = "연차", TeamGroup = "Office" });
        t.Db.ShiftSchedules.Add(new ShiftSchedule { MemberName = "설병석", TargetDate = 어떤날, ShiftType = "연차", TeamGroup = "Office" });
        await t.Db.SaveChangesAsync();

        await new UserService(t.Db).TeamBulkAsync(
            new TeamBulkRequest("Office", "세정Office", null, Department: "나노세정"), "tester");

        using var fresh = t.NewContext();
        Assert.Equal("세정Office", fresh.ShiftSchedules.Single(s => s.MemberName == "박주언").TeamGroup);
        Assert.Equal("Office", fresh.ShiftSchedules.Single(s => s.MemberName == "설병석").TeamGroup);
    }

    [Fact]
    public async Task 교대조_설정도_그_부서의_팀에만_적용된다()
    {
        using var t = SeedTwoOffices();
        var svc = new UserService(t.Db);

        Assert.Null(await svc.SetOrgShiftGroupAsync("Office", 1, "tester", "나노세정"));

        using var fresh = t.NewContext();
        var units = fresh.OrgUnits.Where(o => o.Kind == "team" && o.Name == "Office").ToList();
        Assert.Equal(1, units.Single(o => o.Parent == "나노세정").ShiftGroup);
        Assert.DoesNotContain(units, o => o.Parent == "Wafer 제조팀");   // 건드리지 않았다
    }

    // ── 교대 조 번호는 본부 안에서만 유일 ──

    [Fact]
    public async Task 본부가_다르면_같은_조_번호를_쓸_수_있다()
    {
        // wafer 는 교대 주기가 달라 자체 1조가 필요하다. 예전에는 '1조는 이미 1팀에 있습니다' 로 막혔다.
        using var t = new TestDb();
        t.Db.Users.Add(Person("박주언", "나노세정", "1팀"));
        t.Db.Users.Add(Person("설병석", "Wafer 제조팀", "W1조"));
        await t.Db.SaveChangesAsync();
        var svc = new UserService(t.Db);
        await svc.AddOrgAsync("division", "반도체 사업본부", null, "tester");
        await svc.AddOrgAsync("division", "Wafer 사업본부", null, "tester");
        await svc.SetDeptDivisionAsync("나노세정", "반도체 사업본부", "tester");
        await svc.SetDeptDivisionAsync("Wafer 제조팀", "Wafer 사업본부", "tester");

        Assert.Null(await svc.SetOrgShiftGroupAsync("1팀", 1, "tester", "나노세정"));
        Assert.Null(await svc.SetOrgShiftGroupAsync("W1조", 1, "tester", "Wafer 제조팀"));
    }

    [Fact]
    public async Task 같은_본부_안에서는_여전히_조_번호가_유일해야_한다()
    {
        using var t = new TestDb();
        t.Db.Users.Add(Person("박주언", "나노세정", "1팀"));
        t.Db.Users.Add(Person("홍길동", "나노세정", "2팀"));
        await t.Db.SaveChangesAsync();
        var svc = new UserService(t.Db);
        await svc.AddOrgAsync("division", "반도체 사업본부", null, "tester");
        await svc.SetDeptDivisionAsync("나노세정", "반도체 사업본부", "tester");

        Assert.Null(await svc.SetOrgShiftGroupAsync("1팀", 1, "tester", "나노세정"));
        Assert.NotNull(await svc.SetOrgShiftGroupAsync("2팀", 1, "tester", "나노세정"));
    }

    // ── 대시보드 본부별 묶음 ──

    [Fact]
    public async Task 오늘_현황은_본부별로_묶이고_본부_안에서는_생산팀이_먼저다()
    {
        using var t = new TestDb();
        t.Db.Users.Add(Person("박주언", "나노세정", "1팀"));
        t.Db.Users.Add(Person("김단비", "나노세정", "주간팀"));
        t.Db.Users.Add(Person("이연구", "연구소", ""));
        await t.Db.SaveChangesAsync();

        var svc = new UserService(t.Db);
        await svc.AddOrgAsync("division", "반도체 사업본부", null, "tester");
        await svc.SetDeptDivisionAsync("나노세정", "반도체 사업본부", "tester");
        await svc.SetDeptDivisionAsync("연구소", "", "tester");
        await svc.SetOrgShiftGroupAsync("1팀", 1, "tester", "나노세정");

        var status = await new ScheduleService(t.Db, new HolidayService(), FakeCurrentUser.Admin()).GetTodayStatusAsync();

        // 본부가 붙은 줄이 먼저, 본부 안에서는 교대 생산팀(1팀)이 부서 줄(나노세정)보다 앞
        Assert.Equal(new[] { "1팀", "나노세정", "연구소" }, status.Teams.Select(x => x.Team));
        Assert.Equal("반도체 사업본부", status.Teams[0].Division);
        Assert.True(status.Teams[0].Production);
        Assert.Equal("반도체 사업본부", status.Teams[1].Division);
        Assert.False(status.Teams[1].Production);
        Assert.Equal("", status.Teams[2].Division);   // 본부 미지정은 맨 뒤
    }
}
