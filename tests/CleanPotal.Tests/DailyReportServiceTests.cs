using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>Daily 업무 보고 — 각 메뉴 기록을 그날(주간+야간)로 모은다.</summary>
public class DailyReportServiceTests
{
    private static readonly DateOnly D = new(2026, 9, 29);

    private static DailyReportService Svc(TestDb t)
    {
        var me = FakeCurrentUser.Admin();
        return new DailyReportService(t.Db, new ScheduleService(t.Db, new HolidayService(), me), new CheckSheetService(t.Db, me),
            new HandoverService(t.Db, me), new ScheduleBoardService(t.Db), new WorkLogService(t.Db));
    }

    private static async Task SeedAsync(TestDb t)
    {
        t.Db.ProductionMeetings.Add(new ProductionMeeting { Title = "9/29", MeetingDate = D, DayContent = "주간 특이사항 없음", NightContent = "야간 MDC03 점검" });
        t.Db.ProductionMeetings.Add(new ProductionMeeting { Title = "9/28", MeetingDate = D.AddDays(-1), DayContent = "전날" });
        t.Db.Handovers.Add(new Handover { Vendor = "A업체", Content = "척 세정", InDate = D, OutDate = D.AddDays(1), Status = "진행" });
        t.Db.Handovers.Add(new Handover { Vendor = "B업체", Content = "링", InDate = D.AddDays(-3), OutDate = D, Status = "완료" });
        t.Db.ProdReqs.Add(new ProdReq { RequestDate = D, Category = "설비", RequestDetail = "배관 누수", Status = "진행" });
        t.Db.ProdReqs.Add(new ProdReq { RequestDate = D.AddDays(-2), ActionDate = D, RequestDetail = "조명", Status = "완료", ActionDetail = "교체" });
        var eq = new ScheduleEquipment { Name = "MDC01", GroupName = "MDC", Slot = 0, OrderIndex = 0, IsActive = true };
        t.Db.ScheduleEquipments.Add(eq);
        t.Db.ScheduleEquipments.Add(new ScheduleEquipment { Name = "MDC02", GroupName = "MDC", Slot = 1, OrderIndex = 1, IsActive = true, IsIdle = true });
        // 07:00 기준 30분 → 07:30 시작, 60분 → 08:30 끝 / 23:30 시작(990분) 90분 → 다음 날 01:00 끝
        t.Db.ScheduleBlocks.Add(new ScheduleBlock { BoardDate = "2026-09-29", EquipmentIndex = 0, StartMinute = 30, S2Minutes = 20, HFMinutes = 20, DIMinutes = 20, RecipeText = "POLY@1" });
        t.Db.ScheduleBlocks.Add(new ScheduleBlock { BoardDate = "2026-09-29", EquipmentIndex = 0, StartMinute = 990, S2Minutes = 30, HFMinutes = 30, DIMinutes = 30, RecipeText = "TEOS" });
        await t.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task 그날_기록만_섹션별로_모은다()
    {
        using var t = new TestDb();
        await SeedAsync(t);
        var r = await Svc(t).GetAsync(D, DailyReportAccess.All);

        var m = Assert.Single(r.Meetings!);
        Assert.Equal("야간 MDC03 점검", m.NightContent);

        Assert.Equal("A업체", Assert.Single(r.Handover!.In).Vendor);
        Assert.Equal("B업체", Assert.Single(r.Handover.Out).Vendor);      // 완료된 것도 그날 출고면 싣는다

        Assert.Equal("배관 누수", Assert.Single(r.ProdReq!.New).RequestDetail);
        Assert.Equal("교체", Assert.Single(r.ProdReq.Done).ActionDetail);

        Assert.Equal(2, r.Board!.Equipment.Count);                        // 블록이 없는 설비도 싣는다(대기·유휴)
        var b = r.Board.Equipment[0];
        Assert.Equal("MDC01", b.Name);
        Assert.Empty(r.Board.Equipment[1].Blocks);
        Assert.True(r.Board.Equipment[1].IsIdle);
        Assert.Equal(2, r.Board.TotalEquipment);
        Assert.Equal(1, r.Board.IdleEquipment);
        Assert.Equal(("07:30", "08:30", false, "POLY"), (b.Blocks[0].Start, b.Blocks[0].End, b.Blocks[0].NextDay, b.Blocks[0].Recipe));
        Assert.Equal(("23:30", "01:00", true), (b.Blocks[1].Start, b.Blocks[1].End, b.Blocks[1].NextDay));

        Assert.NotNull(r.Chemical);
        Assert.Null(r.Chemical!.Bake);                                    // BAKE 는 따로
        Assert.NotNull(r.Bake);
    }

    [Fact]
    public async Task 볼_수_없는_메뉴의_섹션은_비운다()
    {
        using var t = new TestDb();
        await SeedAsync(t);
        var none = new DailyReportAccess(false, false, false, false, false, false, false, false);
        var r = await Svc(t).GetAsync(D, none);
        Assert.Null(r.Meetings);
        Assert.Null(r.Handover);
        Assert.Null(r.ProdReq);
        Assert.Null(r.Board);
        Assert.Null(r.Bake);
        Assert.Null(r.Waste);
        Assert.NotNull(r.Chemical);                                       // 업무보고 자체(약액 교체)는 보인다
    }

    [Fact]
    public async Task 근무는_교대팀과_주간팀을_나누고_근속을_붙인다()
    {
        using var t = new TestDb();
        t.Db.OrgUnits.Add(new OrgUnit { Kind = "team", Name = "1팀", Parent = "나노세정", IsProduction = true, ShiftGroup = 1 });
        t.Db.OrgUnits.Add(new OrgUnit { Kind = "team", Name = "주간팀", Parent = "나노세정", IsProduction = true });
        t.Db.Users.Add(new User { Username = "a", RealName = "박주언", TeamName = "1팀", HireDate = "2023-03-01" });
        t.Db.Users.Add(new User { Username = "b", RealName = "김단비", TeamName = "주간팀", HireDate = "2026-06-15" });
        await t.Db.SaveChangesAsync();

        var r = await Svc(t).GetAsync(D, DailyReportAccess.All);   // 2026-09-29 화요일

        var one = r.Crew!.Single(c => c.Team == "1팀");
        Assert.True(one.HasShift);
        Assert.Contains(one.Shift, new[] { "주간", "야간" });
        Assert.Equal(42, Assert.Single(one.Members).TenureMonths);   // 2023-03 ~ 2026-09

        var day = r.Crew!.Single(c => c.Team == "주간팀");
        Assert.False(day.HasShift);
        Assert.Equal("주간", day.Shift);                            // 평일은 도장이 없어도 주간 근무
        Assert.Equal(new[] { "김단비" }, day.Day);
        Assert.Equal(3, day.Members.Single().TenureMonths);
    }

    [Fact]
    public async Task 주간팀은_주말에는_도장이_있을_때만_근무로_본다()
    {
        using var t = new TestDb();
        t.Db.OrgUnits.Add(new OrgUnit { Kind = "team", Name = "주간팀", Parent = "나노세정", IsProduction = true });
        t.Db.Users.Add(new User { Username = "b", RealName = "김단비", TeamName = "주간팀" });
        await t.Db.SaveChangesAsync();

        var r = await Svc(t).GetAsync(new DateOnly(2026, 9, 27), DailyReportAccess.All);   // 일요일
        var day = r.Crew!.Single(c => c.Team == "주간팀");
        Assert.Empty(day.Day);
        Assert.Equal("", day.Shift);
        Assert.Null(day.Members.Single().TenureMonths);                // 입사일 없음
    }

    [Fact]
    public async Task 주간_야간_팀은_도장의_옛_팀이름이_아니라_조직도_이름으로_낸다()
    {
        // WPF 는 근무 도장에 옛 이름('김팀')을 계속 적는다 — 스케줄 보드에 '주간 김팀' 으로 나오던 원인
        using var t = new TestDb();
        t.Db.OrgUnits.Add(new OrgUnit { Kind = "team", Name = "1팀", Parent = "나노세정", IsProduction = true, ShiftGroup = 1 });
        t.Db.OrgUnits.Add(new OrgUnit { Kind = "team", Name = "2팀", Parent = "나노세정", IsProduction = true, ShiftGroup = 2 });
        t.Db.Users.Add(new User { Username = "a", RealName = "박주언", TeamName = "1팀" });
        t.Db.Users.Add(new User { Username = "b", RealName = "김단비", TeamName = "2팀" });
        t.Db.ShiftSchedules.Add(new ShiftSchedule { MemberName = "박주언", TargetDate = D, ShiftType = "야간", TeamGroup = "김팀" });
        t.Db.ShiftSchedules.Add(new ShiftSchedule { MemberName = "김단비", TargetDate = D, ShiftType = "주간", TeamGroup = "장팀" });
        await t.Db.SaveChangesAsync();

        var st = await new ScheduleService(t.Db, new HolidayService(), FakeCurrentUser.Admin()).GetShiftTeamsAsync(D);

        Assert.Equal(new[] { "2팀" }, st.DayTeams);     // 도장대로(예측과 달라도 도장이 우선)
        Assert.Equal(new[] { "1팀" }, st.NightTeams);
    }

    [Fact]
    public async Task 체크시트_설비는_설비별_진행과_그날_NG_를_싣는다()
    {
        using var t = new TestDb();
        CleanPotal.Infrastructure.Data.EquipmentCatalog.SeedDefaults(t.Db);
        CleanPotal.Infrastructure.Data.EqCheckSeed.Run(t.Db);
        var u = t.Db.EqCheckUnits.Single(x => x.Code == "NDC02");
        var item = t.Db.EqCheckItems.First(i => i.TemplateId == u.TemplateId && i.Cycle == EqCycles.Daily);
        t.Db.EqCheckResults.Add(new EqCheckResult
        {
            RecordId = 0, UnitCode = "NDC02", Cycle = EqCycles.Daily, PeriodKey = "2026-09-29", ItemId = item.Id, Name = item.Name,
            Value = "X", Judge = "NG", NgStatus = "OPEN", CheckedAt = D.ToDateTime(new TimeOnly(10, 0)), CheckedByName = "홍길동",
        });
        await t.Db.SaveChangesAsync();

        var r = await Svc(t).GetAsync(D, DailyReportAccess.All);
        Assert.Equal(35, r.EqCheck!.Status.Rows.Count);
        Assert.Equal("NDC02", Assert.Single(r.EqCheck.Ngs).UnitCode);
        Assert.Null((await Svc(t).GetAsync(D, DailyReportAccess.All with { EqCheck = false })).EqCheck);   // 권한 없으면 빼고 보낸다
    }

    [Fact]
    public async Task 섹션_순서는_관리자가_저장하고_보고서에_실린다()
    {
        using var t = new TestDb();
        var svc = Svc(t);
        Assert.Empty((await svc.GetAsync(D, DailyReportAccess.All)).Order!);   // 정한 적 없으면 기본 순서(빈 목록)

        var saved = await svc.SaveOrderAsync(new[] { "Board", "bake", "crew", "bake", "x-y", "" });
        Assert.Equal(new[] { "board", "bake", "crew" }, saved);              // 소문자로, 겹친 것·이상한 키는 버린다
        Assert.Equal(saved, (await Svc(t).GetAsync(D, DailyReportAccess.All)).Order);

        Assert.Empty(await svc.SaveOrderAsync(Array.Empty<string>()));       // 비우면 기본 순서로 되돌린다
        Assert.Empty(await Svc(t).GetOrderAsync());
    }

    [Fact]
    public async Task 근무_인원에_직위가_붙고_없으면_직급()
    {
        using var t = new TestDb();
        t.Db.Users.Add(new User { Username = "a1", RealName = "김팀장", TeamName = "1팀", JobTitle = "세정팀장", Rank = "과장" });
        t.Db.Users.Add(new User { Username = "a2", RealName = "이사원", TeamName = "1팀", Rank = "사원" });
        await t.Db.SaveChangesAsync();
        var r = await Svc(t).GetAsync(D, DailyReportAccess.All);
        var members = r.Crew!.SelectMany(c => c.Members).ToList();
        Assert.Equal("세정팀장", members.Single(m => m.Name == "김팀장").Title);
        Assert.Equal("사원", members.Single(m => m.Name == "이사원").Title);
    }

    [Fact]
    public async Task 출하_실적은_날짜마다_한_장이고_다시_올리면_덮어쓴다()
    {
        using var t = new TestDb();
        var svc = Svc(t);
        Assert.Null((await svc.GetAsync(D, DailyReportAccess.All)).Shipment);

        var cols = new[] { "OUTER", "INNER", "출하금액 (당일)" };
        await svc.SaveShipmentAsync(new DailyShipmentSaveRequest(D, cols,
            new[] { new DailyShipmentRowDto("Memory(화성)", new decimal?[] { 2, 3, 6840000 }), new DailyShipmentRowDto("  ", new decimal?[] { 1 }) },
            "INFORM.xlsx"), "박주언");
        var s = (await Svc(t).GetAsync(D, DailyReportAccess.All)).Shipment!;
        Assert.Equal(cols, s.Columns);
        Assert.Equal(6840000m, Assert.Single(s.Rows).Values[2]);   // 고객 이름이 빈 줄은 버린다
        Assert.Equal("박주언", s.UploadedBy);

        await svc.SaveShipmentAsync(new DailyShipmentSaveRequest(D, cols,
            new[] { new DailyShipmentRowDto("합 계", new decimal?[] { 14, 18, 52755900 }) }, "INFORM2.xlsx"), "홍길동");
        Assert.Equal(1, t.Db.DailyShipments.Count());
        Assert.Equal("합 계", Assert.Single((await svc.GetShipmentAsync(D))!.Rows).Customer);
        await Assert.ThrowsAsync<CleanPotal.Core.BusinessRuleException>(() =>
            svc.SaveShipmentAsync(new DailyShipmentSaveRequest(D, Array.Empty<string>(), Array.Empty<DailyShipmentRowDto>(), null), "x"));
    }

    [Fact]
    public void 주_시작은_월요일()
    {
        Assert.Equal(new DateOnly(2026, 9, 28), DailyReportService.WeekStart(new DateOnly(2026, 9, 28)));   // 월
        Assert.Equal(new DateOnly(2026, 9, 28), DailyReportService.WeekStart(new DateOnly(2026, 10, 1)));   // 목
        Assert.Equal(new DateOnly(2026, 9, 28), DailyReportService.WeekStart(new DateOnly(2026, 10, 4)));   // 일
    }
}
