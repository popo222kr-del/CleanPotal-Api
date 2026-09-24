using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 하루치를 한 번에 저장하는 화면(배차표·스케줄보드)에서 두 사람이 같은 날을 열어 두고 저장할 때.
/// 예전에는 나중에 저장한 사람이 먼저 저장한 사람의 행을 지우고, 다른 날로 이월된 행을 되돌려 놓았다.
/// </summary>
public class ConcurrentDaySaveTests
{
    private static readonly DateOnly Day = new(2026, 9, 24);

    private static DispatchRowRequest Row(int id, string vendor)
        => new(id, vendor, "-", "", "담당", "010", "주소", "");

    [Fact]
    public async Task 배차표_다른_사람이_추가한_행은_지우지_않는다()
    {
        using var t = new TestDb();
        var svc = new DispatchService(t.Db);
        var a = await svc.SaveDayAsync(Day, new[] { Row(0, "A업체") });      // A 가 먼저 한 줄
        var knownByB = new List<int>();                                        // B 는 빈 날을 열어 둔 상태
        await svc.SaveDayAsync(Day, new[] { Row(0, "B업체") }, knownByB);      // B 가 저장

        var day = await svc.GetByDateAsync(Day);
        Assert.Equal(new[] { "A업체", "B업체" }, day.Select(d => d.VendorName));
        Assert.Contains(day, d => d.Id == a[0].Id);
    }

    [Fact]
    public async Task 배차표_이월된_행을_옛_화면이_되돌려_놓지_않는다()
    {
        using var t = new TestDb();
        var svc = new DispatchService(t.Db);
        var saved = await svc.SaveDayAsync(Day, new[] { Row(0, "A업체") });
        var id = saved[0].Id;

        await svc.MoveAsync(id, Day.AddDays(1));                              // 누군가 다음 날로 이월
        await svc.SaveDayAsync(Day, new[] { Row(id, "A업체") }, new[] { id }); // 옛 화면이 그대로 저장

        Assert.Empty(await svc.GetByDateAsync(Day));
        Assert.Single(await svc.GetByDateAsync(Day.AddDays(1)));
    }

    [Fact]
    public async Task 배차표_내가_지운_행은_지워진다()
    {
        using var t = new TestDb();
        var svc = new DispatchService(t.Db);
        var saved = await svc.SaveDayAsync(Day, new[] { Row(0, "A업체"), Row(0, "B업체") });
        var known = saved.Select(d => d.Id).ToList();

        await svc.SaveDayAsync(Day, new[] { Row(saved[0].Id, "A업체") }, known);

        Assert.Equal(new[] { "A업체" }, (await svc.GetByDateAsync(Day)).Select(d => d.VendorName));
    }

    [Fact]
    public async Task 스케줄보드_그_사이_다른_사람이_저장했으면_덮어쓰지_않는다()
    {
        using var t = new TestDb();
        var svc = new ScheduleBoardService(t.Db);
        var block = new ScheduleBlockRow(0, 60, 30, 10, 100, null, "30-10-100");

        var first = await svc.SaveDayAsync("2026-09-24", new[] { block }, Array.Empty<int>());
        // 빈 날을 열어 둔 다른 화면이 저장하려 하면 막힌다.
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => svc.SaveDayAsync("2026-09-24", new[] { block with { EquipmentIndex = 1 } }, Array.Empty<int>()));
        // 최신 번호를 들고 오면 저장된다.
        await svc.SaveDayAsync("2026-09-24", new[] { block with { EquipmentIndex = 2 } }, first.Select(b => b.Id).ToList());
        Assert.Equal(2, Assert.Single(await svc.GetDayAsync("2026-09-24")).EquipmentIndex);
    }
}
