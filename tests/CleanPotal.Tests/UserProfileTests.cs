using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 기본 정보의 직급(호칭)과 근속.
/// 직급은 직위(QA팀장·세정팀장 등 맡은 일)와 별개 항목이다.
/// </summary>
public class UserProfileTests
{
    private static UserUpsertRequest Req(string username, string rank = "", string jobTitle = "", string hireDate = "") =>
        new(username, "pw1234", RealName: username, Department: "나노세정", TeamName: "1팀",
            Rank: rank, JobTitle: jobTitle, Email: "", PhoneNumber: "", EmployeeNumber: username,
            HireDate: hireDate, IsResigned: false, ResignDate: "", IsAdmin: false,
            AccessSchedule: 1, AccessRoster: 1, AccessHandover: 1, AccessField: 1, AccessOffice: 0,
            AccessMes: 1, HiddenMenus: null);

    [Fact]
    public void 직급_서열은_위일수록_작다()
    {
        Assert.True(JobRank.Order("사장") < JobRank.Order("부장"));
        Assert.True(JobRank.Order("부장") < JobRank.Order("대리"));
        Assert.True(JobRank.Order("대리") < JobRank.Order("사원"));
    }

    [Fact]
    public void 직급이_비었거나_목록에_없으면_서열은_맨_뒤()
    {
        Assert.Equal(int.MaxValue, JobRank.Order(""));
        Assert.Equal(int.MaxValue, JobRank.Order(null));
        Assert.Equal(int.MaxValue, JobRank.Order("촉탁"));   // 목록 밖 값도 저장은 되게 두고 서열만 미상
    }

    [Fact]
    public async Task 직급과_직위는_따로_저장된다()
    {
        using var t = new TestDb();
        var svc = new UserService(t.Db);

        var dto = await svc.CreateAsync(Req("2305553", rank: "차장", jobTitle: "QA팀장"), "tester");

        Assert.Equal("차장", dto.Rank);
        Assert.Equal("QA팀장", dto.JobTitle);
    }

    [Fact]
    public async Task 목록에_없는_직급도_저장은_막지_않는다()
    {
        // 직급 체계가 바뀌었을 때 배포 없이 기존 값을 그대로 쓸 수 있어야 한다.
        using var t = new TestDb();
        var dto = await new UserService(t.Db).CreateAsync(Req("1001", rank: "촉탁"), "tester");
        Assert.Equal("촉탁", dto.Rank);
    }

    [Fact]
    public async Task 근속은_입사일에서_계산돼_함께_내려온다()
    {
        // 계산 규칙 자체는 TenureTests 가 검증한다. 여기서는 그 값이 DTO 에 실려 오는지만 본다
        // (화면마다 따로 계산하면 표기가 갈린다).
        using var t = new TestDb();
        var hire = DateOnly.FromDateTime(DateTime.Today).AddYears(-3).ToString("yyyy-MM-dd");

        var dto = await new UserService(t.Db).CreateAsync(Req("1002", hireDate: hire), "tester");

        Assert.Equal(Tenure.Format(hire), dto.Tenure);
        Assert.NotEqual("", dto.Tenure);
    }

    [Fact]
    public async Task 입사일이_없거나_해석_불가면_근속은_빈칸이다()
    {
        // 아무 숫자나 만들어 보여주면 틀린 경력이 사실처럼 보인다.
        using var t = new TestDb();
        var svc = new UserService(t.Db);

        Assert.Equal("", (await svc.CreateAsync(Req("1003"), "tester")).Tenure);
        Assert.Equal("", (await svc.CreateAsync(Req("1004", hireDate: "미정"), "tester")).Tenure);
    }

    [Fact]
    public async Task WPF_시절_점_표기_입사일도_근속이_나온다()
    {
        // 입사일은 DB 에 문자열로 들어 있고 2018.06.01 같은 표기가 섞여 있다.
        using var t = new TestDb();
        var hire = DateOnly.FromDateTime(DateTime.Today).AddYears(-1).ToString("yyyy.MM.dd");

        var dto = await new UserService(t.Db).CreateAsync(Req("1005", hireDate: hire), "tester");

        Assert.NotEqual("", dto.Tenure);   // 점 표기를 해석하지 못하면 빈칸이 된다
        Assert.Equal(Tenure.Format(hire), dto.Tenure);
    }
}
