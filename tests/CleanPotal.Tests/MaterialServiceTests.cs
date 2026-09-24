using CleanPotal.Core.DTOs;
using CleanPotal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 자재물류 일정. 같은 사람이 두 줄로 들어오면 예전에는 삭제를 먼저 커밋한 뒤 넣다가 고유 인덱스에 걸려
/// 500 과 함께 그날 일정이 통째로 사라졌다.
/// </summary>
public class MaterialServiceTests
{
    private static readonly DateOnly Day = new(2026, 9, 24);

    [Fact]
    public async Task 명단의_같은_이름은_한_번만_남는다()
    {
        using var t = new TestDb();
        var names = await new MaterialService(t.Db).SaveRosterAsync(new MaterialRosterSaveRequest(new List<string> { "김", "이", " 김 " }));
        Assert.Equal(new[] { "김", "이" }, names);
    }

    [Fact]
    public async Task 같은_사람이_두_줄이어도_그날_일정이_사라지지_않는다()
    {
        using var t = new TestDb();
        var svc = new MaterialService(t.Db);
        await svc.SaveRosterAsync(new MaterialRosterSaveRequest(new List<string> { "김" }));
        await svc.SaveDayAsync(Day, new MaterialSaveRequest(
            new List<MaterialRowInput> { new("김", new("A업체", null), new("", null)) }, "", ""));

        await svc.SaveDayAsync(Day, new MaterialSaveRequest(new List<MaterialRowInput>
        {
            new("김", new("B업체", null), new("", null)),
            new("김", new("C업체", null), new("", null)),
        }, "메모", ""));

        var entries = await t.Db.MaterialScheduleEntries.AsNoTracking().Where(e => e.TargetDate == Day).ToListAsync();
        Assert.Equal("B업체", Assert.Single(entries).Destination);
    }
}
