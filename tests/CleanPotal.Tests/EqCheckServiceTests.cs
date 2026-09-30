using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using CleanPotal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>체크시트 (설비) — 설비 점검표 AQ-C-13 Rev.7.</summary>
public class EqCheckServiceTests
{
    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeZoneInfo.Local.GetUtcOffset(now));
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
    }

    // 2026-10-01(목) 10시 — 주간 기한 10/2(금), 월간 기한 10/2(첫째 금)
    private static readonly DateTime Now = new(2026, 10, 1, 10, 0, 0);
    private static readonly EqCheckActor Worker = new("w1", "생산", false, false, "나노세정", "1팀");
    private static readonly EqCheckActor Facility = new("f1", "설비", false, false, "설비팀", "");

    private static EqCheckService Svc(TestDb t, DateTime? now = null)
    {
        EquipmentCatalog.SeedDefaults(t.Db);
        EqCheckSeed.Run(t.Db);
        return new EqCheckService(t.Db, clock: new FixedClock(now ?? Now));
    }

    private static EqCheckItem Item(TestDb t, string unit, string cycle, string name, string point = "")
    {
        var u = t.Db.EqCheckUnits.Single(x => x.Code == unit);
        return t.Db.EqCheckItems.First(i => i.TemplateId == u.TemplateId && i.Cycle == cycle && i.Name == name && i.Point == point);
    }

    [Fact]
    public void 기간_키와_기한()
    {
        var d = new DateOnly(2026, 10, 1);
        Assert.Equal("2026-10-01", EqCheckService.PeriodKeyOf(EqCycles.Daily, d));
        Assert.Equal("2026-10-02", EqCheckService.PeriodKeyOf(EqCycles.Weekly, d));
        Assert.Equal("2026-10-02", EqCheckService.PeriodKeyOf(EqCycles.Weekly, new DateOnly(2026, 10, 4)));   // 일요일도 그 주
        Assert.Equal("2026-10", EqCheckService.PeriodKeyOf(EqCycles.Monthly, d));
        Assert.Equal(new DateOnly(2026, 11, 6), EqCheckService.FirstFriday(2026, 11));
        // 월간은 그 달 안에 — 기한은 말일, 그 전엔 지연이 아니다
        Assert.Equal(new DateOnly(2026, 10, 31), EqCheckService.Period(EqCycles.Monthly, "2026-10").Due);
        Assert.Equal("todo", EqCheckService.State(EqCycles.Monthly, "2026-10", new DateOnly(2026, 10, 20), 0, 10));
        Assert.Equal("late", EqCheckService.State(EqCycles.Monthly, "2026-09", new DateOnly(2026, 10, 1), 3, 10));
        Assert.Throws<BusinessRuleException>(() => EqCheckService.Period(EqCycles.Weekly, "2026-10-01"));   // 금요일이 아니다
    }

    [Fact]
    public void 기본_양식은_점검표_Rev7_대로_채운다()
    {
        using var t = new TestDb();
        Svc(t);
        Assert.Equal(13, t.Db.EqCheckTemplates.Count());
        Assert.Equal(35, t.Db.EqCheckUnits.Count());
        var bake = t.Db.EqCheckUnits.Single(u => u.Code == "MBO01");
        Assert.Equal(2, t.Db.EqCheckItems.Count(i => i.TemplateId == bake.TemplateId && i.Name == "Air Regulator"));   // L / R
        Assert.Contains(t.Db.EqCheckUnits, u => u.Code == "MSC02");                                                  // 점검표 MSC01 → 설비 목록 MSC02
        Assert.DoesNotContain(t.Db.EqCheckItems, i => i.Name.Contains("Interlcok"));
    }

    [Fact]
    public async Task 판정_O는_정상_세모와_X는_NG_보기는_첫_보기만_정상()
    {
        using var t = new TestDb();
        var svc = Svc(t);
        var daily = Item(t, "NDC02", EqCycles.Daily, "Bath 수위");
        var r = await svc.SaveResultAsync("NDC02", daily.Id, new EqCheckSaveRequest(EqCycles.Daily, "2026-10-01", "△", null, false, null, ViaQr: true), Worker);
        Assert.Equal(("NG", "OPEN"), (r!.Judge, r.NgStatus));
        r = await svc.SaveResultAsync("NDC02", daily.Id, new EqCheckSaveRequest(EqCycles.Daily, "2026-10-01", "O", null, false, null, ViaQr: true), Worker);
        Assert.Equal(("OK", ""), (r!.Judge, r.NgStatus));

        var gun = Item(t, "NDC02", EqCycles.Weekly, "DI.W GUN");
        r = await svc.SaveResultAsync("NDC02", gun.Id, new EqCheckSaveRequest(EqCycles.Weekly, "2026-10-02", "비정상", null, false, null, ViaQr: true), Worker);
        Assert.Equal("NG", r!.Judge);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            svc.SaveResultAsync("NDC02", gun.Id, new EqCheckSaveRequest(EqCycles.Weekly, "2026-10-02", "몰라", null, false, null, ViaQr: true), Worker));
    }

    [Fact]
    public async Task 수치는_기준_범위로_여러_칸은_편차로_가동_중_항목은_비가동이면_정상()
    {
        using var t = new TestDb();
        var svc = Svc(t);
        var air = Item(t, "NDC02", EqCycles.Weekly, "Air Regulator");                       // 0.5±0.1
        var r = await svc.SaveResultAsync("NDC02", air.Id, new EqCheckSaveRequest(EqCycles.Weekly, "2026-10-02", null, new() { [""] = 0.72m }, false, null, ViaQr: true), Worker);
        Assert.Equal(("NG", "0.72 Mpa"), (r!.Judge, r.ValueText));

        var bubble = Item(t, "NDC02", EqCycles.Weekly, "Bath CDA Bubble", "#1 DI Bath");   // 가동: 30±5
        r = await svc.SaveResultAsync("NDC02", bubble.Id, new EqCheckSaveRequest(EqCycles.Weekly, "2026-10-02", null, null, true, null, ViaQr: true), Worker);
        Assert.Equal(("OK", "비가동"), (r!.Judge, r.ValueText));

        var tc = Item(t, "MBO01", EqCycles.Weekly, "HEATER 정합률");                         // Set|Real, ±2
        r = await svc.SaveResultAsync("MBO01", tc.Id, new EqCheckSaveRequest(EqCycles.Weekly, "2026-10-02", null, new() { ["Set"] = 300m }, false, null, ViaQr: true), Worker);
        Assert.Equal("", r!.Judge);                                                        // 칸을 다 채우기 전엔 판정하지 않는다
        r = await svc.SaveResultAsync("MBO01", tc.Id, new EqCheckSaveRequest(EqCycles.Weekly, "2026-10-02", null, new() { ["Set"] = 300m, ["Real"] = 303m }, false, null, ViaQr: true), Worker);
        Assert.Equal(("NG", "Set 300 / Real 303"), (r!.Judge, r.ValueText));
    }

    [Fact]
    public async Task 월간은_설비팀만_조치함_보기는_바로_조치_완료()
    {
        using var t = new TestDb();
        var svc = Svc(t);
        var nozzle = Item(t, "NDC02", EqCycles.Monthly, "분사 Nozzle 점검");
        var req = new EqCheckSaveRequest(EqCycles.Monthly, "2026-10", "위치조정", null, false, null, ViaQr: true);
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveResultAsync("NDC02", nozzle.Id, req, Worker));
        var r = await svc.SaveResultAsync("NDC02", nozzle.Id, req, Facility);
        Assert.Equal(("NG", "DONE", EqCheckService.AutoClosePrefix + "위치조정"), (r!.Judge, r.NgStatus, r.NgCloseNote));

        var sheet = await svc.GetSheetAsync("NDC02", null, Worker);
        var month = sheet!.Periods.Single(p => p.Cycle == EqCycles.Monthly);
        Assert.False(month.CanEdit);
        Assert.True(sheet.Periods.Single(p => p.Cycle == EqCycles.Daily).CanEdit);
    }

    [Fact]
    public async Task 조회_등급은_QR_로만_편집_등급과_설비팀은_목록에서도()
    {
        using var t = new TestDb();
        var svc = Svc(t);
        var daily = Item(t, "NDC02", EqCycles.Daily, "Bath 수위");
        var offQr = new EqCheckSaveRequest(EqCycles.Daily, "2026-10-01", "O", null, false, null);   // 현황 목록·PC 에서 연 화면
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveResultAsync("NDC02", daily.Id, offQr, Worker));
        Assert.Equal(EqCheckService.QrOnlyMessage, ex.Message);
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveNoteAsync("NDC02", new EqCheckNoteRequest(EqCycles.Daily, "2026-10-01", "메모"), Worker));

        Assert.NotNull(await svc.SaveResultAsync("NDC02", daily.Id, offQr, Worker with { CanEdit = true }));   // 편집 등급
        Assert.NotNull(await svc.SaveResultAsync("NDC02", daily.Id, offQr, Facility));                        // 설비팀
        Assert.False((await svc.GetStatusAsync(null, Worker)).CanOpenOffQr);
        Assert.True((await svc.GetStatusAsync(null, Facility)).CanOpenOffQr);
    }

    [Fact]
    public async Task 지난_기간과_앞으로의_기간은_막는다()
    {
        using var t = new TestDb();
        var svc = Svc(t);
        var daily = Item(t, "NDC02", EqCycles.Daily, "Bath 수위");
        await svc.SaveResultAsync("NDC02", daily.Id, new EqCheckSaveRequest(EqCycles.Daily, "2026-09-30", "O", null, false, null, ViaQr: true), Worker);   // 어제는 된다
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            svc.SaveResultAsync("NDC02", daily.Id, new EqCheckSaveRequest(EqCycles.Daily, "2026-09-28", "O", null, false, null, ViaQr: true), Worker));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            svc.SaveResultAsync("NDC02", daily.Id, new EqCheckSaveRequest(EqCycles.Daily, "2026-10-02", "O", null, false, null, ViaQr: true), Worker));
    }

    [Fact]
    public async Task 현황과_NG_조치_월간_점검표()
    {
        using var t = new TestDb();
        var svc = Svc(t);
        var u = t.Db.EqCheckUnits.Single(x => x.Code == "SUP-HF");
        foreach (var i in t.Db.EqCheckItems.Where(i => i.TemplateId == u.TemplateId && i.Cycle == EqCycles.Daily).ToList())
            await svc.SaveResultAsync("SUP-HF", i.Id, new EqCheckSaveRequest(EqCycles.Daily, "2026-10-01", i.Name == "Cleaning" ? "X" : "O", null, false, null, ViaQr: true), Worker);
        var fault = await svc.AddFaultAsync(new EqCheckFaultRequest("SUP-HF", new DateOnly(2026, 10, 1), "펌프 소음", null), Worker);

        var st = await svc.GetStatusAsync(null);
        var row = st.Rows.Single(r => r.UnitCode == "SUP-HF");
        Assert.Equal(("done", 1, 1), (row.Daily.State, row.Daily.Ng, row.DailyDoneDays));
        Assert.Equal("todo", row.Weekly.State);
        Assert.Equal(2, row.OpenNg);
        Assert.Equal("공통", row.Line);

        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.CloseNgAsync(fault.Id, "교체", Worker));   // 생산직(조회 등급)은 조치 완료 못 함
        var closed = await svc.CloseNgAsync(fault.Id, "베어링 교체", Facility);
        Assert.Equal("DONE", closed.NgStatus);
        Assert.Single(await svc.GetNgsAsync(true, null, "SUP-HF", null, null));

        var m = await svc.GetMonthAsync("SUP-HF", 2026, 10);
        Assert.Equal(new[] { "2026-10-02", "2026-10-09", "2026-10-16", "2026-10-23", "2026-10-30" }, m!.WeekKeys);
        Assert.Equal(5, m.Cells.Count(c => c.Cycle == EqCycles.Daily));
        Assert.Equal(2, m.Faults.Count);
    }
}
