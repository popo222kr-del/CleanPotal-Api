using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 세정 현황은 업체를 이름으로 찾아 주간세정/기타세정으로 나눈다 —
/// 업체 이름을 바꾸면 현황의 업체명도 같이 바뀌어야 주간세정 항목이 기타세정으로 떨어지지 않는다.
/// </summary>
public class VendorRenameTests
{
    private static VendorUpsertRequest Req(string name, bool weekly) => new(name, "일반", weekly, false, null, null, null, null, null);

    [Fact]
    public async Task 이름을_바꾸면_세정_현황의_업체명도_따라간다()
    {
        using var t = new TestDb();
        var v = new Vendor { VendorName = "원익IPS", IsWeekly = true };
        t.Db.Vendors.Add(v);
        t.Db.Handovers.Add(new Handover { Vendor = "원익IPS", Content = "주간 세정", Status = "진행" });
        t.Db.Handovers.Add(new Handover { Vendor = "다른업체", Content = "x", Status = "진행" });
        await t.Db.SaveChangesAsync();

        await new VendorService(t.Db).UpdateAsync(v.Id, Req("원익IPS 평택", true));

        using var fresh = t.NewContext();
        var h = fresh.Handovers.Single(x => x.Content == "주간 세정");
        Assert.Equal("원익IPS 평택", h.Vendor);
        Assert.Equal(1, h.RowVersion);   // 열어 둔 사람이 저장하면 "먼저 고쳤다" 로 알게
        Assert.Equal("다른업체", fresh.Handovers.Single(x => x.Content == "x").Vendor);
    }

    [Fact]
    public async Task 같은_이름의_업체를_또_만들_수_없다()
    {
        using var t = new TestDb();
        t.Db.Vendors.Add(new Vendor { VendorName = "SEMES 천안" });
        await t.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() => new VendorService(t.Db).CreateAsync(Req(" SEMES 천안 ", false)));
    }
}
