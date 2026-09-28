using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using CleanPotal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 부서별 자료 — 관리자가 아니면 본인 부서 자료만 보고 고친다. 세정에서 연구소 업체를 볼 수도 고칠 수도 없다.
/// 기존 자료는 나노세정 기준이므로 시작할 때 나노세정으로 채운다.
/// </summary>
public class DeptScopeTests
{
    private static VendorUpsertRequest Req(string name, int? dept = null)
        => new(name, "일반", false, false, null, null, null, null, null, dept);

    /// <summary>나노세정(세정)·차세대연구소(연구) 두 부서를 만들고 Id 를 돌려준다.</summary>
    private static (int Clean, int Lab) Depts(TestDb t)
    {
        var clean = new OrgUnit { Kind = "dept", Name = "나노세정", IsActive = true };
        var lab = new OrgUnit { Kind = "dept", Name = "차세대연구소", IsActive = true };
        t.Db.OrgUnits.AddRange(clean, lab);
        t.Db.SaveChanges();
        return (clean.Id, lab.Id);
    }

    private static FakeCurrentUser Cleaner => FakeCurrentUser.In("김세정", "나노세정", "1팀");
    private static FakeCurrentUser Researcher => FakeCurrentUser.In("권연구", "차세대연구소", "연구1팀");

