using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 팀 이름을 바꿔도 근무표·달력·교대 예측이 따라오는지.
/// 예전에는 코드에 "김팀"/"장팀" 이 박혀 있어, 이름을 바꾸면 근무표가 빈 화면이 되고
/// 두 팀이 같은 조로 예측됐다.
/// </summary>
public class ProductionTeamsTests
{
    private static readonly DateOnly 어떤날 = new(2026, 6, 10);

    private static OrgUnit Team(string name, int shiftGroup) =>
        new() { Kind = "team", Name = name, Parent = "세정", ShiftGroup = shiftGroup };

    private static User Member(string name, string team) =>
        new() { Username = name, RealName = name, TeamName = team, PasswordHash = "x" };

    [Fact]
    public void 조가_다르면_항상_반대_근무다()
    {
        var a = ShiftPredictor.Predict(1, 어떤날);
        var b = ShiftPredictor.Predict(2, 어떤날);
        Assert.NotEqual(a, b);
        Assert.Contains(a, new[] { "주간", "야간" });
    }

    [Fact]
    public async Task 교대조를_지정하지_않은_DB_는_예전_동작을_유지한다()
    {
        // 이 기능을 올렸다는 이유만으로 멀쩡하던 화면이 비면 안 된다.
        using var t = new TestDb();
        var pt = await ProductionTeams.LoadAsync(t.Db);
        Assert.False(pt.IsConfigured);
        Assert.Equal(new[] { "김팀", "장팀" }, pt.Names);
        Assert.NotEqual(pt.PredictShift("김팀", 어떤날), pt.PredictShift("장팀", 어떤날));
    }

    [Fact]
    public async Task 팀_이름을_바꿔도_조직도에_지정하면_그대로_동작한다()
    {
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Team("1팀", 1));
        t.Db.OrgUnits.Add(Team("2팀", 2));
        await t.Db.SaveChangesAsync();

