using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 교육 일정 → 근무표 '교육' 자동 반영.
/// 예전에는 상태만 바꿔도 교육일이 사라지고, 연차를 교육으로 덮고, 지울 때 남의 교육 표시까지 지웠다.
/// </summary>
public class EducationRosterSyncTests
{
    private static readonly DateOnly D1 = new(2026, 10, 12);   // 월

    private static EducationUpsertRequest Req(string member, DateOnly start, DateOnly end, string status = "대기", string course = "안전교육")
        => new(member, course, start, end, status, 0, "집합", null);

    private static async Task<Dictionary<DateOnly, string>> Roster(TestDb t, string member)
        => await t.Db.ShiftSchedules.AsNoTracking().Where(s => s.MemberName == member)
            .ToDictionaryAsync(s => s.TargetDate, s => s.ShiftType);

    [Fact]
    public async Task 상태만_바꿔도_교육일이_남는다()
    {
        using var t = new TestDb();
        var svc = new EducationService(t.Db);
        var e = await svc.CreateAsync(Req("홍길동", D1, D1.AddDays(2)));

        await svc.UpdateAsync(e.Id, Req("홍길동", D1, D1.AddDays(2), status: "완료"));

        Assert.Equal(3, (await Roster(t, "홍길동")).Count(kv => kv.Value == "교육"));
    }

    [Fact]
    public async Task 이미_적힌_근무는_덮지_않고_지워도_그대로다()
    {
        using var t = new TestDb();
        t.Db.ShiftSchedules.Add(new ShiftSchedule { MemberName = "홍길동", TargetDate = D1.AddDays(1), ShiftType = "연차", CreatorName = "관리자" });
        await t.Db.SaveChangesAsync();
        var svc = new EducationService(t.Db);

        var e = await svc.CreateAsync(Req("홍길동", D1, D1.AddDays(2)));
        Assert.Equal("연차", (await Roster(t, "홍길동"))[D1.AddDays(1)]);

        await svc.DeleteAsync(e.Id);
        var roster = await Roster(t, "홍길동");
        Assert.Equal("연차", Assert.Single(roster).Value);
    }

    [Fact]
    public async Task 지울_때_직접_찍은_교육_도장은_남긴다()
    {
        using var t = new TestDb();
        t.Db.ShiftSchedules.Add(new ShiftSchedule { MemberName = "홍길동", TargetDate = D1.AddDays(5), ShiftType = "교육", CreatorName = "관리자" });
        await t.Db.SaveChangesAsync();
        var svc = new EducationService(t.Db);

        var e = await svc.CreateAsync(Req("홍길동", D1, D1.AddDays(6)));
        await svc.DeleteAsync(e.Id);

        Assert.Equal(D1.AddDays(5), Assert.Single(await Roster(t, "홍길동")).Key);
    }

    [Fact]
    public async Task 겹치는_교육을_지우면_남은_교육이_그날을_다시_채운다()
    {
        using var t = new TestDb();
        var svc = new EducationService(t.Db);
        var a = await svc.CreateAsync(Req("홍길동", D1, D1.AddDays(2), course: "A"));
        await svc.CreateAsync(Req("홍길동", D1.AddDays(2), D1.AddDays(4), course: "B"));   // 3일째는 A 가 이미 채움

        await svc.DeleteAsync(a.Id);

        var roster = await Roster(t, "홍길동");
        Assert.Equal(new[] { D1.AddDays(2), D1.AddDays(3), D1.AddDays(4) }, roster.Keys.OrderBy(d => d));
    }

    [Fact]
    public async Task 취소하면_근무표에서_빠지고_기간을_줄이면_남는_날만_남는다()
    {
        using var t = new TestDb();
        var svc = new EducationService(t.Db);
        var e = await svc.CreateAsync(Req("홍길동", D1, D1.AddDays(4)));

        await svc.UpdateAsync(e.Id, Req("홍길동", D1, D1.AddDays(1)));
        Assert.Equal(2, (await Roster(t, "홍길동")).Count);

        await svc.UpdateAsync(e.Id, Req("홍길동", D1, D1.AddDays(1), status: "취소"));
        Assert.Empty(await Roster(t, "홍길동"));
    }

    [Fact]
    public async Task 사람을_바꾸면_근무표도_옮겨_가고_이름_공백은_지운다()
    {
        using var t = new TestDb();
        var svc = new EducationService(t.Db);
        var e = await svc.CreateAsync(Req(" 홍길동 ", D1, D1));
        Assert.Single(await Roster(t, "홍길동"));

        await svc.UpdateAsync(e.Id, Req("김단비", D1, D1));

        Assert.Empty(await Roster(t, "홍길동"));
        Assert.Single(await Roster(t, "김단비"));
    }

    [Fact]
    public async Task 예전_방식으로_만든_교육일도_이_교육_것으로_알아본다()
    {
        using var t = new TestDb();
        var plan = new EducationPlan { MemberName = "홍길동", CourseName = "예전 교육", StartDate = D1, EndDate = D1.AddDays(1), Status = "대기" };
        t.Db.EducationPlans.Add(plan);
        t.Db.ShiftSchedules.Add(new ShiftSchedule { MemberName = "홍길동", TargetDate = D1, ShiftType = "교육", CreatorName = "education" });
        t.Db.ShiftSchedules.Add(new ShiftSchedule { MemberName = "홍길동", TargetDate = D1.AddDays(1), ShiftType = "교육", CreatorName = "education" });
        await t.Db.SaveChangesAsync();
        var svc = new EducationService(t.Db);

        await svc.DeleteAsync(plan.Id);

        Assert.Empty(await Roster(t, "홍길동"));
    }

    [Fact]
    public async Task 근무표에서_비운_날도_빈_날로_보고_채운다()
    {
        using var t = new TestDb();
        // 근무표의 '비우기' 는 줄을 지우지 않고 "비우기" 로 남긴다(화면에는 빈 칸).
        t.Db.ShiftSchedules.Add(new ShiftSchedule { MemberName = "홍길동", TargetDate = D1, ShiftType = "비우기", CreatorName = "관리자" });
        await t.Db.SaveChangesAsync();
        var svc = new EducationService(t.Db);

        var e = await svc.CreateAsync(Req("홍길동", D1, D1.AddDays(1)));
        var roster = await Roster(t, "홍길동");
        Assert.Equal("교육", roster[D1]);
        Assert.Equal("교육", roster[D1.AddDays(1)]);

        await svc.DeleteAsync(e.Id);
        Assert.Empty(await Roster(t, "홍길동"));
    }
}
