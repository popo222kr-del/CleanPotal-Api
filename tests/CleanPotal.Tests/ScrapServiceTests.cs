using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>업무 파일 통합 관리 — 폐기품 LIST·눈관리 요청·분임조 표.</summary>
public class ScrapServiceTests
{
    private static readonly DateOnly D = new(2026, 9, 9);

    private static ScrapItemSaveRequest I(string line, string sn, string remark = "폐기품", bool loaded = false)
        => new(line, "610000808", "(C)BQ_BOAT_ZRO_LASER", sn, "C0204837903-1", false, loaded, remark);

    [Fact]
    public async Task LIST_에_줄을_넣고_체크하고_상차_완료하면_품목은_잠긴다()
    {
        using var t = new TestDb();
        var svc = new WorkLogService(t.Db);
        var b = await svc.SaveScrapBatchAsync(0, new ScrapBatchSaveRequest(D, "2026-09-09 폐기품 LIST", "", false), "홍길동");
        b = await svc.AddScrapItemsAsync(b.Id, new[] { I("P1-1 LINE", "AOVG2410-3103"), I("17LINE", "AOVG2411-1828"), new ScrapItemSaveRequest("", "", "", "", "", false, false, "") }, "홍길동");
        Assert.Equal(new[] { 1, 2 }, b.Items.Select(i => i.SortOrder));   // 빈 줄은 건너뜀

        var first = b.Items[0];
        await svc.SaveScrapItemAsync(first.Id, new ScrapItemSaveRequest(first.Line, first.MatId, first.MatDesc, first.SerialNo, first.OutNo, true, true, first.Remark), "현장");
        b = await svc.SaveScrapBatchAsync(b.Id, new ScrapBatchSaveRequest(D, b.Title, "", true), "홍길동");
        Assert.True(b.IsClosed);
        Assert.Equal("홍길동", b.ClosedBy);

        var sum = (await svc.GetScrapBatchesAsync()).Single();
        Assert.Equal((2, 1, 1), (sum.Count, sum.Matched, sum.Loaded));

        // 닫힌 LIST — 체크는 되고 품목은 안 된다
        var second = b.Items[1];
        await svc.SaveScrapItemAsync(second.Id, new ScrapItemSaveRequest(second.Line, second.MatId, second.MatDesc, second.SerialNo, second.OutNo, false, true, "늦게 상차"), "현장");
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveScrapItemAsync(second.Id,
            new ScrapItemSaveRequest("S3 LINE", second.MatId, second.MatDesc, second.SerialNo, second.OutNo, false, true, ""), "현장"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.AddScrapItemsAsync(b.Id, new[] { I("7LINE", "X1") }, "x"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.DeleteScrapItemAsync(second.Id));

        var found = await svc.SearchScrapAsync("3103");
        Assert.Equal(b.Id, found.Items.Single().BatchId);
        Assert.Equal("(C)BQ_BOAT_ZRO_LASER", (await svc.GetScrapMaterialsAsync()).Single(m => m.MatId == "610000808").MatDesc);
    }

    [Fact]
    public async Task 눈관리는_분임조를_고르면_라인과_담당자를_채운다()
    {
        using var t = new TestDb();
        var svc = new WorkLogService(t.Db);
        await svc.SaveScrapCirclesAsync(new[] { new ScrapCircleDto(0, "알파고", "S4 LINE", "공지훈"), new ScrapCircleDto(0, "MLD", "S1 LINE", "이미림") });
        var tag = await svc.SaveScrapTagAsync(0, new ScrapTagSaveRequest(D, "", "(C)300mm QTZ INNER TUBE_HFOX_TEL", "Y2406T0450", "", "알파고", "", "실물 SN 미확인", false), "이재백");
        Assert.Equal(("S4 LINE", "공지훈", "이재백"), (tag.Line, tag.Owner, tag.Writer));
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveScrapTagAsync(0, new ScrapTagSaveRequest(D, "", "", "", "", "", "", "", false), "x"));

        // 빠진 분임조는 지운다
        var circles = await svc.SaveScrapCirclesAsync(new[] { new ScrapCircleDto(0, "MLD", "S1 LINE", "이미림") });
        Assert.Equal("MLD", circles.Single().Name);
    }

    [Fact]
    public async Task 엑셀_가져오기는_이미_있는_LIST_와_눈관리를_건너뛴다()
    {
        using var t = new TestDb();
        var svc = new WorkLogService(t.Db);
        var req = new ScrapImportRequest(
            new[]
            {
                new ScrapImportBatch(new DateOnly(2026, 1, 15), "2026-01-15 폐기품 LIST", true, new[] { I("16LINE", "AOVG1507-6246"), I("17LINE", "AOVG1602-9223") }),
                new ScrapImportBatch(D, "2026-09-09 폐기품 LIST", false, new[] { I("P1-1 LINE", "AOVG2410-3103") }),
                new ScrapImportBatch(D, "2026-09-09 폐기품 LIST", true, new[] { I("S3 LINE", "SS24114") }),   // 같은 날 두 번째 상차
                // 이력으로 옮기며 라인 순으로 다시 정렬된 같은 LIST — 건너뛴다
                new ScrapImportBatch(new DateOnly(2026, 1, 15), "2026-01-15 폐기품 LIST", true, new[] { I("17LINE", "AOVG1602-9223"), I("16LINE", "AOVG1507-6246") }),
            },
            new[] { new ScrapTagSaveRequest(D, "김경래", "(C)RING BOAT_MTO", "K25121042-001", "S1 LINE", "MLD", "이미림", "실물 상이함", true) },
            new[] { new ScrapCircleDto(0, "MLD", "S1 LINE", "이미림") });
        var r = await svc.ImportScrapAsync(req, "엑셀");
        Assert.Equal((3, 4, 1, 1, 1), (r.Batches, r.Items, r.SkippedBatches, r.Tags, r.Circles));
        r = await svc.ImportScrapAsync(req, "엑셀");
        Assert.Equal((0, 4, 0, 1), (r.Batches, r.SkippedBatches, r.Tags, r.SkippedTags));
        Assert.Equal(new[] { false, true, true }, (await svc.GetScrapBatchesAsync()).Select(b => b.IsClosed));   // 작성 중인 LIST 가 먼저
    }
}
