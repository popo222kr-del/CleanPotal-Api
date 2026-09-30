using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using CleanPotal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>설비 목록 통합 — 스케줄 보드 설비 표가 모든 화면의 설비 목록이다(2026-09-30).</summary>
public class EquipmentCatalogTests
{
    /// <summary>통합 전 운영 DB 모양 — 보드 설비 19대(라인·종류 빈 칸) + 업무 기록 설비 표.</summary>
    private static void OldDb(CleanPotalDbContext db)
    {
        string[] board = { "MDC01", "MDC02", "MSC01-1", "MSC01-2", "NDC01", "NDC07" };
        for (var i = 0; i < board.Length; i++)
            db.ScheduleEquipments.Add(new ScheduleEquipment
            {
                Name = board[i], GroupName = board[i][..3], Process = "P", Slot = i, OrderIndex = i, IsActive = true, Line = "", Kind = "",
            });
        db.ScheduleEquipGroups.AddRange(new ScheduleEquipGroup { Name = "MDC" }, new ScheduleEquipGroup { Name = "MSC", OrderIndex = 1 },
                                        new ScheduleEquipGroup { Name = "NDC", OrderIndex = 2 });
        var n = 0;
        foreach (var (code, line, process) in new[]
                 {
                     ("MDC01", "METAL", "POLY(L10)"), ("MSC02-1", "METAL", "POLY(대대배치)"), ("NDC08", "N-METAL", "OTT"),
                     ("MBO01-1", "METAL", "BAKE"), ("MBO04-1", "N-METAL", "BAKE"), ("ZZZ01", "METAL", "옛 설비"),
                 })
            db.WorkEquipments.Add(new WorkEquipment { Code = code, Line = line, Kind = "", Process = process, SortOrder = ++n, IsActive = code != "ZZZ01" });
        db.SaveChanges();
    }

    [Fact]
    public void 기존_DB는_한_번_옮기고_두_번째는_그대로_둔다()
    {
        using var t = new TestDb();
        OldDb(t.Db);

        EquipmentCatalog.Backfill(t.Db);
        var all = t.Db.ScheduleEquipments.AsNoTracking().ToList();

        // ① 보드의 옛 이름 MSC01 → MSC02 (현장·약액 기록·ICP-MS 이름)
        Assert.DoesNotContain(all, e => e.Name.StartsWith("MSC01"));
        var msc = all.Single(e => e.Name == "MSC02-1");
        Assert.Equal(2, msc.Slot);                                          // 같은 줄 — 보드 배치는 그대로
        Assert.True(msc.ShowOnBoard);
        // ② 라인·종류
        Assert.All(all, e => Assert.NotEqual("", e.Kind));
        Assert.Equal("N-METAL", all.Single(e => e.Name == "NDC01").Line);
        Assert.Equal("P", all.Single(e => e.Name == "MDC01").Process);     // 공정은 보드 것
        // ③ 업무 기록 설비만 있던 것: NDC08 은 보드에, 오븐은 목록에만
        Assert.True(all.Single(e => e.Name == "NDC08").ShowOnBoard);
        var mbo4 = all.Single(e => e.Name == "MBO04-1");
        Assert.Equal((EquipKinds.Bake, "N-METAL", false), (mbo4.Kind, mbo4.Line, mbo4.ShowOnBoard));
        Assert.False(all.Single(e => e.Name == "ZZZ01").IsActive);          // 끈 설비는 끈 채로(지난 기록용)
        // ④ 새 기본 설비
        Assert.True(all.Single(e => e.Name == "SPC02").ShowOnBoard);
        Assert.True(all.Single(e => e.Name == "RFC01").ShowOnBoard);
        Assert.Equal((EquipKinds.Dry, false), (all.Single(e => e.Name == "MDO01").Kind, all.Single(e => e.Name == "MDO01").ShowOnBoard));
        Assert.Equal(all.Count, all.Select(e => e.Slot).Distinct().Count());   // 줄 번호 겹치지 않음
        Assert.Contains(t.Db.ScheduleEquipGroups.AsNoTracking(), g => g.Name == "BAKE");

        // 두 번째 기동 — 관리자가 고친 것(예: 설비 삭제)을 다시 되살리지 않는다
        var spc = t.Db.ScheduleEquipments.Single(e => e.Name == "SPC02");
        t.Db.ScheduleEquipments.Remove(spc);
        t.Db.SaveChanges();
        EquipmentCatalog.Backfill(t.Db);
        Assert.DoesNotContain(t.Db.ScheduleEquipments.AsNoTracking(), e => e.Name == "SPC02");
    }

    [Fact]
    public async Task 보드는_보드_설비만_관리_창은_전부_보여준다()
    {
        using var t = new TestDb();
        EquipmentCatalog.SeedDefaults(t.Db);
        var board = new ScheduleBoardService(t.Db);

        var shown = await board.GetEquipmentsAsync();
        var all = await board.GetEquipmentsAsync(includeHidden: true);
        Assert.Contains(shown, e => e.Name == "NDC08");
        Assert.Contains(shown, e => e.Name == "SPC01");
        Assert.DoesNotContain(shown, e => e.Name == "MBO01-1");
        Assert.Contains(all, e => e.Name == "MBO01-1" && e.Kind == EquipKinds.Bake && !e.ShowOnBoard);
        Assert.Equal(EquipmentCatalog.Defaults.Length, all.Count);
    }

    [Fact]
    public async Task 같은_이름은_두_번_넣지_않고_지운_설비는_되살린다()
    {
        using var t = new TestDb();
        EquipmentCatalog.SeedDefaults(t.Db);
        var board = new ScheduleBoardService(t.Db);
        await Assert.ThrowsAsync<BusinessRuleException>(() => board.AddEquipmentAsync(new ScheduleEquipmentUpsertRequest("mdc1", "MDC", "", "", false)));

        var mdo = (await board.GetEquipmentsAsync(true)).Single(e => e.Name == "MDO01");
        await board.DeleteEquipmentAsync(mdo.Id);
        var again = await board.AddEquipmentAsync(new ScheduleEquipmentUpsertRequest("MDO01", "DRY", "", "", false, ShowOnBoard: false));
        Assert.Equal((mdo.Id, mdo.Index), (again.Id, again.Index));          // 같은 줄 — 지난 기록이 그대로 붙는다

        var added = await board.AddEquipmentAsync(new ScheduleEquipmentUpsertRequest("NBO4-1", "", "", "", false, ShowOnBoard: false));
        Assert.Equal(("NBO04-1", EquipKinds.Bake, "N-METAL", "BAKE"), (added.Name, added.Kind, added.Line, added.GroupName));
    }
}
