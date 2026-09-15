using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Data;

/// <summary>
/// WPF 시절 팀 이름 → 현재 팀 이름.
///
/// WPF 와 웹을 함께 쓰는 동안 WPF 는 옛 이름(예: "김팀")을 계속 기록한다. 웹에서 팀 이름을
/// 바꿨다면 임포트할 때 바꿔 넣어야 한다. 그렇지 않으면
/// - WPF 에서 새로 등록된 직원이 없어진 팀에 배정되어 <b>근무표에서 조용히 사라지고</b>,
/// - 근무표 행의 팀 이름이 옛 이름으로 돌아와 주간/야간 팀 라벨이 어긋난다.
///
/// 별칭은 조직도(OrgUnit.LegacyNames)에서 관리한다. WPF 를 끄면 비워도 된다.
/// </summary>
public sealed class TeamAliases
{
    private readonly Dictionary<string, string> _map;

    private TeamAliases(Dictionary<string, string> map) => _map = map;

    public static TeamAliases Load(CleanPotalDbContext db)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var units = db.OrgUnits
            .Where(o => o.Kind == "team" && o.LegacyNames != "")
            .Select(o => new { o.Name, o.LegacyNames })
            .ToList();

        foreach (var u in units)
        {
            var current = (u.Name ?? "").Trim();
            if (current.Length == 0) continue;
            foreach (var raw in (u.LegacyNames ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var old = raw.Trim();
                // 현재 이름과 같은 별칭은 무시한다(자기 자신으로의 변환은 의미가 없다).
                if (old.Length == 0 || string.Equals(old, current, StringComparison.OrdinalIgnoreCase)) continue;
                map[old] = current;   // 같은 옛 이름이 여러 팀에 적혀 있으면 마지막 것을 쓴다
            }
        }
        return new TeamAliases(map);
    }

    public bool IsEmpty => _map.Count == 0;

    /// <summary>옛 이름이면 현재 이름으로. 아니면 원래 값 그대로(앞뒤 공백만 정리).</summary>
    public string Normalize(string? team)
    {
        var t = (team ?? "").Trim();
        if (t.Length == 0) return "";
        return _map.TryGetValue(t, out var current) ? current : t;
    }

    public override string ToString()
        => _map.Count == 0 ? "(없음)" : string.Join(", ", _map.Select(kv => $"{kv.Key}→{kv.Value}"));
}
