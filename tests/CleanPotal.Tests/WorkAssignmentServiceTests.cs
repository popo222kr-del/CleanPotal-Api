using CleanPotal.Core;
using CleanPotal.Core.DTOs;
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
    public async Task 목록에_부서_사번_재직여부가_함께_내려온다()
    {
        // 사용자 계정 관리 화면과 같은 목록(부서·팀·직위 / 사번 / 재직·퇴사 탭)을 그리려면
        // 이 값들이 DTO 에 실려야 한다.
        using var t = new TestDb();
        var u = NewUser("0907", "김태종", "1210045", "세정", "세정");
        u.Department = "세정";
        u.IsResigned = true;
        u.ResignDate = "2026-03-31";
        t.Db.Users.Add(u);
        t.Db.WorkMembers.Add(new WorkMember { Username = "1210045" });
        await t.Db.SaveChangesAsync();

        var m = (await new WorkAssignmentService(t.Db).GetMembersAsync(false)).Single();
        Assert.Equal("세정", m.Department);
        Assert.Equal("1210045", m.EmployeeNumber);
        Assert.True(m.IsResigned);              // 계정이 정본 — 계정 관리 화면과 인원수가 어긋나지 않는다
        Assert.Equal("2026-03-31", m.ResignDate);
        Assert.True(m.HasAccount);
    }

    [Fact]
    public async Task 기본_정보는_계정에서_가져오고_경력은_입사일로_계산한다()
    {
        using var t = new TestDb();
        var u = NewUser("1806224", "고은경", "1806224", "Office", "대리");
        u.Department = "Office";
        u.HireDate = "2018-06-01";
        u.Email = "ek.ko@aets.co.kr";
        u.PhoneNumber = "010-8583-5576";
        t.Db.Users.Add(u);
        t.Db.WorkMembers.Add(new WorkMember { Username = "1806224" });
        await t.Db.SaveChangesAsync();

        var m = (await new WorkAssignmentService(t.Db).GetMembersAsync(false)).Single();
        Assert.Equal("2018-06-01", m.HireDate);
        Assert.Equal("ek.ko@aets.co.kr", m.Email);
        Assert.Equal("010-8583-5576", m.PhoneNumber);
        Assert.False(string.IsNullOrEmpty(m.Tenure));   // 기준일이 흐르므로 값 자체는 Tenure 테스트에서 검증
    }

    [Fact]
    public async Task 계정을_못_찾으면_기본_정보가_비어_있고_경력도_지어내지_않는다()
    {
        using var t = new TestDb();
        t.Db.WorkMembers.Add(new WorkMember { Username = "9999999" });
        await t.Db.SaveChangesAsync();

        var m = (await new WorkAssignmentService(t.Db).GetMembersAsync(false)).Single();
        Assert.False(m.HasAccount);
        Assert.Equal("", m.HireDate);
        Assert.Equal("", m.Tenure);
        Assert.Equal("", m.Email);
        Assert.Equal("", m.PhoneNumber);
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
        Assert.False(m.HasAccount);
        Assert.Equal("9999999", m.EmployeeNumber);   // 계정이 없으면 분장표 키가 곧 사번
        Assert.False(m.IsResigned);
    }

    [Fact]
    public async Task 교육_일자는_시작_종료일에서_만들어지고_적재_순서대로_나온다()
    {
        using var t = await Seed();
        // WPF 에서 온 실제 모양: EduDate 는 비어 있고 StartDate/EndDate 에만 값이 있다.
        t.Db.WorkEdus.Add(new WorkEdu { Username = "1210045", EduName = "1. 환경안전 교육", StartDate = "2018-06-04" });
        t.Db.WorkEdus.Add(new WorkEdu { Username = "1210045", EduName = "3. 현장 실습 교육", StartDate = "2018-06-04", EndDate = "2018-06-07" });
        t.Db.WorkEdus.Add(new WorkEdu { Username = "1210045", EduName = "4. 사원평가" });   // 미이수
        await t.Db.SaveChangesAsync();

        var detail = await new WorkAssignmentService(t.Db).GetMemberAsync("1210045");
        var edus = detail!.Edus;

        Assert.Equal(new[] { "1. 환경안전 교육", "3. 현장 실습 교육", "4. 사원평가" },
                     edus.Select(e => e.EduName));          // 적재 순서 유지
        Assert.Equal("2018-06-04", edus[0].EduDateText);
        Assert.Equal("2018-06-04~07", edus[1].EduDateText);  // WPF 표기와 동일
        Assert.Equal("", edus[2].EduDateText);
    }

    [Fact]
    public async Task 한_사람이_아이디와_사번으로_두_번_등록되면_중복으로_드러난다()
    {
        // 운영 DB 에서 실제로 났던 상황: 김태종 님이 '1210045'(사번)과 '0907'(로그인)로 두 줄.
        // 사번으로도 계정을 찾게 되면서 두 줄이 똑같아 보이므로, 어느 쪽이 빈 껍데기인지
        // 구분할 수 있어야 한다.
        using var t = new TestDb();
        t.Db.Users.Add(NewUser("0907", "김태종", "1210045"));
        t.Db.WorkMembers.Add(new WorkMember { Username = "1210045" });   // 내용이 붙어 있는 쪽
        t.Db.WorkMembers.Add(new WorkMember { Username = "0907" });      // 빈 껍데기
        t.Db.WorkAccounts.Add(new WorkAccount { Username = "1210045", ServiceName = "상생협력아카데미" });
        t.Db.WorkEdus.Add(new WorkEdu { Username = "1210045", EduName = "1. 환경안전 교육" });
        await t.Db.SaveChangesAsync();

        var list = await new WorkAssignmentService(t.Db).GetMembersAsync(false);

        Assert.Equal(2, list.Count);
        Assert.All(list, m => Assert.Equal("김태종", m.RealName));
        // 두 줄이 같은 계정을 가리킨다는 사실이 드러나야 한다
        Assert.Single(list.Select(m => m.LinkedUserId).Distinct());

        var 내용있는쪽 = list.Single(m => m.Username == "1210045");
        var 빈쪽 = list.Single(m => m.Username == "0907");
        Assert.Equal(1, 내용있는쪽.AccountCount);
        Assert.Equal(1, 내용있는쪽.EduCount);
        Assert.Equal(0, 빈쪽.AccountCount);
        Assert.Equal(0, 빈쪽.EduCount);
    }

    [Fact]
    public async Task 이미_다른_키로_등록된_사람은_또_추가되지_않는다()
    {
        using var t = new TestDb();
        t.Db.Users.Add(NewUser("0907", "김태종", "1210045"));
        t.Db.WorkMembers.Add(new WorkMember { Username = "1210045" });
        await t.Db.SaveChangesAsync();

        var svc = new WorkAssignmentService(t.Db);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => svc.AddMemberAsync(new WorkMemberUpsertRequest("0907", false, "")));
        Assert.Contains("김태종", ex.Message);
        Assert.Contains("1210045", ex.Message);
    }

    [Fact]
    public async Task 숨김_인원도_목록에_포함해_돌려준다()
    {
        // 서버에서 미리 걸러내면 '숨김 처리된 퇴사자'가 퇴사자 탭에서도 사라진다.
        // 구분은 화면에서 한다(사용자 계정 관리와 같은 방식).
        using var t = new TestDb();
        var u = NewUser("0907", "김태종", "1210045");
        u.IsResigned = true;
        t.Db.Users.Add(u);
        t.Db.WorkMembers.Add(new WorkMember { Username = "1210045", IsHidden = true });
        await t.Db.SaveChangesAsync();

        var m = (await new WorkAssignmentService(t.Db).GetMembersAsync(includeHidden: false)).Single();
        Assert.True(m.IsHidden);
        Assert.True(m.IsResigned);
    }

    // ── 외부 교육 기록 (교육 현황 대시보드 연동) ──

    [Fact]
    public async Task 외부_교육은_이름으로_대시보드에서_끌어오고_최근_것이_위로_온다()
    {
        using var t = await Seed();
        t.Db.EducationPlans.Add(new EducationPlan { MemberName = "김태종", CourseName = "품질 심화", StartDate = new DateOnly(2025, 3, 2), EndDate = new DateOnly(2025, 3, 4), Status = "완료", Progress = 100, EduMethod = "집합" });
        t.Db.EducationPlans.Add(new EducationPlan { MemberName = "김태종", CourseName = "안전 보수", StartDate = new DateOnly(2026, 5, 1), Status = "진행", Progress = 40 });
        t.Db.EducationPlans.Add(new EducationPlan { MemberName = "곽병호", CourseName = "남의 교육", StartDate = new DateOnly(2026, 6, 1) });
        await t.Db.SaveChangesAsync();

        var detail = await new WorkAssignmentService(t.Db).GetMemberAsync("1210045");

        Assert.Equal(new[] { "안전 보수", "품질 심화" }, detail!.ExternalEdus.Select(e => e.CourseName));
        Assert.DoesNotContain(detail.ExternalEdus, e => e.CourseName == "남의 교육");
        Assert.False(detail.ExternalEduNameAmbiguous);
    }

    [Fact]
    public async Task 동명이인이면_남의_교육이_섞일_수_있다고_알린다()
    {
        using var t = await Seed();
        t.Db.Users.Add(NewUser("9999", "김태종", "9999999"));   // 같은 이름, 다른 계정
        t.Db.EducationPlans.Add(new EducationPlan { MemberName = "김태종", CourseName = "안전 보수" });
        await t.Db.SaveChangesAsync();

        var detail = await new WorkAssignmentService(t.Db).GetMemberAsync("1210045");

        Assert.Single(detail!.ExternalEdus);
        Assert.True(detail.ExternalEduNameAmbiguous);   // 숨기지 않고 화면에서 경고하게 한다
    }

    [Fact]
    public async Task 계정이_연결되지_않은_인원은_외부_교육을_끌어오지_않는다()
    {
        // 이름이 비어 있는데 조회하면 남의 기록이 통째로 딸려 올 수 있다.
        using var t = new TestDb();
        t.Db.WorkMembers.Add(new WorkMember { Username = "9999999" });
        t.Db.EducationPlans.Add(new EducationPlan { MemberName = "", CourseName = "이름 없는 기록" });
        t.Db.EducationPlans.Add(new EducationPlan { MemberName = "곽병호", CourseName = "남의 교육" });
        await t.Db.SaveChangesAsync();

        var detail = await new WorkAssignmentService(t.Db).GetMemberAsync("9999999");

        Assert.Empty(detail!.ExternalEdus);
        Assert.False(detail.ExternalEduNameAmbiguous);
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