    [Fact]
    public async Task 다른_부서_업체는_목록에_없고_고치거나_지울_수_없다()
    {
        using var t = new TestDb();
        var (clean, lab) = Depts(t);
        var mine = new Vendor { VendorName = "원익IPS", DeptId = clean };
        var theirs = new Vendor { VendorName = "원익IPS", DeptId = lab };
        t.Db.Vendors.AddRange(mine, theirs);
        t.Db.SaveChanges();

        var svc = new VendorService(t.Db, Cleaner);
        var list = await svc.GetAllAsync(null);
        Assert.Equal(new[] { mine.Id }, list.Select(v => v.Id));
        Assert.Equal("나노세정", list[0].DeptName);

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.UpdateAsync(theirs.Id, Req("바꿈")));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.DeleteAsync(theirs.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ToggleFavoriteAsync(theirs.Id));
    }

    [Fact]
    public async Task 등록하면_본인_부서로_정해지고_같은_업체를_부서마다_따로_둘_수_있다()
    {
        using var t = new TestDb();
        var (clean, lab) = Depts(t);

        // 연구소 직원이 세정 부서를 골라도 본인 부서로 등록된다.
        var a = await new VendorService(t.Db, Researcher).CreateAsync(Req("SEMES 천안", clean));
        Assert.Equal(lab, a.DeptId);
        var b = await new VendorService(t.Db, Cleaner).CreateAsync(Req("SEMES 천안"));
        Assert.Equal(clean, b.DeptId);

        // 같은 부서 안에서는 여전히 겹치지 않게
        await Assert.ThrowsAsync<BusinessRuleException>(() => new VendorService(t.Db, Cleaner).CreateAsync(Req(" SEMES 천안 ")));
    }

    [Fact]
    public async Task 관리자는_모든_부서를_보고_부서를_골라_등록한다()
    {
        using var t = new TestDb();
        var (clean, lab) = Depts(t);
        t.Db.Vendors.AddRange(new Vendor { VendorName = "A", DeptId = clean }, new Vendor { VendorName = "B", DeptId = lab });
        t.Db.SaveChanges();

        var admin = new FakeCurrentUser { Id = 99, RealName = "관리자", IsAdmin = true, Department = "나노세정" };
        var svc = new VendorService(t.Db, admin);
        Assert.Equal(2, (await svc.GetAllAsync(null)).Count);
        // 기타세정 현황·배차표에서 고르는 목록은 관리자도 본인 부서만
        Assert.Equal(new[] { "A" }, (await svc.GetAllAsync(null, mineOnly: true)).Select(v => v.VendorName));

        Assert.Equal(lab, (await svc.CreateAsync(Req("C", lab))).DeptId);
        Assert.Equal(clean, (await svc.CreateAsync(Req("D"))).DeptId);
    }

    [Fact]
    public async Task 소속_부서가_조직도에_없으면_등록할_수_없다()
    {
        using var t = new TestDb();
        Depts(t);
        var nobody = FakeCurrentUser.In("홍길동", "없는부서", "");
        await Assert.ThrowsAsync<BusinessRuleException>(() => new VendorService(t.Db, nobody).CreateAsync(Req("X")));
    }

    [Fact]
    public async Task 이름을_바꿔도_다른_부서에_같은_이름이_남아_있으면_세정_현황은_그대로다()
    {
        using var t = new TestDb();
        var (clean, lab) = Depts(t);
        var labV = new Vendor { VendorName = "원익IPS", DeptId = lab };
        t.Db.Vendors.AddRange(labV, new Vendor { VendorName = "원익IPS", DeptId = clean });
        t.Db.Handovers.Add(new Handover { Vendor = "원익IPS", Content = "세정 건", Status = "진행" });
        t.Db.SaveChanges();

        await new VendorService(t.Db, Researcher).UpdateAsync(labV.Id, Req("원익IPS 연구"));

        using var fresh = t.NewContext();
        Assert.Equal("원익IPS", fresh.Handovers.Single().Vendor);
    }

    [Fact]
    public void 기존_자료는_시작할_때_나노세정으로_채우고_교육은_대상자_부서를_따른다()
    {
        using var t = new TestDb();
        var (clean, lab) = Depts(t);
        t.Db.Users.Add(new User { Username = "r1", RealName = "권연구", Department = "차세대연구소" });
        t.Db.Vendors.Add(new Vendor { VendorName = "옛 업체" });
        t.Db.Quotations.Add(new Quotation());
        t.Db.ProductMasters.Add(new ProductMaster { ProductName = "품목" });
        t.Db.CheckZones.Add(new CheckZone { Code = "M-OUT", Name = "출하" });
        t.Db.Reports.Add(new Report { ReportType = "weekly", Title = "주간" });
        t.Db.Reports.Add(new Report { ReportType = "meeting", Title = "미팅" });
        t.Db.EducationPlans.AddRange(new EducationPlan { MemberName = "권연구", CourseName = "A" },
                                     new EducationPlan { MemberName = "모르는사람", CourseName = "B" });
        t.Db.SaveChanges();

        Assert.True(DeptBackfill.Run(t.Db) > 0);

        using var f = t.NewContext();
        Assert.Equal(clean, f.Vendors.Single().DeptId);
        Assert.Equal(clean, f.Quotations.Single().DeptId);
        Assert.Equal(clean, f.ProductMasters.Single().DeptId);
        Assert.Equal(clean, f.CheckZones.Single().DeptId);
        Assert.Equal(clean, f.Reports.Single(r => r.ReportType == "weekly").DeptId);
        Assert.Null(f.Reports.Single(r => r.ReportType == "meeting").DeptId);   // 생산미팅은 세정 공용
        Assert.Equal(lab, f.EducationPlans.Single(e => e.CourseName == "A").DeptId);
        Assert.Equal(clean, f.EducationPlans.Single(e => e.CourseName == "B").DeptId);

        Assert.Equal(0, DeptBackfill.Run(t.Db));   // 다시 돌려도 바뀌는 것 없음
    }

    [Fact]
    public async Task 부서_목록은_내_부서를_표시하고_관리자만_있는_부서는_뺀다()
    {
        using var t = new TestDb();
        var (clean, _) = Depts(t);
        t.Db.OrgUnits.Add(new OrgUnit { Kind = "dept", Name = "시스템", IsActive = true });
        t.Db.Users.Add(new User { Username = "sys", RealName = "시스템관리", Department = "시스템", IsAdmin = true });
        t.Db.SaveChanges();

        var list = await new DeptScope(t.Db, Cleaner).ListAsync();
        Assert.DoesNotContain(list, d => d.Name == "시스템");
        Assert.Equal(clean, list.Single(d => d.Mine).Id);
    }

    [Fact]
    public async Task 부서_목록에는_부서별_자료를_켠_부서와_내_부서만_나온다()
    {
        using var t = new TestDb();
        var (clean, lab) = Depts(t);
        t.Db.OrgUnits.Add(new OrgUnit { Kind = "dept", Name = "공정기술팀", IsActive = true });
        t.Db.Vendors.Add(new Vendor { VendorName = "연구 업체", DeptId = lab });
        t.Db.SaveChanges();

        // 처음 한 번: 기본 부서(나노세정)와 자료가 있는 부서(연구소)를 켠다. 자료 없는 공정기술팀은 끈 채로.
        DeptBackfill.Run(t.Db);
        var admin = new FakeCurrentUser { Id = 99, IsAdmin = true, Department = "나노세정" };
        Assert.Equal(new[] { "나노세정", "차세대연구소" }, (await new DeptScope(t.Db, admin).ListAsync()).Select(d => d.Name).OrderBy(n => n));

        // 관리자가 연구소를 끄면 목록에서 빠진다(다시 켜지지 않는다). 연구소 사람에게는 내 부서로 계속 보인다.
        t.Db.OrgUnits.Where(o => o.Id == lab).ExecuteUpdate(u => u.SetProperty(o => o.UsesDeptData, false));
        DeptBackfill.Run(t.Db);
        Assert.Equal(new[] { "나노세정" }, (await new DeptScope(t.Db, admin).ListAsync()).Select(d => d.Name));
        Assert.Equal(lab, (await new DeptScope(t.Db, Researcher).ListAsync()).Single(d => d.Mine).Id);
        Assert.Equal(clean, (await new DeptScope(t.Db, admin).ListAsync()).Single().Id);
    }

    [Fact]
    public async Task 견적서와_단가표도_다른_부서_것은_보이지_않고_열거나_고칠_수_없다()
    {
        using var t = new TestDb();
        var (clean, lab) = Depts(t);
        var theirs = new Quotation { QuoteNo = "LAB-1", DeptId = lab };
        t.Db.Quotations.AddRange(new Quotation { QuoteNo = "CLN-1", DeptId = clean }, theirs);
        var labItem = new ProductMaster { ProductName = "연구 품목", DeptId = lab };
        t.Db.ProductMasters.AddRange(new ProductMaster { ProductName = "세정 품목", DeptId = clean }, labItem);
        t.Db.SaveChanges();

        var qs = new QuotationService(t.Db, Cleaner);
        Assert.Equal(new[] { "CLN-1" }, (await qs.GetAllAsync(null, null)).Select(x => x.QuoteNo));
        await Assert.ThrowsAsync<ForbiddenException>(() => qs.GetAsync(theirs.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => qs.DeleteAsync(theirs.Id));
        var made = await qs.CreateAsync(new QuotationUpsertRequest("N", "", "", "", "", "", null, "", "", "", "", "", "", "",
            Array.Empty<QuotationItemRequest>(), lab), "u");
        Assert.Equal(clean, made.DeptId);   // 다른 부서를 골라도 본인 부서로

        var pm = new QuotationMasterService(t.Db, Cleaner);
        Assert.Equal(new[] { "세정 품목" }, (await pm.GetProductsAsync(null)).Select(x => x.ProductName));
        await Assert.ThrowsAsync<ForbiddenException>(() => pm.UpdateProductAsync(labItem.Id,
            new ProductMasterUpsertRequest("x", "", "", 1, "", ""), "u"));
        await Assert.ThrowsAsync<ForbiddenException>(() => pm.DeleteProductAsync(labItem.Id));

        // 관리자는 모두 본다
        var admin = new FakeCurrentUser { Id = 99, IsAdmin = true, Department = "나노세정" };
        Assert.Equal(3, (await new QuotationService(t.Db, admin).GetAllAsync(null, null)).Count);
        Assert.Equal(2, (await new QuotationMasterService(t.Db, admin).GetProductsAsync(null)).Count);
    }

    [Fact]
    public async Task 체크시트는_본인_부서_구역만_보이고_다른_부서_QR_은_열_수_없다()
    {
        using var t = new TestDb();
        CheckSheetSeed.Run(t.Db);
        var (clean, lab) = Depts(t);
        DeptBackfill.Run(t.Db);   // 기존 METAL 구역 → 나노세정
        t.Db.CheckZones.Add(new CheckZone { Code = "L-LAB", Name = "연구실", Line = "LAB", DeptId = lab, IsActive = true, HasQr = true });
        t.Db.SaveChanges();

        var cleaner = new CheckSheetService(t.Db, Cleaner);
        var researcher = new CheckSheetService(t.Db, Researcher);
        var actor = new CheckActor("x", "x", false, true);

        Assert.DoesNotContain(await cleaner.GetZonesAsync(), z => z.Code == "L-LAB");
        Assert.Equal(new[] { "L-LAB" }, (await researcher.GetZonesAsync()).Select(z => z.Code));
        Assert.All(await researcher.GetItemsAsync(), i => Assert.Equal("L-LAB", i.ZoneCode));
        Assert.DoesNotContain((await researcher.GetStatusAsync(null)).Lines, l => l.Line == "METAL");

        await Assert.ThrowsAsync<ForbiddenException>(() => researcher.GetSheetAsync("M-OUT", null, null, actor));
        Assert.NotNull(await cleaner.GetSheetAsync("M-OUT", null, null, actor));

        // 관리자는 전부, 새 구역은 고른 부서로
        var admin = new CheckSheetService(t.Db, new FakeCurrentUser { Id = 99, IsAdmin = true, Department = "나노세정" });
        Assert.Contains(await admin.GetZonesAsync(), z => z.Code == "L-LAB");
        var made = await admin.SaveZoneAsync(new CheckZoneDto(0, "L-2", "연구실2", "LAB", 2, false, true, "", 1, true, "", lab));
        Assert.Equal(lab, made.DeptId);
    }

    private static ReportUpsertRequest Weekly(string title, string type = "weekly", int? dept = null)
        => new(type, "2026년 9월", title, title, "", "", "", "", "", "", "", "", "", "", "", new List<ReportBlockInput>(), null, dept);

    [Fact]
    public async Task 주간보고는_부서별이고_생산미팅은_공용이다()
    {
        using var t = new TestDb();
        var (clean, lab) = Depts(t);

        var mineW = await new ReportService(t.Db, Cleaner).CreateAsync(Weekly("세정 1주차"));
        var labW = await new ReportService(t.Db, Researcher).CreateAsync(Weekly("연구 1주차", dept: clean));   // 다른 부서를 골라도 본인 부서
        var meeting = await new ReportService(t.Db, Researcher).CreateAsync(Weekly("미팅", "meeting"));
        Assert.Equal(clean, mineW.DeptId);
        Assert.Equal(lab, labW.DeptId);
        Assert.Null(meeting.DeptId);

        var svc = new ReportService(t.Db, Cleaner);
        var titles = (await svc.GetGroupedAsync("weekly")).SelectMany(g => g.Reports).Select(r => r.Title).ToList();
        Assert.Equal(new[] { "세정 1주차" }, titles);
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.GetAsync(labW.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.UpdateAsync(labW.Id, Weekly("바꿈")));
        Assert.NotNull(await svc.GetAsync(meeting.Id));   // 생산미팅은 부서를 가리지 않는다

        // 관리자는 부서를 골라 본다(안 고르면 본인 부서)
        var admin = new ReportService(t.Db, new FakeCurrentUser { Id = 99, IsAdmin = true, Department = "나노세정" });
        Assert.Equal(new[] { "세정 1주차" }, (await admin.GetGroupedAsync("weekly")).SelectMany(g => g.Reports).Select(r => r.Title));
        Assert.Equal(new[] { "연구 1주차" }, (await admin.GetGroupedAsync("weekly", lab)).SelectMany(g => g.Reports).Select(r => r.Title));
    }

    [Fact]
    public async Task 교육은_대상자_부서를_따르고_다른_부서_인원은_등록할_수_없다()
    {
        using var t = new TestDb();
        var (clean, lab) = Depts(t);
        t.Db.Users.AddRange(new User { Username = "c1", RealName = "김세정", Department = "나노세정" },
                            new User { Username = "r1", RealName = "권연구", Department = "차세대연구소" });
        t.Db.SaveChanges();
        static EducationUpsertRequest Edu(string who) => new(who, "안전교육", null, null, "대기", 0, "", null);

        var svc = new EducationService(t.Db, Cleaner);
        Assert.Equal(clean, (await svc.CreateAsync(Edu("김세정"))).DeptId);
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.CreateAsync(Edu("권연구")));

        var labEdu = await new EducationService(t.Db, Researcher).CreateAsync(Edu("권연구"));
        Assert.Equal(lab, labEdu.DeptId);
        Assert.Equal(new[] { "김세정" }, (await svc.GetAllAsync(null, null, null)).Select(e => e.MemberName));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.DeleteAsync(labEdu.Id));

        // 관리자는 누구든 등록하고, 부서는 대상자 부서
        var admin = new EducationService(t.Db, new FakeCurrentUser { Id = 99, IsAdmin = true, Department = "나노세정" });
        Assert.Equal(lab, (await admin.CreateAsync(Edu("권연구"))).DeptId);
        Assert.Equal(3, (await admin.GetAllAsync(null, null, null)).Count);
    }

    [Fact]
    public async Task 업무_분장표는_본인_부서_인원만_보이고_고칠_수_있다()
    {
        using var t = new TestDb();
        Depts(t);
        t.Db.Users.AddRange(new User { Username = "c1", RealName = "김세정", Department = "나노세정" },
                            new User { Username = "r1", RealName = "권연구", Department = "차세대연구소" });
        t.Db.WorkMembers.AddRange(new WorkMember { Username = "c1" }, new WorkMember { Username = "r1" });
        t.Db.SaveChanges();

        var svc = new WorkAssignmentService(t.Db, Cleaner);
        Assert.Equal(new[] { "김세정" }, (await svc.GetMembersAsync(false)).Select(m => m.RealName));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.GetMemberAsync("r1"));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.SaveAccountAsync(new WorkAccountUpsertRequest("r1", "메일", "id", "pw", "")));
        Assert.NotNull(await svc.GetMemberAsync("c1"));

        var admin = new WorkAssignmentService(t.Db, new FakeCurrentUser { Id = 99, IsAdmin = true });
        Assert.Equal(2, (await admin.GetMembersAsync(false)).Count);
    }
}
