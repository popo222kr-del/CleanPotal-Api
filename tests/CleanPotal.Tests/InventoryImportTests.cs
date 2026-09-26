using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>실사 반영 — 같은 날 두 번 반영해도 그 주 소비량 기준(반영 전 재고 스냅샷)이 지워지지 않는다.</summary>
public class InventoryImportTests
{
    [Fact]
    public async Task 같은_날_두_번째_반영은_첫_반영_전_스냅샷을_지키고_새_품목만_더한다()
    {
        using var t = new TestDb();
        var a = new InventoryItem { ItemName = "장갑", CurrentStock = "10" };
        t.Db.InventoryItems.Add(a);
        await t.Db.SaveChangesAsync();
        var svc = new InventoryService(t.Db);
        var today = DateTime.Now.ToString("yyyy-MM-dd");

        await svc.ConfirmImportAsync([new InventoryImportRow(a.Id, "8")]);
        var b = new InventoryItem { ItemName = "마스크", CurrentStock = "5" };   // 그 사이 새 품목
        t.Db.InventoryItems.Add(b);
        await t.Db.SaveChangesAsync();
        await svc.ConfirmImportAsync([new InventoryImportRow(a.Id, "7")]);    // 정정 반영

        using var fresh = t.NewContext();
        var snap = fresh.InventorySnapshots.Where(s => s.SnapshotDate == today).ToDictionary(s => s.ItemId, s => s.Stock);
        Assert.Equal("10", snap[a.Id]);   // 예전에는 "8" 로 덮여 10→7 소비가 1 로 줄었다
        Assert.Equal("5", snap[b.Id]);
        Assert.Equal("7", fresh.InventoryItems.Single(i => i.Id == a.Id).CurrentStock);
    }
}
