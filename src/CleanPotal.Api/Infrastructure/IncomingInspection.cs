using System.Globalization;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 입고검사 이력 — 입고검사하면서 그 부품(같은 반출 S/N)의 지난 회차를 바로 보게 한다(세정 작업 › 입고검사 이력).
/// 기존 MES DB 를 읽기만 하고, 단가·금액은 싣지 않는다(세정 작업 권한으로 본다). DB 와 떨어진 계산은 여기 모아 시험한다.
/// </summary>
public static class IncomingInspection
{
    /// <summary>외관 항목 — 값이 Y 면 이상</summary>
    public static readonly string[] Visual = ["CHIP", "CRACK", "SCR", "STAIN", "PIT", "STRIP", "UNETCH"];
    /// <summary>입고검사 쪽 공정 — 나머지(최종검사 7000 등)는 출하검사 쪽</summary>
    public static bool IsInSide(string oper) => oper is "RCV" or "2000" or "2100";

    public sealed record SpecLine(string Param, string Oper, string Name, string Min, string Max);

    /// <summary>한 회차(LOT 하나) 요약</summary>
    public sealed record Cycle(
        string LotId, string UseCount, string? InDay, string? OutDay, string Status, bool Current,
        string? WeightIn, string? WeightOut, string? Etch,
        List<string> InVisual, List<string> OutVisual, string? Particle, string? Rough, string? Result,
        List<string> OutOfSpec, string Comment);

    private static decimal? Num(string? s)
        => decimal.TryParse((s ?? "").Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string Fmt(decimal d) => d.ToString("0.##", CultureInfo.InvariantCulture);

    public static string SpecText(SpecLine s)
        => s.Min.Length > 0 && s.Max.Length > 0 ? $"{s.Min}~{s.Max}" : s.Min.Length > 0 ? $"{s.Min} 이상" : $"{s.Max} 이하";

    /// <summary>값이 스펙 밖인가 — 스펙이 없거나 숫자가 아니면 아니다.</summary>
    public static bool OutOfSpec(string value, SpecLine? s)
    {
        if (s is null || Num(value) is not decimal v) return false;
        return (Num(s.Min) is decimal lo && v < lo) || (Num(s.Max) is decimal hi && v > hi);
    }

    /// <summary>LOT 과 그 측정값으로 회차 요약을 만든다. 같은 항목을 여러 번 쟀으면 마지막(HIST_SEQ 큰 것).</summary>
    public static Cycle BuildCycle(LegacyLot lot, IEnumerable<LegacyEdc> edc, IReadOnlyList<SpecLine> spec, bool current)
    {
        var latest = edc
            .GroupBy(e => (In: IsInSide(e.Oper), e.Param))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => int.TryParse(e.Seq, out var n) ? n : 0).First());
        string? Val(bool inSide, string p) => latest.TryGetValue((inSide, p), out var e) ? e.Values.FirstOrDefault(v => v.Length > 0) : null;
        List<string> Bad(bool inSide) => Visual.Where(p => string.Equals(Val(inSide, p), "Y", StringComparison.OrdinalIgnoreCase)).ToList();

        var wIn = Val(true, "WEIGHT");
        var wOut = Val(false, "WEIGHT");
        var etch = Num(wIn) is decimal a && Num(wOut) is decimal b ? Fmt(a - b) : null;
        var particle = latest.TryGetValue((false, "PARTICLE"), out var pe) && pe.Values.Any(v => v.Length > 0)
            ? string.Join("/", pe.Values.Where(v => v.Length > 0)) : null;
        var result = Val(false, "RESULT") ?? Val(true, "RESULT");

        // 스펙 밖 — 숫자 항목만(외관·판정 제외). 값이 여러 개(표면먼지 A/B/C)면 하나라도 밖이면.
        var oos = new List<string>();
        foreach (var e in latest.Values.Where(e => !Visual.Contains(e.Param) && e.Param is not "RESULT" and not "NTHK")
                     .OrderBy(e => IsInSide(e.Oper) ? 0 : 1).ThenBy(e => e.Param, StringComparer.Ordinal))
        {
            var s = spec.FirstOrDefault(x => x.Param == e.Param && x.Oper == e.Oper) ?? spec.FirstOrDefault(x => x.Param == e.Param && x.Oper.Length == 0);
            var bad = e.Values.Where(v => OutOfSpec(v, s)).ToList();
            if (bad.Count > 0 && s is not null)
                oos.Add($"{(IsInSide(e.Oper) ? "입고" : "출하")} {e.Param} {string.Join("/", bad)} (기준 {SpecText(s)})");
        }

