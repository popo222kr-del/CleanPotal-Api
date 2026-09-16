using CleanPotal.Core;
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
    private static readonly DateOnly 오늘 = DateOnly.FromDateTime(DateTime.Today);

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

        var roster = await new ScheduleService(t.Db, new HolidayService(), FakeCurrentUser.Admin())
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

        var names = await new ScheduleService(t.Db, new HolidayService(), FakeCurrentUser.Admin()).GetProductionTeamsAsync();
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

    [Fact]
    public async Task 교대조_미지정_상태에서_옛_이름_팀은_오늘_현황에_유령으로_뜨지_않는다()
    {
        // 팀 이름을 이미 바꾼 뒤 교대 조를 아직 지정하지 않은 상태.
        // 아무도 없는 '김팀 0명' 상자가 뜨면 전원 휴무처럼 보여 헷갈린다.
        using var t = new TestDb();
        t.Db.Users.Add(Member("박주언", "1팀"));
        t.Db.Users.Add(Member("홍길동", "Office"));
        await t.Db.SaveChangesAsync();

        var status = await new ScheduleService(t.Db, new HolidayService(), FakeCurrentUser.Admin()).GetTodayStatusAsync();
        var names = status.Teams.Select(x => x.Team).ToList();

        Assert.DoesNotContain("김팀", names);
        Assert.DoesNotContain("장팀", names);
        Assert.Contains("1팀", names);
        Assert.Contains("Office", names);
    }

    [Fact]
    public async Task 교대조를_지정하면_인원이_없어도_그_팀은_보여준다()
    {
        // 전원 휴무도 의미 있는 정보다.
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Team("1팀", 1));
        t.Db.OrgUnits.Add(Team("2팀", 2));
        t.Db.Users.Add(Member("박주언", "1팀"));
        await t.Db.SaveChangesAsync();

        var status = await new ScheduleService(t.Db, new HolidayService(), FakeCurrentUser.Admin()).GetTodayStatusAsync();
        var names = status.Teams.Select(x => x.Team).ToList();

        Assert.Equal(new[] { "1팀", "2팀" }, names);
    }

    [Fact]
    public async Task 달력_근태_집계는_생산팀이_아닌_부서도_포함한다()
    {
        // 예전에는 교대 생산팀만 세어서, Office 사람이 연차를 등록해도 달력에 나오지 않았다
        // (근태 등록 화면은 전 직원을 받는데 달력만 걸러내고 있었다).
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Team("1팀", 1));
        t.Db.OrgUnits.Add(Team("2팀", 2));
        t.Db.Users.Add(Member("박주언", "Office"));
        t.Db.Users.Add(Member("김단비", "주간팀"));
        t.Db.Users.Add(Member("홍길동", "1팀"));
        t.Db.ShiftSchedules.Add(new ShiftSchedule { MemberName = "박주언", TargetDate = 어떤날, ShiftType = "연차" });
        t.Db.ShiftSchedules.Add(new ShiftSchedule { MemberName = "김단비", TargetDate = 어떤날, ShiftType = "휴무" });
        await t.Db.SaveChangesAsync();

        var cal = await new ScheduleService(t.Db, new HolidayService(), FakeCurrentUser.Admin()).GetCalendarAsync(2026, 6, predict: true);
        var day = cal.Days.Single(d => d.Date == 어떤날);

        Assert.Contains("박주언(연차)", day.OffShift);
        Assert.Contains("김단비(휴무)", day.OffShift);
    }

    [Fact]
    public async Task 교대_근무가_아닌_팀은_주야_예측_대상이_아니다()
    {
        // 연구소·전산에 주간/야간 개념이 없으므로, 근태를 등록하지 않았으면 아무것도 잡히지 않는다.
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Team("1팀", 1));
        t.Db.Users.Add(Member("박주언", "Office"));
        await t.Db.SaveChangesAsync();

        var cal = await new ScheduleService(t.Db, new HolidayService(), FakeCurrentUser.Admin()).GetCalendarAsync(2026, 6, predict: true);
        var day = cal.Days.Single(d => d.Date == 어떤날);

        Assert.DoesNotContain("박주언", day.DayShift);
        Assert.DoesNotContain("박주언", day.NightShift);
        Assert.Empty(day.OffShift);
    }

    // ── 오늘 현황의 표시 단위 (교대 생산팀 = 팀 / 나머지 = 등록 부서) ──

    private static OrgUnit Dept(string name, int order = 0) =>
        new() { Kind = "dept", Name = name, OrderIndex = order };

    private static User Member(string name, string team, string dept) =>
        new() { Username = name, RealName = name, TeamName = team, Department = dept, PasswordHash = "x" };

    [Fact]
    public async Task 부서를_등록하면_오늘_현황은_생산팀과_등록_부서로_묶인다()
    {
        // 예전에는 User.TeamName 을 그대로 나열해서 '관리자'처럼 근무와 무관한 팀이 올라오고,
        // 조직도에 등록한 부서(연구소 등)는 따로 묶이지 않았다.
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Team("1팀", 1));
        t.Db.OrgUnits.Add(Team("2팀", 2));
        t.Db.OrgUnits.Add(Dept("나노세정", 1));
        t.Db.OrgUnits.Add(Dept("연구소", 2));
        t.Db.OrgUnits.Add(Dept("품질", 3));                     // 인원 없음 → 빈 줄을 만들지 않는다
        t.Db.Users.Add(Member("박주언", "1팀", "나노세정"));
        t.Db.Users.Add(Member("홍길동", "2팀", "나노세정"));
        t.Db.Users.Add(Member("김단비", "주간팀", "나노세정"));   // 교대가 아닌 팀 → 부서로 묶인다
        t.Db.Users.Add(Member("이연구", "", "연구소"));
        await t.Db.SaveChangesAsync();

        var status = await new ScheduleService(t.Db, new HolidayService(), FakeCurrentUser.Admin()).GetTodayStatusAsync();

        Assert.Equal(new[] { "1팀", "2팀", "나노세정", "연구소" }, status.Teams.Select(x => x.Team));
    }

    [Fact]
    public async Task 등록_부서에_속하지_않은_계정은_오늘_현황에_줄을_만들지_않는다()
    {
        // 실제 증상: 관리자 전용 계정 때문에 '관리자' 줄이 떴다.
        // 이름으로 거르지 않고, 조직도에 등록된 부서만 싣는 방식으로 막는다.
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Team("1팀", 1));
        t.Db.OrgUnits.Add(Dept("나노세정"));
        t.Db.Users.Add(Member("박주언", "1팀", "나노세정"));
        t.Db.Users.Add(Member("최고관리", "관리자", ""));
        await t.Db.SaveChangesAsync();

        var status = await new ScheduleService(t.Db, new HolidayService(), FakeCurrentUser.Admin()).GetTodayStatusAsync();

        Assert.DoesNotContain("관리자", status.Teams.Select(x => x.Team));
    }

    [Fact]
    public async Task 부서_줄에도_등록한_근태가_그대로_나온다()
    {
        using var t = new TestDb();
        t.Db.OrgUnits.Add(Dept("연구소"));
        t.Db.Users.Add(Member("이연구", "", "연구소"));
        t.Db.ShiftSchedules.Add(new ShiftSchedule { MemberName = "이연구", TargetDate = 오늘, ShiftType = "연차" });
        await t.Db.SaveChangesAsync();

        var status = await new ScheduleService(t.Db, new HolidayService(), FakeCurrentUser.Admin()).GetTodayStatusAsync();
        var row = status.Teams.Single(x => x.Team == "연구소");

        Assert.Contains("이연구(연차)", row.Badges.Single(b => b.Kind == "off").Names);
    }

    // ── 근태 등록 대상 범위 (관리자 / 일반 직원) ──

    private static TestDb SeedOrg()
    {
        var t = new TestDb();
        t.Db.Users.Add(new User { Username = "a", RealName = "박주언", Department = "나노세정", TeamName = "Office", PasswordHash = "x" });
        t.Db.Users.Add(new User { Username = "b", RealName = "홍길동", Department = "나노세정", TeamName = "1팀", PasswordHash = "x" });
        t.Db.Users.Add(new User { Username = "c", RealName = "김민수", Department = "설비", TeamName = "설비", PasswordHash = "x" });
        t.Db.SaveChanges();
        return t;
    }

    [Fact]
    public async Task 관리자는_전_직원을_볼_수_있다()
    {
        using var t = SeedOrg();
        var svc = new ScheduleService(t.Db, new HolidayService(), FakeCurrentUser.Admin());
        var list = await svc.GetMembersAsync();
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public async Task 일반_직원은_자기_부서_사람만_보인다()
    {
        using var t = SeedOrg();
        var svc = new ScheduleService(t.Db, new HolidayService(), FakeCurrentUser.In("박주언", "나노세정", "Office"));
        var names = (await svc.GetMembersAsync()).Select(m => m.RealName).ToList();

        Assert.Contains("박주언", names);
        Assert.Contains("홍길동", names);      // 같은 부서, 다른 팀
        Assert.DoesNotContain("김민수", names); // 다른 부서
    }

    [Fact]
    public async Task 부서가_없으면_자기_팀_기준으로_본다()
    {
        using var t = SeedOrg();
        var svc = new ScheduleService(t.Db, new HolidayService(), FakeCurrentUser.In("홍길동", "", "1팀"));
        var names = (await svc.GetMembersAsync()).Select(m => m.RealName).ToList();

        Assert.Equal(new[] { "홍길동" }, names);   // 1팀은 본인뿐
    }

    [Fact]
    public async Task 소속이_없어도_본인은_항상_보인다()
    {
        // 자기 연차는 스스로 넣을 수 있어야 한다.
        using var t = SeedOrg();
        var svc = new ScheduleService(t.Db, new HolidayService(), FakeCurrentUser.In("박주언", "", ""));
        Assert.Equal(new[] { "박주언" }, (await svc.GetMembersAsync()).Select(m => m.RealName));
    }

    [Fact]
    public async Task 화면을_거치지_않고_남의_근태를_넣으려_하면_막힌다()
    {
        // 목록만 줄이면 요청을 직접 보내 남의 근태를 넣을 수 있다.
        using var t = SeedOrg();
        var svc = new ScheduleService(t.Db, new HolidayService(), FakeCurrentUser.In("박주언", "나노세정", "Office"));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => svc.RegisterAttendanceAsync(new AttendanceRequest("김민수", 어떤날, 어떤날, "연차"), "박주언"));

        // 같은 부서 사람은 정상 등록된다
        Assert.True(await svc.RegisterAttendanceAsync(new AttendanceRequest("홍길동", 어떤날, 어떤날, "연차"), "박주언") > 0);
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
    public async Task 소속_인원이_없는_팀이나_잘못된_조는_거부한다()
    {
        using var t = new TestDb();
        var svc = new UserService(t.Db);
        Assert.NotNull(await svc.SetOrgShiftGroupAsync("없는팀", 1, "tester"));
        Assert.NotNull(await svc.SetOrgShiftGroupAsync("", 1, "tester"));
        t.Db.OrgUnits.Add(Team("1팀", 0));
        await t.Db.SaveChangesAsync();
        Assert.NotNull(await svc.SetOrgShiftGroupAsync("1팀", 3, "tester"));
    }

    [Fact]
    public async Task 조직도에_등록하지_않은_자동_팀에도_교대조를_지정할_수_있다()
    {
        // 조직도의 팀은 대부분 '자동'(사용자 소속에서 유도)이라 등록부에 행이 없다.
        // 여기서 막으면 "먼저 팀을 등록하세요" 라는 막다른 길이 된다.
        using var t = new TestDb();
        t.Db.Users.Add(Member("박주언", "1팀"));
        await t.Db.SaveChangesAsync();
        Assert.Empty(t.Db.OrgUnits);

        var svc = new UserService(t.Db);
        Assert.Null(await svc.SetOrgShiftGroupAsync("1팀", 1, "tester", parent: "세정"));

        using var fresh = t.NewContext();
        var unit = fresh.OrgUnits.Single();
        Assert.Equal("1팀", unit.Name);
        Assert.Equal("세정", unit.Parent);
        Assert.Equal(1, unit.ShiftGroup);
    }

    [Fact]
    public async Task 소속_인원이_없는_이름으로는_팀이_새로_생기지_않는다()
    {
        // 오타로 엉뚱한 팀이 등록되는 것을 막는다.
        using var t = new TestDb();
        t.Db.Users.Add(Member("박주언", "1팀"));
        await t.Db.SaveChangesAsync();

        var svc = new UserService(t.Db);
        Assert.NotNull(await svc.SetOrgShiftGroupAsync("1틈", 1, "tester"));

        using var fresh = t.NewContext();
        Assert.Empty(fresh.OrgUnits);
    }
}
