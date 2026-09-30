using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>권한 프리셋(역할) — 관리자가 화면에서 만들고 고치고, 여러 사람에게 한 번에 적용한다.</summary>
public class PermissionPresetTests
{
    [Fact]
    public async Task 처음_열면_기본_프리셋으로_채운다()
    {
        using var t = new TestDb();
        var list = await new PermissionPresetService(t.Db).GetAllAsync();
        Assert.Equal(new[] { "현장 작업자", "현장 리더", "물류 담당", "품질 담당", "Office", "조회 전용", "타 부서" }, list.Select(p => p.Name));
        var worker = list[0];
        Assert.Equal(2, worker.AccessField);
        Assert.Contains("\"/dispatch\"", worker.ReadOnlyMenus);
        Assert.Equal(list.Count, (await new PermissionPresetService(t.Db).GetAllAsync()).Count);   // 두 번 채우지 않는다
    }

    [Fact]
    public async Task 목록을_저장하면_순서_추가_수정_삭제가_그대로_반영된다()
    {
        using var t = new TestDb();
        var svc = new PermissionPresetService(t.Db);
        var list = (await svc.GetAllAsync()).ToList();
        var worker = list[0] with { ReadOnlyMenus = "[\"/notice\",\"/notice\",\"bad\"]", AccessHandover = 9 };
        var added = new PermissionPresetDto(0, " 설비 담당 ", "새 역할", 0, 1, 1, 1, 2, 1, 0, 1, "[]", "[\"/quotation\"]");
        var saved = await svc.SaveAllAsync(new List<PermissionPresetDto> { added, worker }, "admin");

        Assert.Equal(new[] { "설비 담당", "현장 작업자" }, saved.Select(p => p.Name));   // 보낸 순서, 나머지는 지움
        Assert.Equal("[\"/notice\"]", saved[1].ReadOnlyMenus);                            // 경로만·중복 없이
        Assert.Equal(2, saved[1].AccessHandover);                                        // 등급은 0~2
        Assert.Equal(2, await t.Db.PermissionPresets.CountAsync());
    }

    [Fact]
    public async Task 이름이_비거나_겹치거나_목록이_비면_저장하지_않는다()
    {
        using var t = new TestDb();
        var svc = new PermissionPresetService(t.Db);
        var p = new PermissionPresetDto(0, "A", "", 0, 1, 1, 1, 1, 1, 0, 1, "[]", "[]");
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveAllAsync(new List<PermissionPresetDto>(), "admin"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveAllAsync(new List<PermissionPresetDto> { p with { Name = " " } }, "admin"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveAllAsync(new List<PermissionPresetDto> { p, p }, "admin"));
    }

    [Fact]
    public async Task 여러_사람에게_적용하면_등급과_메뉴_설정이_바뀌고_관리자는_건너뛴다()
    {
        using var t = new TestDb();
        var a = new User { Username = "a", RealName = "가", AccessOffice = 2, HiddenMenus = "[\"/checklist\"]" };
        var b = new User { Username = "b", RealName = "나" };
        var adm = new User { Username = "adm", RealName = "관리", IsAdmin = true };
        t.Db.Users.AddRange(a, b, adm);
        await t.Db.SaveChangesAsync();
        var svc = new PermissionPresetService(t.Db);
        var worker = (await svc.GetAllAsync()).First(p => p.Name == "현장 작업자");

        var n = await svc.ApplyAsync(worker.Id, new List<int> { a.Id, b.Id, adm.Id }, "admin");

        Assert.Equal(2, n);
        await t.Db.Entry(a).ReloadAsync();
        Assert.Equal(0, a.AccessOffice);
        Assert.Equal(2, a.AccessHandover);
        Assert.Equal("[]", a.HiddenMenus);
        Assert.Contains("/dispatch", a.ReadOnlyMenus);
        Assert.Equal(2, await t.Db.UserAuditLogs.CountAsync(l => l.Detail == "프리셋 '현장 작업자' 적용"));
    }
}