        var pt = await ProductionTeams.LoadAsync(t.Db);
        Assert.True(pt.IsConfigured);
        Assert.Equal(new[] { "1팀", "2팀" }, pt.Names);          // 1조 → 2조 순
        Assert.True(pt.IsProduction("1팀"));
        Assert.False(pt.IsProduction("주간팀"));
        Assert.NotEqual(pt.PredictShift("1팀", 어떤날), pt.PredictShift("2팀", 어떤날));
        Assert.Equal("", pt.PredictShift("Office", 어떤날));      // 교대 근무가 아니면 예측하지 않는다
    }

    [Fact]
    public async Task 이름을_바꾼_뒤에도_근무표에_팀원이_나온다()
    {
        // 실제로 났던 증상: 팀 이름을 바꾸니 근무표 '전체' 가 빈 화면이 됐다.
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Team("1팀", 1));
        t.Db.OrgUnits.Add(Team("2팀", 2));
        t.Db.Users.Add(Member("박주언", "1팀"));
        t.Db.Users.Add(Member("홍길동", "2팀"));
        await t.Db.SaveChangesAsync();

        var roster = await new ScheduleService(t.Db, new HolidayService())
            .GetRosterAsync(2026, 6, "전체", predict: false);

        Assert.Equal(new[] { "1팀", "2팀" }, roster.Teams.Select(x => x.Team));
        Assert.Equal("박주언", roster.Teams[0].Members.Single().Name);
    }

    [Fact]
    public async Task 근무표_필터_목록도_조직도를_따른다()
    {
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Team("2팀", 2));
        t.Db.OrgUnits.Add(Team("1팀", 1));
        t.Db.OrgUnits.Add(Team("주간팀", 0));   // 교대 근무 아님 → 목록에서 빠진다
        await t.Db.SaveChangesAsync();

        var names = await new ScheduleService(t.Db, new HolidayService()).GetProductionTeamsAsync();
        Assert.Equal(new[] { "1팀", "2팀" }, names);
    }

    [Fact]
    public async Task 팀명을_바꾸면_이미_찍은_근무표도_새_이름을_따라간다()
    {
        // 근무표는 팀 이름을 문자열로 들고 있어, 이름만 바꾸면 과거 근무가 옛 이름에 묶인다.
        using var t = new TestDb();
        t.Db.Users.Add(Member("박주언", "김팀"));
        t.Db.OrgUnits.Add(Team("김팀", 1));
        t.Db.ShiftSchedules.Add(new ShiftSchedule { MemberName = "박주언", TargetDate = 어떤날, ShiftType = "주간", TeamGroup = "김팀" });
        await t.Db.SaveChangesAsync();

        await new UserService(t.Db).TeamBulkAsync(new TeamBulkRequest("김팀", "1팀", null), "tester");

        using var fresh = t.NewContext();
        Assert.Equal("1팀", fresh.Users.Single().TeamName);
        Assert.Equal("1팀", fresh.OrgUnits.Single().Name);
        Assert.Equal("1팀", fresh.ShiftSchedules.Single().TeamGroup);   // 예전에는 '김팀' 으로 남았다
        Assert.Equal(1, fresh.OrgUnits.Single().ShiftGroup);            // 교대 조는 그대로 유지
    }

    [Fact]
    public async Task 같은_조를_두_팀에_줄_수_없다()
    {
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Team("1팀", 1));
        t.Db.OrgUnits.Add(Team("2팀", 0));
        await t.Db.SaveChangesAsync();

        var svc = new UserService(t.Db);
        Assert.NotNull(await svc.SetOrgShiftGroupAsync("2팀", 1, "tester"));   // 1조는 이미 사용 중
        Assert.Null(await svc.SetOrgShiftGroupAsync("2팀", 2, "tester"));
        Assert.Null(await svc.SetOrgShiftGroupAsync("1팀", 0, "tester"));      // 해제는 언제나 가능
    }

    // ── WPF 병행 기간: 옛 팀 이름 변환 ──

    [Fact]
    public void 옛_이름은_현재_이름으로_바뀌고_나머지는_그대로다()
    {
        using var t = new TestDb();
        t.Db.OrgUnits.Add(new OrgUnit { Kind = "team", Name = "1팀", ShiftGroup = 1, LegacyNames = "김팀" });
        t.Db.OrgUnits.Add(new OrgUnit { Kind = "team", Name = "2팀", ShiftGroup = 2, LegacyNames = "장팀, 구2팀" });
        t.Db.SaveChanges();

        var a = CleanPotal.Infrastructure.Data.TeamAliases.Load(t.Db);
        Assert.False(a.IsEmpty);
        Assert.Equal("1팀", a.Normalize("김팀"));
        Assert.Equal("2팀", a.Normalize("장팀"));
        Assert.Equal("2팀", a.Normalize(" 구2팀 "));       // 공백 정리
        Assert.Equal("주간팀", a.Normalize("주간팀"));      // 별칭이 아니면 그대로
        Assert.Equal("1팀", a.Normalize("1팀"));           // 이미 현재 이름
        Assert.Equal("", a.Normalize(null));
    }

    [Fact]
    public void 별칭이_없으면_아무것도_바꾸지_않는다()
    {
        using var t = new TestDb();
        t.Db.OrgUnits.Add(new OrgUnit { Kind = "team", Name = "1팀", ShiftGroup = 1 });
        t.Db.SaveChanges();

        var a = CleanPotal.Infrastructure.Data.TeamAliases.Load(t.Db);
        Assert.True(a.IsEmpty);
        Assert.Equal("김팀", a.Normalize("김팀"));
    }

    [Fact]
    public async Task 자기_이름을_별칭으로_넣어도_저장되지_않는다()
    {
        using var t = new TestDb();
        t.Db.OrgUnits.Add(new OrgUnit { Kind = "team", Name = "1팀" });
        await t.Db.SaveChangesAsync();

        var svc = new UserService(t.Db);
        Assert.Null(await svc.SetOrgLegacyNamesAsync("1팀", "김팀, 1팀, 김팀", "tester"));

        using var fresh = t.NewContext();
        Assert.Equal("김팀", fresh.OrgUnits.Single().LegacyNames);   // 자기 이름·중복 제거
    }

    [Fact]
    public async Task 조직도에_없는_팀이나_잘못된_조는_거부한다()
    {
        using var t = new TestDb();
        var svc = new UserService(t.Db);
        Assert.NotNull(await svc.SetOrgShiftGroupAsync("없는팀", 1, "tester"));
        Assert.NotNull(await svc.SetOrgShiftGroupAsync("", 1, "tester"));
        t.Db.OrgUnits.Add(Team("1팀", 0));
        await t.Db.SaveChangesAsync();
        Assert.NotNull(await svc.SetOrgShiftGroupAsync("1팀", 3, "tester"));
    }
}
