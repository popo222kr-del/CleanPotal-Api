using System.Globalization;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 매출 트렌드 — 출하실적(출하 기준) LOT 를 날짜·영업 구분·고객사·라인·품목·설비·품목 유형으로 모아
/// 이번 주 / 이번 달 누계 / 월말 예상 / 올해 누계와 작년 같은 기간 대비, 월·주 추이, 증감 원인(구분별·물량·단가), 증감 TOP 을 만든다.
/// 주는 월~일, 월말에 걸친 주는 그 달 말일에서 끊고 1일부터 새 달의 1주로 센다(2026-10-14 결정).
/// </summary>
public static class SalesTrend
{
    /// <summary>LOT 하나(또는 케미칼 출고 1건)의 매출 — 금액 = 입고일 단가 × 수량(출하실적과 같다)</summary>
    public sealed record Sale(DateOnly Day, decimal Amount, decimal Qty, string Group, string Customer, string Line,
        string ItemId, string ItemName, string Eq, string Type);

    /// <summary>영업 구분 하나 — Parent 는 소계 이름(SEC·OTHER 등), Codes 가 비면("*") 나머지 고객사 전부</summary>
    public sealed record SalesGroup(string Parent, string Name, IReadOnlyList<string> Codes)
    {
        public bool IsRest => Codes.Count == 0;
    }

    /// <summary>기본 구분 — 영업 차트 기준. SEMES 등을 따로 뺄 때는 LegacyMes:SalesGroups 에 "OTHER:SEMES=10023;" 을 기타 앞에 넣는다.</summary>
    public const string DefaultGroups = "SEC:Memory=10001;SEC:Foundry=10002;OTHER:A급 Quartz=11008,11009;OTHER:기타=*";

