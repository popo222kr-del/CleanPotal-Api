using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>두 사람이 같은 것을 열어 두고 저장할 때 — BROKEN 기록, 배차표 행, 자재 일정(하루).</summary>
public class MoreConcurrencyTests
{
    private static BrokenUpsertRequest Broken(string desc, int? ver) => new(
        null, "L1", "제품", "PARTS", "SN", "생산", "홍길동", "사원", "1년", "세정", desc, "접수", true, false,
        "[]", "[]", "[]", "[]", ver);

    [Fact]
    public async Task BROKEN_기록은_받아간_버전이_다르면_저장을_막는다()
    {
        using var t = new TestDb();
        var svc = new BrokenService(t.Db);
        var created = await svc.CreateAsync(Broken("처음", null));
        var first = await svc.UpdateAsync(created.Id, Broken("A 가 고침", created.RowVersion));
        Assert.Equal(created.RowVersion + 1, first!.RowVersion);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => svc.UpdateAsync(created.Id, Broken("B 가 옛 화면으로", created.RowVersion)));
    }

    private static DispatchRowRequest Row(int id, string vendor, string note, int? ver) =>
        new(id, vendor, "-", "", "김", "010", "주소", note, ver);

    [Fact]
    public async Task 배차표_같은_행을_옛_버전으로_고치면_막고_안_고친_행은_그냥_둔다()
    {
        using var t = new TestDb();
        // 요청마다 새 컨텍스트 — 실제 서버처럼(실패한 요청이 고친 값이 다음 요청에 남지 않게)
        DispatchService Svc() => new(t.NewContext());
        var svc = new DispatchService(t.Db);
        var day = new DateOnly(2026, 9, 26);
        var saved = await svc.SaveDayAsync(day, [Row(0, "A업체", "", null), Row(0, "B업체", "", null)]);
        var a = saved.Single(x => x.VendorName == "A업체");
        var b = saved.Single(x => x.VendorName == "B업체");

        // 첫 사람이 A 를 고친다
        await Svc().SaveDayAsync(day, [Row(a.Id, "A업체", "첫 사람", a.RowVersion), Row(b.Id, "B업체", "", b.RowVersion)], [a.Id, b.Id]);

        // 옛 화면의 두 번째 사람이 A 를 다르게 고치면 막는다
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            Svc().SaveDayAsync(day, [Row(a.Id, "A업체", "둘째 사람", a.RowVersion), Row(b.Id, "B업체", "", b.RowVersion)], [a.Id, b.Id]));

        // 옛 화면이라도 A 를 손대지 않고(첫 사람 값 그대로) B 만 고치면 저장된다
        var ok = await Svc().SaveDayAsync(day, [Row(a.Id, "A업체", "첫 사람", a.RowVersion), Row(b.Id, "B업체", "둘째 사람", b.RowVersion)], [a.Id, b.Id]);
        Assert.Equal("둘째 사람", ok.Single(x => x.Id == b.Id).Note);
        Assert.Equal("첫 사람", ok.Single(x => x.Id == a.Id).Note);
    }

    [Fact]
    public async Task 자재_일정은_하루_단위_버전이_다르면_저장을_막는다()
    {
        using var t = new TestDb();
        t.Db.MaterialRosterMembers.Add(new MaterialRosterMember { Name = "김기사" });
        await t.Db.SaveChangesAsync();
        var svc = new MaterialService(t.Db);
        var day = new DateOnly(2026, 9, 26);
        var first = await svc.GetDayAsync(day);
        var row = new MaterialRowInput("김기사", new MaterialCellInput("A업체", []), new MaterialCellInput("", []));

        var saved = await svc.SaveDayAsync(day, new MaterialSaveRequest([row], "", "", first.Version));
        Assert.Equal(first.Version + 1, saved.Version);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            svc.SaveDayAsync(day, new MaterialSaveRequest([row], "옛 화면", "", first.Version)));
    }
}
