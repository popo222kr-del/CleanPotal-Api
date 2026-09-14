using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 근무표 도장/조회 — 잘못된 입력이 500이 아니라 400(BusinessRuleException)으로 나가는지,
/// 그리고 도장 결과가 실제로 저장되는지(스케줄보드 미반영 장애의 회귀)를 확인한다.
/// </summary>
public class ScheduleServiceTests
{
    private static readonly DateOnly Start = new(2026, 6, 10);

    private static ScheduleService Service(TestDb t) => new(t.Db, new HolidayService());

    private static async Task SeedMembers(TestDb t, params string[] names)
    {
        foreach (var n in names)
            t.Db.Users.Add(new User { Username = n, RealName = n, TeamName = "김팀", PasswordHash = "x" });
        await t.Db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(2026, 13)]
    [InlineData(2026, 0)]
    [InlineData(1999, 6)]
    [InlineData(2200, 6)]
    public async Task 잘못된_연월은_업무규칙_예외로_막힌다(int year, int month)
    {
        using var t = new TestDb();
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service(t).GetRosterAsync(year, month, "전체", false));
    }

    [Fact]
    public async Task 대상자가_비어_있으면_업무규칙_예외()
    {
        using var t = new TestDb();
        var req = new StampShiftRequest(Array.Empty<string>(), Start, "주간");
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service(t).StampAsync(req, "tester"));
    }

    [Fact]
    public async Task 허용되지_않은_근무표시는_업무규칙_예외()
    {
        using var t = new TestDb();
        await SeedMembers(t, "박주언");
        var req = new StampShiftRequest(new[] { "박주언" }, Start, "<script>");
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service(t).StampAsync(req, "tester"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public async Task 일수가_범위를_벗어나면_업무규칙_예외(int days)
    {
        using var t = new TestDb();
        await SeedMembers(t, "박주언");
        var req = new StampShiftRequest(new[] { "박주언" }, Start, "주간", days);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service(t).StampAsync(req, "tester"));
    }

    [Fact]
    public async Task 직원_목록에_없는_이름은_업무규칙_예외()
    {
        using var t = new TestDb();
        await SeedMembers(t, "박주언");
        var req = new StampShiftRequest(new[] { "없는사람" }, Start, "주간");
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service(t).StampAsync(req, "tester"));
    }

    [Fact]
    public async Task 도장은_여러명_여러날에_한번에_저장된다()
    {
        using var t = new TestDb();
        await SeedMembers(t, "박주언", "홍길동");

        var req = new StampShiftRequest(new[] { "박주언", "홍길동" }, Start, "야간", 3);
        var cells = await Service(t).StampAsync(req, "tester");

        Assert.Equal(6, cells.Count);   // 2명 × 3일
        using var fresh = t.NewContext();
        Assert.Equal(6, fresh.ShiftSchedules.Count());
        Assert.All(fresh.ShiftSchedules.ToList(), s =>
        {
            Assert.Equal("야간", s.ShiftType);
            Assert.Equal("김팀", s.TeamGroup);   // 소속팀이 함께 기록된다
        });
    }

    [Fact]
    public async Task 같은_칸에_다시_찍으면_행이_늘지_않고_갱신된다()
    {
        using var t = new TestDb();
        await SeedMembers(t, "박주언");
        var svc = Service(t);

        await svc.StampAsync(new StampShiftRequest(new[] { "박주언" }, Start, "주간"), "tester");
        await svc.StampAsync(new StampShiftRequest(new[] { "박주언" }, Start, "야간"), "tester");

        using var fresh = t.NewContext();
        var row = Assert.Single(fresh.ShiftSchedules.ToList());
        Assert.Equal("야간", row.ShiftType);
    }

    [Fact]
    public async Task 중복된_대상자는_한_번만_처리된다()
    {
        using var t = new TestDb();
        await SeedMembers(t, "박주언");

        var req = new StampShiftRequest(new[] { "박주언", "박주언", " 박주언 " }, Start, "주간");
        var cells = await Service(t).StampAsync(req, "tester");

        Assert.Single(cells);
        using var fresh = t.NewContext();
        Assert.Single(fresh.ShiftSchedules.ToList());
    }

    [Fact]
    public async Task 근무표_조회는_팀별로_인원과_날짜수를_돌려준다()
    {
        using var t = new TestDb();
        await SeedMembers(t, "박주언", "홍길동");
        await Service(t).StampAsync(new StampShiftRequest(new[] { "박주언" }, Start, "주간", 2), "tester");

        var roster = await Service(t).GetRosterAsync(2026, 6, "전체", false);

        Assert.Equal(30, roster.Days.Count);            // 2026년 6월 = 30일
        var team = Assert.Single(roster.Teams);
        Assert.Equal(2, team.Members.Count);
        var me = team.Members.Single(m => m.Name == "박주언");
        Assert.Equal(30, me.Cells.Count);
        Assert.Equal(2, me.TotalWorkDays);              // 주간 2일 = 근무 2일
    }
}
