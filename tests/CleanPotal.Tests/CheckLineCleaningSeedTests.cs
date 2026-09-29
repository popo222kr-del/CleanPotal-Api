using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>라인 內 청소(엑셀 13번) → 체크시트 항목 한 번 넣기.</summary>
public class CheckLineCleaningSeedTests
{
    [Fact]
    public void 기존_구역에_붙이고_없는_N_METAL_구역은_만들며_한_번만_넣는다()
    {
        using var t = new TestDb();
        CheckSheetSeed.Run(t.Db);
        CheckSheetSeed.AddLineCleaning(t.Db);

        var items = t.Db.CheckItems.Where(i => i.Code.Contains("-CL")).OrderBy(i => i.Code).ToList();
        Assert.Equal(7, items.Count);
        Assert.Equal("M-CLEAN", items.Single(i => i.Code == "M-CL01").ZoneCode);
        Assert.Equal("M-DRY", items.Single(i => i.Code == "M-CL02").ZoneCode);
        Assert.All(items, i => Assert.Equal((CheckTimings.Weekly, CheckPhotoPolicies.BeforeAfter), (i.Timing, i.PhotoPolicy)));
        Assert.Equal(4, t.Db.CheckZones.Count(z => z.Line == "N-METAL"));

        // 관리 화면에서 지운 항목은 다시 넣지 않는다
        t.Db.CheckItems.Remove(items[0]);
        t.Db.SaveChanges();
        CheckSheetSeed.AddLineCleaning(t.Db);
        Assert.Equal(6, t.Db.CheckItems.Count(i => i.Code.Contains("-CL")));
    }

    [Fact]
    public void 구역이_하나도_없으면_넣지_않는다()
    {
        using var t = new TestDb();
        CheckSheetSeed.AddLineCleaning(t.Db);
        Assert.Empty(t.Db.CheckItems);
    }
}
