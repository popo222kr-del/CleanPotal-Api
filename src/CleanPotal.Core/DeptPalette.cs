namespace CleanPotal.Core;

/// <summary>
/// 부서 색 배정.
///
/// 색을 지정하지 않은 부서는 <b>Id 기준</b>으로 자동 배정한다. 순서(몇 번째 부서인가)로
/// 정하면 중간 부서를 지웠을 때 나머지 색이 한 칸씩 밀려 전부 바뀐다.
/// 색약·흑백 인쇄를 고려해 화면에는 색과 함께 약칭을 붙인다(색만으로 구분하지 않는다).
/// </summary>
public static class DeptPalette
{
    /// <summary>서로 충분히 구분되는 색들. 채도를 낮춰 달력 글씨를 가리지 않게 했다.</summary>
    private static readonly string[] Colors =
    {
        "#3D6E93",  // 청
        "#2F6B3A",  // 녹
        "#A9552F",  // 주황
        "#6B4E9B",  // 보라
        "#A93C36",  // 적
        "#1F7A7A",  // 청록
        "#8A6D1F",  // 황토
        "#4A5568",  // 회청
    };

    /// <summary>지정한 색이 있으면 그것을, 없으면 Id 기준 자동 배정.</summary>
    public static string Resolve(string? color, int id)
    {
        var c = (color ?? "").Trim();
        if (IsHex(c)) return c.ToUpperInvariant();
        return Colors[Math.Abs(id) % Colors.Length];
    }

    /// <summary>지정한 약칭이 있으면 그것을, 없으면 이름 앞 두 글자.</summary>
    public static string ResolveShortName(string? shortName, string? name)
    {
        var s = (shortName ?? "").Trim();
        if (s.Length > 0) return s;
        var n = (name ?? "").Trim();
        return n.Length <= 2 ? n : n[..2];
    }

    private static bool IsHex(string c)
        => c.Length == 7 && c[0] == '#' && c.Skip(1).All(Uri.IsHexDigit);
}
