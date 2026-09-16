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
    private static readonly (string Name, int Group, string Dept, string Division)[] Legacy =
        { ("김팀", 1, "", ""), ("장팀", 2, "", "") };

    private readonly Dictionary<string, int> _groupByName;
    private readonly Dictionary<string, string> _deptByName;
    private readonly Dictionary<string, string> _divisionByName;

    /// <summary>표시 순서대로의 팀 이름 — 본부별로 모은 뒤 그 안에서 1조 → 2조.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>조직도에 교대조가 지정돼 있으면 true. false 면 옛 기본값으로 동작 중이다.</summary>
    public bool IsConfigured { get; }

    private ProductionTeams(IEnumerable<(string Name, int Group, string Dept, string Division)> teams, bool configured)
    {
        var ordered = teams
            .Where(t => !string.IsNullOrWhiteSpace(t.Name) && t.Group > 0)
            .Select(t => (Name: t.Name.Trim(), t.Group, Dept: (t.Dept ?? "").Trim(), Division: (t.Division ?? "").Trim()))
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            // 본부가 다르면 교대 주기도 달라서 조 번호가 겹칠 수 있다. 본부별로 모아 둔다.
            .OrderBy(t => t.Division.Length == 0 ? 1 : 0)     // 본부 미지정은 뒤로
            .ThenBy(t => t.Division, StringComparer.Ordinal)
            .ThenBy(t => t.Group)
            .ToList();

        _groupByName = ordered.ToDictionary(t => t.Name, t => t.Group, StringComparer.Ordinal);
        _deptByName = ordered.ToDictionary(t => t.Name, t => t.Dept, StringComparer.Ordinal);
        _divisionByName = ordered.ToDictionary(t => t.Name, t => t.Division, StringComparer.Ordinal);
        Names = ordered.Select(t => t.Name).ToList();
        IsConfigured = configured;
    }

    public static async Task<ProductionTeams> LoadAsync(CleanPotalDbContext db)
    {
        var units = await db.OrgUnits
            .Where(o => o.Kind == "team" && o.ShiftGroup > 0)
            .Select(o => new { o.Name, o.ShiftGroup, o.OrderIndex, o.Parent })
            .ToListAsync();

        if (units.Count == 0) return new ProductionTeams(Legacy, configured: false);

        // 팀 → 부서(Parent) → 본부(부서 행의 Parent)
        var deptRows = await db.OrgUnits.Where(o => o.Kind == "dept")
            .Select(o => new { o.Name, o.Parent }).ToListAsync();
        var divisionOf = deptRows
            .GroupBy(d => (d.Name ?? "").Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.First().Parent ?? "").Trim(), StringComparer.Ordinal);

        return new ProductionTeams(
            units.OrderBy(u => u.ShiftGroup).ThenBy(u => u.OrderIndex)
                 .Select(u => (u.Name, u.ShiftGroup, u.Parent,
                               divisionOf.GetValueOrDefault((u.Parent ?? "").Trim(), ""))),
            configured: true);
    }

    /// <summary>이 팀이 속한 부서. 모르면 빈 문자열.</summary>
    public string DeptOf(string? team)
        => team is not null && _deptByName.TryGetValue(team.Trim(), out var d) ? d : "";

    /// <summary>이 팀이 속한 본부(사업본부). 모르면 빈 문자열.</summary>
    public string DivisionOf(string? team)
        => team is not null && _divisionByName.TryGetValue(team.Trim(), out var d) ? d : "";

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
