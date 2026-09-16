using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// MES 세부 권한을 누가 언제 켜 줬는지 남는가.
///
/// 이 권한에는 공정 무효화(지나간 이력을 무효 처리)가 들어 있다. 등급(0/1/2)과 다른 축이라
/// 기존 권한 변경 diff 에 잡히지 않아, 적어 두지 않으면 나중에 확인할 방법이 없다.
/// </summary>
public class MesPermissionAuditTests
{
    private static UserUpsertRequest Req(string username, string? mesPermissions = null) =>
        new(username, "pw1234", RealName: username, Department: "나노세정", TeamName: "1팀",
            Rank: "", JobTitle: "", Email: "", PhoneNumber: "", EmployeeNumber: username,
            HireDate: "", IsResigned: false, ResignDate: "", IsAdmin: false,
            AccessSchedule: 1, AccessRoster: 1, AccessHandover: 1, AccessField: 1, AccessOffice: 0,
            AccessMes: 2, MesPermissions: mesPermissions, HiddenMenus: null);

    [Fact]
    public async Task 세부_권한을_켜고_끄면_감사_로그에_한글_이름으로_남는다()
    {
        using var t = new TestDb();
        var svc = new UserService(t.Db);

        var user = await svc.CreateAsync(Req("2305553", MesPermissionCodes.AdminProduct), "관리자");
        await svc.UpdateAsync(user.Id,
            Req("2305553", $"{MesPermissionCodes.Rollback},{MesPermissionCodes.AdminCustomer}"), "관리자");

        var audit = await svc.GetAuditAsync();
        var change = audit.First(a => a.Action == "권한 변경");

        Assert.Contains("+공정 무효화", change.Detail);
        Assert.Contains("+업체 마스터", change.Detail);
        Assert.Contains("-제품 마스터", change.Detail);   // 거둔 것도 남아야 한다
        Assert.Equal("관리자", change.ByUser);
    }

    [Fact]
    public async Task 세부_권한이_그대로면_권한_변경으로_남기지_않는다()
    {
        using var t = new TestDb();
        var svc = new UserService(t.Db);

        var user = await svc.CreateAsync(Req("1001", MesPermissionCodes.AdminProduct), "관리자");
        // 이름만 바꾼다 — 권한은 그대로다.
        var same = Req("1001", MesPermissionCodes.AdminProduct) with { RealName = "이름 바뀜" };
        await svc.UpdateAsync(user.Id, same, "관리자");

        Assert.DoesNotContain(await svc.GetAuditAsync(), a => a.Action == "권한 변경");
    }

    [Fact]
    public async Task 계정을_만들_때_켜_준_세부_권한도_남는다()
    {
        using var t = new TestDb();
        var svc = new UserService(t.Db);

        await svc.CreateAsync(Req("1002", MesPermissionCodes.Rollback), "관리자");

        var created = (await svc.GetAuditAsync()).First(a => a.Action == "생성");
        Assert.Contains("공정 무효화", created.Detail);
    }

    [Fact]
    public async Task 모르는_코드는_저장되지_않는다()
    {
        using var t = new TestDb();
        var svc = new UserService(t.Db);

        var user = await svc.CreateAsync(Req("1003", "NotARealCode,AdminProduct"), "관리자");

        Assert.Equal(MesPermissionCodes.AdminProduct, user.MesPermissions);
    }
}
