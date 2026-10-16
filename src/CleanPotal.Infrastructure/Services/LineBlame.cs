using System.Text.Json;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// 글의 줄마다 쓴 사람 — 생산팀 인수인계(회의록)는 주간·야간 사람들이 한 칸에 이어 쓰므로 줄 단위로 남긴다.
/// 저장할 때 이전 글과 줄을 맞춰 보고(가장 긴 공통 줄 순서), 그대로인 줄은 원래 작성자, 새로 쓰거나 고친 줄은 저장한 사람.
/// 빈 줄은 작성자 없음(""). 저장 형식은 줄 수만큼의 이름 JSON 배열.
/// </summary>
public static class LineBlame
{
    public static string[] Lines(string? text) => (text ?? "").Replace("\r\n", "\n").Split('\n');

    /// <summary>저장된 JSON → 줄별 이름. 줄 수가 글과 다르거나 비어 있으면 fallback(그 칸을 쓴 사람이 한 명뿐이면 그 사람)으로 채운다.</summary>
    public static string[] Read(string? json, string? text, string fallback = "")
    {
        var lines = Lines(text);
        string[]? by = null;
        if (!string.IsNullOrWhiteSpace(json))
            try { by = JsonSerializer.Deserialize<string[]>(json); } catch (JsonException) { }
        if (by is null || by.Length != lines.Length)
            by = lines.Select(l => l.Trim().Length == 0 ? "" : fallback).ToArray();
        return by;
    }

    public static string Write(IReadOnlyList<string> by) => JsonSerializer.Serialize(by);

    /// <summary>이전 글·줄별 이름 → 새 글의 줄별 이름. 같은 줄은 이름을 이어받고, 나머지는 me.</summary>
    public static string[] Update(string? oldText, IReadOnlyList<string> oldBy, string? newText, string me)
    {
        var a = Lines(oldText);
        var b = Lines(newText);
        var result = b.Select(l => l.Trim().Length == 0 ? "" : me).ToArray();
        if (a.Length != oldBy.Count) return result;
        // 가장 긴 공통 줄 순서(LCS) — 인수인계 한 칸은 수십 줄이라 n×m 이면 충분
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
        for (int i = 0, j = 0; i < a.Length && j < b.Length;)
        {
            if (a[i] == b[j]) { if (b[j].Trim().Length > 0) result[j] = oldBy[i]; i++; j++; }
            else if (dp[i + 1, j] >= dp[i, j + 1]) i++;
            else j++;
        }
        return result;
    }
}
