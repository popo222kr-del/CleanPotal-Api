using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 개인별 업무 분장표 — 인원과 계정을 잇는 키 문제.
/// WorkMember.Username 에는 WPF 에서 온 <b>사번</b>이 들어 있는데, 예전에는 로그인 아이디로만
/// 찾아서 둘이 다른 사람은 화면에 이름 대신 사번이 그대로 찍혔다.
/// </summary>
public class WorkAssignmentServiceTests
{
    private static User NewUser(string login, string realName, string empNo, string team = "주간팀", string jobTitle = "사원")
        => new() { Username = login, RealName = realName, EmployeeNumber = empNo, TeamName = team, JobTitle = jobTitle, PasswordHash = "x" };

    private static async Task<TestDb> Seed()
    {
        var t = new TestDb();
        // 로그인 아이디 = 사번 (대부분의 직원)
        t.Db.Users.Add(NewUser("2305557", "곽병호", "2305557", "김팀", "QA"));
        // 로그인 아이디 ≠ 사번 (실제로 화면이 깨졌던 두 사람)
        t.Db.Users.Add(NewUser("0907", "김태종", "1210045"));
        t.Db.Users.Add(NewUser("5812", "박광순", "2605812"));

        t.Db.WorkMembers.Add(new WorkMember { Username = "2305557" });
        t.Db.WorkMembers.Add(new WorkMember { Username = "1210045" });   // 사번으로 저장돼 있다
        t.Db.WorkMembers.Add(new WorkMember { Username = "2605812" });
        await t.Db.SaveChangesAsync();
        return t;
    }

    [Fact]
    public async Task 로그인_아이디가_사번과_같으면_예전처럼_찾는다()
    {
        using var t = await Seed();
        var list = await new WorkAssignmentService(t.Db).GetMembersAsync(includeHidden: false);
        var m = list.Single(x => x.Username == "2305557");
        Assert.Equal("곽병호", m.RealName);
        Assert.Equal("김팀", m.TeamName);
        Assert.Equal("QA", m.JobTitle);
    }

    [Fact]
    public async Task 로그인_아이디가_사번과_달라도_사번으로_찾는다()
    {
        using var t = await Seed();
        var list = await new WorkAssignmentService(t.Db).GetMembersAsync(includeHidden: false);

        var 김태종 = list.Single(x => x.Username == "1210045");
        Assert.Equal("김태종", 김태종.RealName);       // 예전에는 "1210045" 가 그대로 찍혔다
        Assert.Equal("주간팀", 김태종.TeamName);

        var 박광순 = list.Single(x => x.Username == "2605812");
        Assert.Equal("박광순", 박광순.RealName);
    }

    [Fact]
    public async Task 상세_조회도_같은_기준으로_찾는다()
    {
        using var t = await Seed();
        var detail = await new WorkAssignmentService(t.Db).GetMemberAsync("1210045");
        Assert.NotNull(detail);
        Assert.Equal("김태종", detail!.Member.RealName);
    }

    [Fact]
    public async Task 계정을_못_찾으면_사번을_이름인_것처럼_보여주지_않는다()
    {
        using var t = new TestDb();
        t.Db.WorkMembers.Add(new WorkMember { Username = "9999999" });   // 대응하는 계정 없음
        await t.Db.SaveChangesAsync();

        var m = (await new WorkAssignmentService(t.Db).GetMembersAsync(false)).Single();
        Assert.Contains("계정 미등록", m.RealName);   // 사번이 이름 자리에 그대로 들어가지 않는다
        Assert.Equal("", m.TeamName);
    }

    [Fact]
    public async Task 사번이_중복_입력된_계정이_있어도_터지지_않는다()
    {
        using var t = new TestDb();
        t.Db.Users.Add(NewUser("a1", "이름A", "1234567"));
        t.Db.Users.Add(NewUser("a2", "이름B", "1234567"));   // 사번 중복
        t.Db.WorkMembers.Add(new WorkMember { Username = "1234567" });
        await t.Db.SaveChangesAsync();

        var m = (await new WorkAssignmentService(t.Db).GetMembersAsync(false)).Single();
        Assert.Contains(m.RealName, new[] { "이름A", "이름B" });
    }
}