        string? ship = lot.Hist is not null && lot.Hist.TryGetValue("ship", out var sp) ? sp.Split('|')[0] : null;
        var comment = lot.Hist is not null && lot.Hist.TryGetValue("cmt", out var c) && c.Split('|', 2) is { Length: 2 } cp ? cp[1].Trim() : lot.Comment;
        return new Cycle(lot.LotId, lot.UseCount, Day(lot.CreateTime), Day(ship ?? lot.AetsOutDay),
            LegacyMesReports.OperName(lot.Oper, lot.Factory), current,
            wIn, wOut, etch, Bad(true), Bad(false), particle, Val(false, "ROUGH"), result, oos, comment);
    }

    private static string? Day(string? s) => LegacyMesDb.ParseDay(s ?? "")?.ToString("yyyy-MM-dd");

    /// <summary>
    /// 입고검사 때 볼 경고 — 지난 회차(최근 것부터)에서: 직전 판정 NG · 외관 이상 · 스펙 밖 값. 같은 말은 한 번만.
    /// </summary>
    public static List<string> Warnings(IReadOnlyList<Cycle> previousNewestFirst)
    {
        var w = new List<string>();
        if (previousNewestFirst.Count == 0) return w;
        var last = previousNewestFirst[0];
        if (string.Equals(last.Result, "NG", StringComparison.OrdinalIgnoreCase)) w.Add($"직전({Use(last)}) 판정 NG");
        foreach (var c in previousNewestFirst)
        {
            if (c.InVisual.Count > 0) w.Add($"{Use(c)} 입고 외관 이상 — {string.Join("·", c.InVisual)}");
            if (c.OutVisual.Count > 0) w.Add($"{Use(c)} 출하 외관 이상 — {string.Join("·", c.OutVisual)}");
            w.AddRange(c.OutOfSpec.Select(o => $"{Use(c)} {o}"));
        }
        return w.Distinct().ToList();
    }

    private static string Use(Cycle c) => c.UseCount.Length > 0 ? $"{c.UseCount}회" : c.InDay ?? c.LotId;

    /// <summary>
    /// 대기 목록 표시 — 최근 회차들(최근 것부터, 사용횟수·측정값)에서: 판정 NG · 외관 이상(MES 항목 이름 그대로 CHIP·CRACK …).
    /// 어느 회차였는지 붙인다("NG 10회", "SCR 8·9회"). 이상이 없으면 빈 목록.
    /// </summary>
    public static List<string> RecentFlags(IEnumerable<(string UseCount, IEnumerable<LegacyEdc> Edc)> recentNewestFirst)
    {
        var hits = new List<(string Key, string Use)>();
        foreach (var (use, edc) in recentNewestFirst)
        {
            var latest = edc.GroupBy(e => (In: IsInSide(e.Oper), e.Param))
                .Select(g => g.OrderByDescending(e => int.TryParse(e.Seq, out var n) ? n : 0).First()).ToList();
            if (latest.Any(e => e.Param == "RESULT" && e.Values.Any(v => v.Equals("NG", StringComparison.OrdinalIgnoreCase)))) hits.Add(("NG", use));
            foreach (var p in Visual.Where(p => latest.Any(e => e.Param == p && e.Values.Any(v => v.Equals("Y", StringComparison.OrdinalIgnoreCase)))))
                hits.Add((p, use));
        }
        string Label(IEnumerable<string> uses)
        {
            var u = uses.Where(x => x.Length > 0).Distinct().ToList();
            return u.Count == 0 ? "" : $" {string.Join("·", u)}회";
        }
        return hits.GroupBy(h => h.Key).OrderBy(g => g.Key == "NG" ? 0 : 1 + Array.IndexOf(Visual, g.Key))
            .Select(g => g.Key + Label(g.Select(x => x.Use).Reverse())).ToList();
    }
}
