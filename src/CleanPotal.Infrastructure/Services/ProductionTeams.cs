using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// 교대 근무하는 생산팀이 무엇인지, 어느 팀이 몇 조인지.
///
/// 예전에는 코드에 "김팀"/"장팀" 이 박혀 있어서, 팀 이름을 바꾸면 근무표·달력·오늘 현황이
/// 모두 빈 화면이 되고 교대 예측도 한쪽으로 쏠렸다. 이제 조직도(OrgUnits)의
/// <see cref="CleanPotal.Core.Entities.OrgUnit.ShiftGroup"/> 을 보므로 이름을 바꿔도 따라온다.
/// </summary>
public sealed class ProductionTeams
{
    /// <summary>
    /// 아직 교대조를 지정하지 않은 DB 를 위한 기본값. 예전 동작을 그대로 유지해,
    /// 이 기능을 올렸다는 이유만으로 멀쩡하던 화면이 비지 않게 한다.
    /// 팀 이름을 바꾼 뒤에는 조직도에서 교대조를 지정해야 한다.
    /// </summary>
    private static readonly (string Name, int Group)[] Legacy = { ("김팀", 1), ("장팀", 2) };

    private readonly Dictionary<string, int> _groupByName;

    /// <summary>교대 순서(1조 → 2조)대로의 팀 이름.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>조직도에 교대조가 지정돼 있으면 true. false 면 옛 기본값으로 동작 중이다.</summary>
    public bool IsConfigured { get; }

    private ProductionTeams(IEnumerable<(string Name, int Group)> teams, bool configured)
    {
        var ordered = teams
            .Where(t => !string.IsNullOrWhiteSpace(t.Name) && t.Group > 0)
            .Select(t => (Name: t.Name.Trim(), t.Group))
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(t => t.Group)
            .ToList();

        _groupByName = ordered.ToDictionary(t => t.Name, t => t.Group, StringComparer.Ordinal);
        Names = ordered.Select(t => t.Name).ToList();
        IsConfigured = configured;
    }

    public static async Task<ProductionTeams> LoadAsync(CleanPotalDbContext db)
    {
        var units = await db.OrgUnits
            .Where(o => o.Kind == "team" && o.ShiftGroup > 0)
            .Select(o => new { o.Name, o.ShiftGroup, o.OrderIndex })
            .ToListAsync();

        if (units.Count > 0)
            return new ProductionTeams(
                units.OrderBy(u => u.ShiftGroup).ThenBy(u => u.OrderIndex).Select(u => (u.Name, u.ShiftGroup)),
                configured: true);

        return new ProductionTeams(Legacy, configured: false);
    }

    /// <summary>교대 근무하는 생산팀인가.</summary>
    public bool IsProduction(string? team) => GroupOf(team) > 0;

    /// <summary>1조 / 2조. 교대 근무가 아니면 0.</summary>
    public int GroupOf(string? team)
        => team is not null && _groupByName.TryGetValue(team.Trim(), out var g) ? g : 0;

    /// <summary>해당 날짜의 근무(주간/야간). 교대 근무 팀이 아니면 빈 문자열.</summary>
    public string PredictShift(string? team, DateOnly date)
    {
        var g = GroupOf(team);
        return g == 0 ? "" : ShiftPredictor.Predict(g, date);
    }
}