    /// <summary>"부모:이름=코드,코드;…" 를 읽는다. "*" 는 나머지 전부. 나머지 구분이 없으면 맨 뒤에 "기타" 를 붙인다.</summary>
    public static List<SalesGroup> ParseGroups(string? cfg)
    {
        var list = new List<SalesGroup>();
        foreach (var part in (string.IsNullOrWhiteSpace(cfg) ? DefaultGroups : cfg).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var head = part[..eq].Trim();
            var colon = head.IndexOf(':');
            var parent = colon > 0 ? head[..colon].Trim() : "";
            var name = (colon > 0 ? head[(colon + 1)..] : head).Trim();
            if (name.Length == 0) continue;
            var codes = part[(eq + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(c => c != "*").ToList();
            list.Add(new SalesGroup(parent, name, codes));
        }
        if (!list.Any(g => g.IsRest)) list.Add(new SalesGroup(list.LastOrDefault()?.Parent ?? "", "기타", Array.Empty<string>()));
        return list;
    }

    /// <summary>고객사 코드 → 구분 이름</summary>
    public static string GroupOf(IReadOnlyList<SalesGroup> groups, string customer)
    {
        foreach (var g in groups) if (!g.IsRest && g.Codes.Contains(customer)) return g.Name;
        return groups.First(g => g.IsRest).Name;
    }

    // ── 기간 ──────────────────────────────────────────────

    /// <summary>주 한 칸 — 월~일, 단 달이 바뀌면 끊는다. No = 그 달의 몇 주째(1일이 든 주가 1주).</summary>
    public sealed record Week(DateOnly Start, DateOnly End, int Month, int No)
    {
        public string Label => $"{Month}월 {No}주";
        public int Days => End.DayNumber - Start.DayNumber + 1;
    }

    /// <summary>그 날이 든 주(월요일 또는 1일 ~ 일요일 또는 말일)</summary>
    public static Week WeekOf(DateOnly d)
    {
        var monday = d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
        var first = new DateOnly(d.Year, d.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var start = monday < first ? first : monday;
        var sunday = monday.AddDays(6);
        var end = sunday > last ? last : sunday;
        // 1일이 든 주가 1주 — 1일의 월요일부터 몇 번째 월요일인지
        var firstMonday = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        var no = (monday.DayNumber - firstMonday.DayNumber) / 7 + 1;
        return new Week(start, end, d.Month, no);
    }

    /// <summary>기준일이 든 주부터 거슬러 n 주(오래된 것부터)</summary>
    public static List<Week> RecentWeeks(DateOnly asOf, int n)
    {
        var list = new List<Week>();
        var w = WeekOf(asOf);
        for (var i = 0; i < n; i++) { list.Add(w); w = WeekOf(w.Start.AddDays(-1)); }
        list.Reverse();
        return list;
    }

    /// <summary>같은 월·일을 다른 해로 — 2월 29일처럼 없는 날은 그 달 말일</summary>
    public static DateOnly SameDay(DateOnly d, int year)
        => new(year, d.Month, Math.Min(d.Day, DateTime.DaysInMonth(year, d.Month)));

    // ── 결과 ──────────────────────────────────────────────

    public sealed record Compare(decimal Value, decimal? Base, string BaseLabel)
    {
        public decimal? Rate => Base is > 0 ? (Value - Base.Value) / Base.Value : null;
    }

    public sealed record Card(string Key, string Title, string Period, decimal Value, List<Compare> Compares, string? Note);

    public sealed record GroupRow(string Name, string Parent, bool Subtotal, decimal Mtd, decimal LyMtd, decimal Ytd, decimal LyYtd);

    public sealed record MonthSeries(int Year, decimal[] Total, Dictionary<string, decimal[]> ByGroup);

    public sealed record WeekPoint(string Label, string Start, string End, int Days, bool Partial, decimal Amount, Dictionary<string, decimal> ByGroup);

    /// <summary>날짜별 금액(1일~말일) — 누적 페이스 그래프</summary>
    public sealed record DailySeries(string Label, int Year, int Month, int Days, decimal[] Amount, int UpTo);

    public sealed record BridgeStep(string Label, decimal Value, string Kind);

    public sealed record Mover(string Key, string Name, string Sub, decimal Cur, decimal Prev, decimal CurQty, decimal PrevQty)
    {
        public decimal Diff => Cur - Prev;
    }

    public sealed record MoverSet(string Dim, string Label, List<Mover> Up, List<Mover> Down, int Count);

    public sealed record Report(string AsOf, string PeriodLabel, string LyLabel, List<string> Groups, List<Card> Cards,
        List<GroupRow> GroupRows, MonthSeries ThisYear, MonthSeries LastYear, List<WeekPoint> Weeks,
        List<DailySeries> Daily, List<BridgeStep> GroupBridge, List<BridgeStep> PriceVolume, List<MoverSet> Movers);

    public static readonly (string Key, string Label)[] Dims =
    {
        ("item", "품목"), ("line", "라인"), ("eq", "설비"), ("customer", "고객사"), ("type", "품목 유형"), ("group", "구분"),
    };

    private static (string Key, string Name, string Sub) DimOf(Sale s, string dim) => dim switch
    {
        "item" => (s.ItemId, s.ItemName.Length > 0 ? s.ItemName : s.ItemId, s.ItemId),
        "line" => (s.Line, s.Line, ""),
        "eq" => (s.Eq.Length > 0 ? s.Eq : "(설비 없음)", s.Eq.Length > 0 ? s.Eq : "(설비 없음)", s.Line),
        "customer" => (s.Customer, s.Customer, s.Group),
        "type" => (s.Type.Length > 0 ? s.Type : "-", TypeName(s.Type), ""),
        _ => (s.Group, s.Group, ""),
    };

    /// <summary>품목 유형(MAT_TYPE) 이름 — 확인된 것만, 나머지는 코드 그대로</summary>
    public static string TypeName(string t) => t switch
    {
        "B" => "B · BOAT", "N" => "N · INNER", "U" => "U · OUTER", "D" => "D · DOME", "S" => "S", "A" => "A · 부품", "P" => "P", "" => "-", _ => t,
    };

    /// <summary>
    /// 매출 트렌드를 만든다. sales 는 올해·작년 출하(같은 규칙으로 거른 것), asOf 는 기준일(이 날까지 센다).
    /// 이번 달 = 1일~기준일, 비교는 작년 같은 달 같은 날까지·지난달 같은 날까지. 올해 누계 = 1/1~기준일 vs 작년 1/1~같은 날.
    /// </summary>
    public static Report Build(IReadOnlyList<Sale> sales, DateOnly asOf, IReadOnlyList<SalesGroup> groupDefs, int topN = 8)
    {
        var y = asOf.Year;
        var groupNames = groupDefs.Select(g => g.Name).Distinct().ToList();
        decimal Sum(DateOnly from, DateOnly to, Func<Sale, bool>? f = null)
            => sales.Where(s => s.Day >= from && s.Day <= to && (f is null || f(s))).Sum(s => s.Amount);

        var monthStart = new DateOnly(y, asOf.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(y, asOf.Month);
        var monthEnd = monthStart.AddDays(daysInMonth - 1);
        var lyAsOf = SameDay(asOf, y - 1);
        var lyMonthStart = new DateOnly(y - 1, asOf.Month, 1);
        var pmStart = monthStart.AddMonths(-1);
        var pmAsOf = new DateOnly(pmStart.Year, pmStart.Month, Math.Min(asOf.Day, DateTime.DaysInMonth(pmStart.Year, pmStart.Month)));

        // ── 카드 ──
        var week = WeekOf(asOf);
        var wk = Sum(week.Start, asOf);
        var lwStart = week.Start.AddDays(-7);
        var lw = Sum(lwStart, asOf.AddDays(-7));
        var recent4 = RecentWeeks(week.Start.AddDays(-1), 4);
        var avgDaily4 = recent4.Sum(w => w.Days) is var d4 && d4 > 0 ? recent4.Sum(w => Sum(w.Start, w.End)) / d4 : 0;
        var wkDays = asOf.DayNumber - week.Start.DayNumber + 1;

        var mtd = Sum(monthStart, asOf);
        var lyMtd = Sum(lyMonthStart, lyAsOf);
        var pmMtd = Sum(pmStart, pmAsOf);
        var lyMonth = Sum(lyMonthStart, lyMonthStart.AddDays(DateTime.DaysInMonth(y - 1, asOf.Month) - 1));
        var elapsed = asOf.Day;
        var forecast = elapsed >= daysInMonth ? mtd : Math.Round(mtd / elapsed * daysInMonth);

        var ytd = Sum(new DateOnly(y, 1, 1), asOf);
        var lyYtd = Sum(new DateOnly(y - 1, 1, 1), lyAsOf);

        string D(DateOnly d) => d.ToString("M/d", CultureInfo.InvariantCulture);
        var cards = new List<Card>
        {
            new("week", "이번 주", $"{week.Label} ({D(week.Start)}~{D(asOf)})", wk, new()
            {
                new(wk, lw, $"지난주 같은 요일까지 ({D(lwStart)}~{D(asOf.AddDays(-7))})"),
                new(wk, avgDaily4 * wkDays, $"최근 4주 일평균 × {wkDays}일"),
            }, null),
            new("mtd", "이번 달 누계", $"{asOf.Month}월 1일~{asOf.Day}일", mtd, new()
            {
                new(mtd, lyMtd, $"작년 {asOf.Month}월 같은 날까지"),
                new(mtd, pmMtd, $"지난달 같은 날까지 ({pmStart.Month}/1~{pmAsOf.Month}/{pmAsOf.Day})"),
            }, null),
            new("forecast", "이번 달 예상", elapsed >= daysInMonth ? $"{asOf.Month}월 확정" : $"{asOf.Month}월 말 예상", forecast, new()
            {
                new(forecast, lyMonth, $"작년 {asOf.Month}월 전체"),
            }, elapsed >= daysInMonth ? null : $"지금까지 일평균({elapsed}일) × {daysInMonth}일"),
            new("ytd", "올해 누계", $"1월 1일~{asOf.Month}월 {asOf.Day}일", ytd, new()
            {
                new(ytd, lyYtd, "작년 같은 기간"),
            }, null),
        };

        // ── 구분 표(소계 포함) ──
        var groupRows = new List<GroupRow>();
        GroupRow RowOf(string name, string parent, bool sub, Func<Sale, bool> f) => new(name, parent, sub,
            Sum(monthStart, asOf, f), Sum(lyMonthStart, lyAsOf, f), Sum(new DateOnly(y, 1, 1), asOf, f), Sum(new DateOnly(y - 1, 1, 1), lyAsOf, f));
        foreach (var parent in groupDefs.Select(g => g.Parent).Distinct())
        {
            var names = groupDefs.Where(g => g.Parent == parent).Select(g => g.Name).ToHashSet();
            foreach (var n in names) groupRows.Add(RowOf(n, parent, false, s => s.Group == n));
            if (parent.Length > 0 && names.Count > 1) groupRows.Add(RowOf($"{parent} 소계", parent, true, s => names.Contains(s.Group)));
        }

        // ── 월 추이 ──
        MonthSeries Months(int year)
        {
            var total = new decimal[12];
            var by = groupNames.ToDictionary(g => g, _ => new decimal[12]);
            foreach (var s in sales.Where(s => s.Day.Year == year && (year < y || s.Day <= asOf)))
            {
                total[s.Day.Month - 1] += s.Amount;
                if (by.TryGetValue(s.Group, out var arr)) arr[s.Day.Month - 1] += s.Amount;
            }
            return new MonthSeries(year, total, by);
        }

        // ── 주 추이(최근 12주) ──
        var weeks = RecentWeeks(asOf, 12).Select(w =>
        {
            var to = w.End > asOf ? asOf : w.End;
            var xs = sales.Where(s => s.Day >= w.Start && s.Day <= to).ToList();
            return new WeekPoint(w.Label, w.Start.ToString("yyyy-MM-dd"), w.End.ToString("yyyy-MM-dd"), w.Days, w.End > asOf,
                xs.Sum(s => s.Amount), groupNames.ToDictionary(g => g, g => xs.Where(s => s.Group == g).Sum(s => s.Amount)));
        }).ToList();

        // ── 이번 달 날짜별(누적 페이스) — 이번 달·작년 같은 달·지난달 ──
        DailySeries Daily(string label, int year, int month, int upTo)
        {
            var days = DateTime.DaysInMonth(year, month);
            var arr = new decimal[days];
            foreach (var s in sales.Where(s => s.Day.Year == year && s.Day.Month == month && s.Day.Day <= upTo)) arr[s.Day.Day - 1] += s.Amount;
            return new DailySeries(label, year, month, days, arr, Math.Min(upTo, days));
        }
        var daily = new List<DailySeries>
        {
            Daily($"{y}년 {asOf.Month}월", y, asOf.Month, asOf.Day),
            Daily($"{y - 1}년 {asOf.Month}월", y - 1, asOf.Month, 31),
            Daily($"{pmStart.Year}년 {pmStart.Month}월", pmStart.Year, pmStart.Month, 31),
        };

        // ── 증감 원인: 작년 같은 기간 → 구분별 → 이번 ──
        var groupBridge = new List<BridgeStep> { new($"작년 {asOf.Month}월(~{lyAsOf.Day}일)", lyMtd, "base") };
        foreach (var g in groupNames)
            groupBridge.Add(new(g, Sum(monthStart, asOf, s => s.Group == g) - Sum(lyMonthStart, lyAsOf, s => s.Group == g), "delta"));
        groupBridge.Add(new($"올해 {asOf.Month}월(~{asOf.Day}일)", mtd, "total"));

        // ── 물량·단가 효과(품목 단위) ──
        var cur = sales.Where(s => s.Day >= monthStart && s.Day <= asOf).GroupBy(s => s.ItemId)
            .ToDictionary(g => g.Key, g => (Q: g.Sum(s => s.Qty), A: g.Sum(s => s.Amount)));
        var prev = sales.Where(s => s.Day >= lyMonthStart && s.Day <= lyAsOf).GroupBy(s => s.ItemId)
            .ToDictionary(g => g.Key, g => (Q: g.Sum(s => s.Qty), A: g.Sum(s => s.Amount)));
        decimal vol = 0, price = 0, added = 0, lost = 0;
        foreach (var k in cur.Keys.Union(prev.Keys))
        {
            var hasC = cur.TryGetValue(k, out var c) && c.Q > 0;
            var hasP = prev.TryGetValue(k, out var p) && p.Q > 0;
            if (hasC && hasP)
            {
                var p0 = p.A / p.Q; var p1 = c.A / c.Q;
                vol += (c.Q - p.Q) * p0;
                price += (p1 - p0) * c.Q;
            }
            else if (hasC) added += c.A;
            else if (hasP) lost -= p.A;
        }
        var priceVolume = new List<BridgeStep>
        {
            new($"작년 {asOf.Month}월(~{lyAsOf.Day}일)", lyMtd, "base"),
            new("물량 증감", Math.Round(vol), "delta"),
            new("단가 변동", Math.Round(price), "delta"),
            new("새 품목", added, "delta"),
            new("끊긴 품목", lost, "delta"),
            new($"올해 {asOf.Month}월(~{asOf.Day}일)", mtd, "total"),
        };

        // ── 증감 TOP ──
        var movers = Dims.Select(dim =>
        {
            var all = sales.Where(s => (s.Day >= monthStart && s.Day <= asOf) || (s.Day >= lyMonthStart && s.Day <= lyAsOf))
                .GroupBy(s => DimOf(s, dim.Key).Key)
                .Select(g =>
                {
                    var first = DimOf(g.First(), dim.Key);
                    var c = g.Where(s => s.Day.Year == y).ToList();
                    var p = g.Where(s => s.Day.Year == y - 1).ToList();
                    return new Mover(g.Key, first.Name, first.Sub, c.Sum(s => s.Amount), p.Sum(s => s.Amount), c.Sum(s => s.Qty), p.Sum(s => s.Qty));
                }).ToList();
            return new MoverSet(dim.Key, dim.Label,
                all.Where(m => m.Diff > 0).OrderByDescending(m => m.Diff).Take(topN).ToList(),
                all.Where(m => m.Diff < 0).OrderBy(m => m.Diff).Take(topN).ToList(), all.Count);
        }).ToList();

        return new Report(asOf.ToString("yyyy-MM-dd"), $"{y}년 {asOf.Month}월 1일~{asOf.Day}일", $"{y - 1}년 {asOf.Month}월 1일~{lyAsOf.Day}일",
            groupNames, cards, groupRows, Months(y), Months(y - 1), weeks, daily, groupBridge, priceVolume, movers);
    }
}
