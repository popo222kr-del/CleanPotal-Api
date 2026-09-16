using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 달력 일정에 여러 부서를 붙이는 기능.
/// 생산회의처럼 한 일정에 세정·품질·설비가 함께 들어가므로 일정↔부서를 다대다로 묶는다.
/// 부서는 이름이 아니라 Id 로 가리켜, 부서 이름을 바꿔도 일정이 따라온다.
/// </summary>
public class CalendarDeptTests
{
    private static readonly DateOnly D = new(2026, 6, 10);

    private static OrgUnit Dept(string name, string color = "", string shortName = "", bool active = true)
        => new() { Kind = "dept", Name = name, Color = color, ShortName = shortName, IsActive = active };

    private static ScheduleService Svc(TestDb t) => new(t.Db, new HolidayService(), FakeCurrentUser.Admin());

    private static TeamEventRequest Req(string content, params int[] deptIds)
        => new(D, D, content, "", deptIds);

    // ── 색·약칭 ──

    [Fact]
    public void 색을_지정하지_않으면_Id_기준으로_자동_배정된다()
    {
        // 순서 기준이면 중간 부서를 지웠을 때 나머지 색이 전부 밀린다.
        var a = DeptPalette.Resolve("", 3);
        Assert.Equal(a, DeptPalette.Resolve("", 3));            // 같은 Id → 항상 같은 색
        Assert.StartsWith("#", a);
        Assert.NotEqual(a, DeptPalette.Resolve("", 4));
    }

    [Fact]
    public void 지정한_색이_있으면_그것을_쓰고_잘못된_값은_자동으로_되돌린다()
    {
        Assert.Equal("#AABBCC", DeptPalette.Resolve("#aabbcc", 1));
        Assert.StartsWith("#", DeptPalette.Resolve("빨강", 1));   // 형식이 아니면 자동값
        Assert.StartsWith("#", DeptPalette.Resolve(null, 1));
    }

    [Fact]
    public void 약칭을_비우면_이름_앞_두_글자를_쓴다()
    {
        Assert.Equal("나노", DeptPalette.ResolveShortName("", "나노세정"));
        Assert.Equal("연구", DeptPalette.ResolveShortName(null, "연구소"));
        Assert.Equal("전산", DeptPalette.ResolveShortName("전산", "전산팀"));   // 지정값 우선
        Assert.Equal("품질", DeptPalette.ResolveShortName("", "품질"));
    }

    // ── 부서 목록 ──

    [Fact]
    public async Task 부서_목록은_조직도에_등록된_사용중인_부서만_준다()
    {
        // 사용자 소속 칸에서 유도된 부서까지 받으면 오타 하나가 별개 부서로 잡힌다.
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Dept("나노세정"));
        t.Db.OrgUnits.Add(Dept("설비"));
        t.Db.OrgUnits.Add(Dept("폐지된부서", active: false));
        t.Db.OrgUnits.Add(new OrgUnit { Kind = "team", Name = "1팀" });   // 팀은 부서가 아니다
        t.Db.Users.Add(new User { Username = "u1", RealName = "박주언", Department = "오타부서", PasswordHash = "x" });
        await t.Db.SaveChangesAsync();

        var list = await Svc(t).GetDepartmentsAsync();

        Assert.Equal(new[] { "나노세정", "설비" }, list.Select(d => d.Name));
        Assert.All(list, d => Assert.StartsWith("#", d.Color));
        Assert.All(list, d => Assert.False(string.IsNullOrEmpty(d.ShortName)));
    }

    [Fact]
    public async Task 마스터_계정뿐인_부서는_목록에서_빠진다()
    {
        // 관리자 로그인 전용 계정이 우연히 그 이름을 부서로 쓰고 있을 뿐, 일정을 잡을
        // 실제 조직이 아니다. 이름을 코드에 박지 않고 IsAdmin 플래그로 가려낸다.
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Dept("나노세정"));
        t.Db.OrgUnits.Add(Dept("관리자"));
        t.Db.Users.Add(new User { Username = "u1", RealName = "박주언", Department = "나노세정", PasswordHash = "x" });
        t.Db.Users.Add(new User { Username = "admin", RealName = "최고관리", Department = "관리자", IsAdmin = true, PasswordHash = "x" });
        await t.Db.SaveChangesAsync();

        var list = await Svc(t).GetDepartmentsAsync();

        Assert.Equal(new[] { "나노세정" }, list.Select(d => d.Name));
    }

    [Fact]
    public async Task 인원이_아직_없는_등록_부서는_그대로_보여준다()
    {
        // 연구소처럼 채용 전에 미리 등록해 둔 빈 부서는 admin-only 판정을 받지 않는다.
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Dept("연구소"));
        await t.Db.SaveChangesAsync();

        var list = await Svc(t).GetDepartmentsAsync();

        Assert.Equal(new[] { "연구소" }, list.Select(d => d.Name));
    }

    // ── 일정 ↔ 부서 ──

    [Fact]
    public async Task 한_일정에_여러_부서를_붙일_수_있다()
    {
        using var t = new TestDb();
        t.Db.OrgUnits.AddRange(Dept("나노세정"), Dept("품질"), Dept("설비"));
        await t.Db.SaveChangesAsync();
        var ids = t.Db.OrgUnits.Select(o => o.Id).ToList();

        var svc = Svc(t);
        var ev = await svc.AddTeamEventAsync(Req("생산회의", ids[0], ids[1], ids[2]), "tester");

        Assert.Equal(3, ev.Depts.Count);
        Assert.Equal(new[] { "나노세정", "설비", "품질" }, ev.Depts.Select(d => d.Name));   // 이름순
    }

    [Fact]
    public async Task 부서_이름을_바꿔도_일정이_그대로_따라온다()
    {
        // 이름으로 묶었다면 여기서 일정이 옛 부서에 묶여 사라진다(김팀 사태와 같은 함정).
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Dept("품질"));
        await t.Db.SaveChangesAsync();
        var id = t.Db.OrgUnits.Single().Id;
        await Svc(t).AddTeamEventAsync(Req("생산회의", id), "tester");

        var unit = t.Db.OrgUnits.Single();
        unit.Name = "품질보증";
        await t.Db.SaveChangesAsync();

        var events = await Svc(t).GetTeamEventsAsync(2026, 6);
        Assert.Equal("품질보증", events.Single().Depts.Single().Name);
    }

    [Fact]
    public async Task 부서를_지정하지_않은_일정도_그대로_저장된다()
    {
        // 화면에서는 이런 일정을 부서 필터와 무관하게 항상 보여준다.
        using var t = new TestDb();
        var ev = await Svc(t).AddTeamEventAsync(new TeamEventRequest(D, D, "전사 공지", ""), "tester");
        Assert.Empty(ev.Depts);
    }

    [Fact]
    public async Task 수정하면_보낸_목록이_곧_최종_상태다()
    {
        using var t = new TestDb();
        t.Db.OrgUnits.AddRange(Dept("나노세정"), Dept("품질"));
        await t.Db.SaveChangesAsync();
        var ids = t.Db.OrgUnits.Select(o => o.Id).ToList();

        var svc = Svc(t);
        var ev = await svc.AddTeamEventAsync(Req("생산회의", ids[0], ids[1]), "tester");
        var updated = await svc.UpdateTeamEventAsync(ev.Id, Req("생산회의", ids[1]));

        Assert.Equal("품질", updated!.Depts.Single().Name);
        using var fresh = t.NewContext();
        Assert.Single(fresh.TeamEventDepts);   // 빠진 연결은 지워진다
    }

    [Fact]
    public async Task 조직도에_없는_부서_Id_는_무시한다()
    {
        // 잘못된 값으로 연결이 생기면 화면에서 '사라진 부서' 처럼 보인다.
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Dept("나노세정"));
        await t.Db.SaveChangesAsync();
        var ok = t.Db.OrgUnits.Single().Id;

        var ev = await Svc(t).AddTeamEventAsync(Req("생산회의", ok, 99999), "tester");
        Assert.Equal("나노세정", ev.Depts.Single().Name);
    }

    [Fact]
    public async Task 일정을_지우면_부서_연결도_함께_지워진다()
    {
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Dept("나노세정"));
        await t.Db.SaveChangesAsync();
        var id = t.Db.OrgUnits.Single().Id;

        var svc = Svc(t);
        var ev = await svc.AddTeamEventAsync(Req("생산회의", id), "tester");
        Assert.True(await svc.DeleteTeamEventAsync(ev.Id));

        using var fresh = t.NewContext();
        Assert.Empty(fresh.TeamEventDepts);
    }

    [Fact]
    public async Task 달력에도_부서가_실려_내려온다()
    {
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Dept("나노세정", "#112233", "나노"));
        await t.Db.SaveChangesAsync();
        var id = t.Db.OrgUnits.Single().Id;
        await Svc(t).AddTeamEventAsync(Req("생산회의", id), "tester");

        var cal = await Svc(t).GetCalendarAsync(2026, 6, predict: false);
        var day = cal.Days.Single(d => d.Day == 10);
        var dept = day.Events.Single().Depts.Single();

        Assert.Equal("#112233", dept.Color);
        Assert.Equal("나노", dept.ShortName);
    }

    // ── 부서 표시 설정 ──

    [Fact]
    public async Task 잘못된_색_형식은_거부한다()
    {
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Dept("품질"));
        await t.Db.SaveChangesAsync();

        var svc = new UserService(t.Db);
        Assert.NotNull(await svc.SetDeptStyleAsync("품질", "빨강", "품질", "tester"));
        Assert.NotNull(await svc.SetDeptStyleAsync("품질", "#12345", "품질", "tester"));
        Assert.Null(await svc.SetDeptStyleAsync("품질", "#AABBCC", "품질", "tester"));
        Assert.Null(await svc.SetDeptStyleAsync("품질", "", "", "tester"));   // 비우면 자동값
    }
}
