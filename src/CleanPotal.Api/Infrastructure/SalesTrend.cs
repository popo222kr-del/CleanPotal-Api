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
        string ItemId, string ItemName, string Eq, string Type, string CustomerCode = "");

    /// <summary>필터 고르기 목록 — 업체(코드·이름)·라인, 조회 기간과 작년 같은 기간 금액이 큰 순</summary>
    public sealed record OptionItem(string Key, string Name, decimal Amount);
    public sealed record FilterOptions(List<OptionItem> Customers, List<OptionItem> Lines);

    /// <summary>
    /// 업체·라인 필터 — 비면 전체. 업체는 고객사 코드, 라인은 반출라인 이름. 고르기 목록은 필터 전 자료(조회 기간 + 작년 같은 기간)로 만든다.
    /// </summary>
    public static (List<Sale> Sales, FilterOptions Options) ApplyFilter(IReadOnlyList<Sale> sales, Period p,
        IReadOnlyCollection<string> customers, IReadOnlyCollection<string> lines)
    {
        var ly = p.LastYear;
        var scope = sales.Where(s => (s.Day >= p.From && s.Day <= p.To) || (s.Day >= ly.From && s.Day <= ly.To)).ToList();
        var custOpts = scope.GroupBy(s => s.CustomerCode.Length > 0 ? s.CustomerCode : s.Customer)
            .Select(g => new OptionItem(g.Key, g.First().Customer, g.Sum(s => s.Amount))).OrderByDescending(o => o.Amount).ToList();
        // 라인 목록은 고른 업체 안에서(업체를 고르면 그 업체의 라인만)
        var lineScope = customers.Count == 0 ? scope : scope.Where(s => customers.Contains(s.CustomerCode.Length > 0 ? s.CustomerCode : s.Customer)).ToList();
        var lineOpts = lineScope.GroupBy(s => s.Line).Select(g => new OptionItem(g.Key, g.Key, g.Sum(s => s.Amount)))
            .OrderByDescending(o => o.Amount).ToList();
        var filtered = sales.Where(s => (customers.Count == 0 || customers.Contains(s.CustomerCode.Length > 0 ? s.CustomerCode : s.Customer))
                                     && (lines.Count == 0 || lines.Contains(s.Line))).ToList();
        return (filtered, new FilterOptions(custOpts, lineOpts));
    }

    /// <summary>영업 구분 하나 — Parent 는 소계 이름(SEC·OTHER 등), Codes 가 비면("*") 나머지 고객사 전부</summary>
    public sealed record SalesGroup(string Parent, string Name, IReadOnlyList<string> Codes)
    {
        public bool IsRest => Codes.Count == 0;
    }

    /// <summary>
    /// 기본 구분 — 영업 업체 구분표(2026-10) 기준. 고객사 코드로 묶고, 표에 없는 업체(삼성전자 현업 PO·티티에스 등)는 기타.
    /// SEMES = 세메스 + 세메스 협력사(다원씨티에스·델타아이오·메티스·명성인스트루먼트·비즈테크·신우에이엔티·에스앤씨·와이즈테크놀로지·트라코월드·한주반도체·
    ///         TOMO·삼호테크놀로지·스프레이시스템코리아·시너스·엠피원테크·우신세미텍·유성산업)
    /// 기타 = 원익IPS·유진테크·TEL·국제엘렉트릭코리아·디에스테크노·이노디스·진성큐엔에스 등. 바꿀 때는 LegacyMes:SalesGroups 로 덮어쓴다.
    /// </summary>
    public const string DefaultGroups = "SEC:Memory=10001;SEC:Foundry=10002;OTHER:A급 Quartz=11008,11009;"
        + "OTHER:SEMES=10023,10042,10026,10027,10028,10029,10030,10031,10032,10033,10035,10047,11013,10037,10038,10054,10040,10041;"
        + "OTHER:우암신소재=10006;OTHER:제우스=10011;OTHER:기타=*";

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

    // ── 기간 ──────────────────────────────────────────────

    /// <summary>조회 기간 종류 — month(한 달), quarter(분기), range(직접 고른 날짜). 직전 기간을 고르는 방법이 다르다.</summary>
    public enum PeriodKind { Month, Quarter, Range }

    /// <summary>
    /// 조회 기간. From~End 가 고른 기간, To = 실제로 센 마지막 날(오늘이 기간 안이면 오늘). Partial = 아직 끝나지 않은 기간(예상을 낸다).
    /// </summary>
    public sealed record Period(PeriodKind Kind, DateOnly From, DateOnly End, DateOnly To)
    {
        public bool Partial => To < End;
        public int Days => End.DayNumber - From.DayNumber + 1;
        public int Elapsed => To.DayNumber - From.DayNumber + 1;

        /// <summary>작년 같은 기간(같은 월·일, 2/29 는 2/28)</summary>
        public (DateOnly From, DateOnly End, DateOnly To) LastYear => (From.AddYears(-1), End.AddYears(-1), To.AddYears(-1));

        /// <summary>직전 기간 — 달은 지난달, 분기는 지난 분기, 직접 고른 기간은 바로 앞의 같은 길이</summary>
        public (DateOnly From, DateOnly End, DateOnly To) Previous => Kind switch
        {
            // 끝난 기간이면 직전 기간도 끝까지(6월 → 5월 31일까지), 진행 중이면 같은 날까지(10/14 → 9/14)
            PeriodKind.Month => (From.AddMonths(-1), From.AddDays(-1), Partial ? Min(To.AddMonths(-1), From.AddDays(-1)) : From.AddDays(-1)),
            PeriodKind.Quarter => (From.AddMonths(-3), From.AddDays(-1), Partial ? Min(To.AddMonths(-3), From.AddDays(-1)) : From.AddDays(-1)),
            _ => (From.AddDays(-Days), From.AddDays(-1), To.AddDays(-Days)),
        };

        public string Label => Kind switch
        {
            PeriodKind.Month => $"{From.Year}년 {From.Month}월",
            PeriodKind.Quarter => $"{From.Year}년 {(From.Month - 1) / 3 + 1}분기",
            _ => From.Year == End.Year ? $"{From.Year}년 {From:M/d}~{End:M/d}" : $"{From:yyyy/M/d}~{End:yyyy/M/d}",
        };
        public string PrevName => Kind switch { PeriodKind.Month => "지난달", PeriodKind.Quarter => "지난 분기", _ => "직전 같은 길이" };

        /// <summary>
        /// 고른 기간을 만든다 — 끝이 오늘보다 뒤면 오늘까지만 센다. 시작이 오늘보다 뒤거나 끝보다 뒤면 null.
        /// </summary>
        public static Period? Make(PeriodKind kind, DateOnly from, DateOnly end, DateOnly today)
        {
            if (from > end || from > today) return null;
            return new Period(kind, from, end, end > today ? today : end);
        }
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    // ── 결과 ──────────────────────────────────────────────

    public sealed record Compare(decimal Value, decimal? Base, string BaseLabel)
    {
        public decimal? Rate => Base is > 0 ? (Value - Base.Value) / Base.Value : null;
    }

    /// <summary>카드 아래 덧붙이는 값(예: 예상의 다른 계산 방식)</summary>
    public sealed record CardLine(string Label, decimal Value);

    public sealed record Card(string Key, string Title, string Period, decimal Value, List<Compare> Compares, string? Note, List<CardLine> Lines);

    public sealed record GroupRow(string Name, string Parent, bool Subtotal, decimal Cur, decimal Ly, decimal Ytd, decimal LyYtd);

    public sealed record MonthSeries(int Year, decimal[] Total, Dictionary<string, decimal[]> ByGroup);

    public sealed record WeekPoint(string Label, string Start, string End, int Days, bool Partial, decimal Amount, Dictionary<string, decimal> ByGroup);

    /// <summary>날짜별 금액(기간 첫날부터) — 누적 페이스 그래프. UpTo = 센 날 수(그 뒤는 아직 안 온 날).</summary>
    public sealed record DailySeries(string Label, string Start, int Days, decimal[] Amount, int UpTo);

    /// <summary>일별 매출 하루 — 조회 기간의 날마다(구분별), Ly = 작년 같은 날짜, Counted = 센 날(오늘 뒤는 false)</summary>
    public sealed record DayPoint(string Date, int Dow, bool Counted, decimal Amount, Dictionary<string, decimal> ByGroup, decimal Ly);

    public sealed record BridgeStep(string Label, decimal Value, string Kind);

    public sealed record Mover(string Key, string Name, string Sub, decimal Cur, decimal Prev, decimal CurQty, decimal PrevQty)
    {
        public decimal Diff => Cur - Prev;
    }

    public sealed record MoverSet(string Dim, string Label, List<Mover> Up, List<Mover> Down, int Count);

    /// <summary>Options = 업체·라인 고르기 목록, Filtered = 필터가 걸렸는지</summary>
    public sealed record Report(string Kind, string From, string End, string To, bool Partial, string PeriodLabel, string CurLabel, string LyLabel,
        List<string> Groups, List<Card> Cards, List<GroupRow> GroupRows, MonthSeries ThisYear, MonthSeries LastYear, List<WeekPoint> Weeks,
        List<DailySeries> Daily, List<DayPoint> Days, List<BridgeStep> GroupBridge, List<BridgeStep> PriceVolume, List<MoverSet> Movers)
    {
        public FilterOptions? Options { get; init; }
        public bool Filtered { get; init; }
    }

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
    /// 기간 예상 — 끝나지 않은 기간만. 작년 패턴 = 작년 같은 기간 전체 × (올해 센 날까지 ÷ 작년 같은 날까지) — 월말 쏠림·휴일 같은 기간 안의 흐름이 반영된다.
    /// 단순 진도 = 센 날까지 ÷ 지난 날 수 × 기간 날 수. 작년 같은 날까지 매출이 없으면(비교 불가) 작년 패턴은 null.
    /// </summary>
    public static (decimal? Pattern, decimal Simple) Forecast(decimal cur, decimal lyToDate, decimal lyFull, int elapsed, int days)
    {
        var simple = elapsed <= 0 ? cur : Math.Round(cur / elapsed * days);
        decimal? pattern = lyToDate > 0 && lyFull > 0 ? Math.Round(cur * lyFull / lyToDate) : null;
        return (pattern, simple);
    }

    /// <summary>
    /// 매출 트렌드를 만든다. sales 는 필요한 해의 출하(같은 규칙으로 거른 것). 비교는 늘 같은 날짜까지 —
    /// 기간 누계 vs 작년 같은 기간(같은 날까지)·직전 기간(같은 날까지), 이번 주 vs 지난주 같은 요일까지, 올해 누계 vs 작년 같은 기간.
    /// </summary>
    public static Report Build(IReadOnlyList<Sale> sales, Period p, IReadOnlyList<SalesGroup> groupDefs, int topN = 8)
    {
        var to = p.To;
        var y = to.Year;
        var groupNames = groupDefs.Select(g => g.Name).Distinct().ToList();
        decimal Sum(DateOnly from, DateOnly end, Func<Sale, bool>? f = null)
            => sales.Where(s => s.Day >= from && s.Day <= end && (f is null || f(s))).Sum(s => s.Amount);
        string D(DateOnly d) => d.ToString("M/d", CultureInfo.InvariantCulture);

        var ly = p.LastYear;
        var pv = p.Previous;
        var cur = Sum(p.From, to);
        var lyCur = Sum(ly.From, ly.To);
        var lyFull = Sum(ly.From, ly.End);
        var prevCur = Sum(pv.From, pv.To);
        var name = p.Kind switch { PeriodKind.Month => $"{p.From.Month}월", PeriodKind.Quarter => $"{(p.From.Month - 1) / 3 + 1}분기", _ => "기간" };
        var curLabel = p.Partial ? $"{p.Label}(~{D(to)})" : p.Label;
        var lyLabel = p.Partial ? $"작년 같은 기간(~{D(ly.To)})" : "작년 같은 기간";

        // ── 카드 ──
        var week = WeekOf(to);
        var wk = Sum(week.Start, to);
        var lwStart = week.Start.AddDays(-7);
        var lw = Sum(lwStart, to.AddDays(-7));
        var recent4 = RecentWeeks(week.Start.AddDays(-1), 4);
        var d4 = recent4.Sum(w => w.Days);
        var avgDaily4 = d4 > 0 ? recent4.Sum(w => Sum(w.Start, w.End)) / d4 : 0;
        var wkDays = to.DayNumber - week.Start.DayNumber + 1;

        var ytd = Sum(new DateOnly(y, 1, 1), to);
        var lyYtd = Sum(new DateOnly(y - 1, 1, 1), to.AddYears(-1));

        // 오늘(센 마지막 날) — 지난주 같은 요일·최근 4주 같은 요일 평균과
        var dayAmt = Sum(to, to);
        var sameDow = Enumerable.Range(1, 4).Select(i => Sum(to.AddDays(-7 * i), to.AddDays(-7 * i))).ToList();
        var cards = new List<Card>
        {
            new("day", p.Partial ? "오늘" : "기간 마지막 날", $"{D(to)} ({"일월화수목금토"[(int)to.DayOfWeek]})", dayAmt, new()
            {
                new(dayAmt, sameDow[0], $"지난주 같은 요일 ({D(to.AddDays(-7))})"),
                new(dayAmt, sameDow.Average(), "최근 4주 같은 요일 평균"),
            }, p.Partial ? "오늘은 출하가 들어오는 대로 늘어납니다" : null, new()),
            new("week", "이번 주", $"{week.Label} ({D(week.Start)}~{D(to)})", wk, new()
            {
                new(wk, lw, $"지난주 같은 요일까지 ({D(lwStart)}~{D(to.AddDays(-7))})"),
                new(wk, avgDaily4 * wkDays, $"최근 4주 일평균 × {wkDays}일"),
            }, null, new()),
            new("cur", $"{name} 누계", p.Partial ? $"{D(p.From)}~{D(to)} ({p.Elapsed}/{p.Days}일)" : $"{D(p.From)}~{D(p.End)}", cur, new()
            {
                new(cur, lyCur, $"작년 같은 기간 ({D(ly.From)}~{D(ly.To)})"),
                new(cur, prevCur, $"{p.PrevName} 같은 날까지 ({D(pv.From)}~{D(pv.To)})"),
            }, null, new()),
        };
        if (p.Partial)
        {
            var (pattern, simple) = Forecast(cur, lyCur, lyFull, p.Elapsed, p.Days);
            var main = pattern ?? simple;
            cards.Add(new("forecast", $"{name} 예상", $"{D(p.End)}까지", main, new() { new(main, lyFull, $"작년 같은 기간 전체 ({D(ly.From)}~{D(ly.End)})") },
                pattern is null
                    ? $"작년 같은 날까지 매출이 없어 단순 진도로 계산(지금까지 일평균 × {p.Days}일)"
                    : $"작년 패턴 = 작년 전체 × (올해 누계 ÷ 작년 같은 날까지 누계) · 작년 이맘때 진도 {(lyCur / lyFull * 100):0}%",
                pattern is null ? new() : new() { new($"단순 진도(일평균 × {p.Days}일)", simple) }));
        }
        else
        {
            cards.Add(new("forecast", $"{name} 확정", $"{D(p.From)}~{D(p.End)}", cur, new() { new(cur, lyFull, "작년 같은 기간 전체") }, "기간이 끝나 예상 대신 확정 금액", new()));
        }
        cards.Add(new("ytd", "올해 누계", $"1/1~{D(to)}", ytd, new() { new(ytd, lyYtd, "작년 같은 기간") }, null, new()));

        // ── 구분 표(소계 포함) ──
        var groupRows = new List<GroupRow>();
        GroupRow RowOf(string n, string parent, bool sub, Func<Sale, bool> f) => new(n, parent, sub,
            Sum(p.From, to, f), Sum(ly.From, ly.To, f), Sum(new DateOnly(y, 1, 1), to, f), Sum(new DateOnly(y - 1, 1, 1), to.AddYears(-1), f));
        foreach (var parent in groupDefs.Select(g => g.Parent).Distinct())
        {
            var names = groupDefs.Where(g => g.Parent == parent).Select(g => g.Name).ToHashSet();
            foreach (var n in names) groupRows.Add(RowOf(n, parent, false, s => s.Group == n));
            if (parent.Length > 0 && names.Count > 1) groupRows.Add(RowOf($"{parent} 소계", parent, true, s => names.Contains(s.Group)));
        }

        // ── 월 추이(기간 끝 해 · 작년) ──
        MonthSeries Months(int year)
        {
            var total = new decimal[12];
            var by = groupNames.ToDictionary(g => g, _ => new decimal[12]);
            foreach (var s in sales.Where(s => s.Day.Year == year && (year < y || s.Day <= to)))
            {
                total[s.Day.Month - 1] += s.Amount;
                if (by.TryGetValue(s.Group, out var arr)) arr[s.Day.Month - 1] += s.Amount;
            }
            return new MonthSeries(year, total, by);
        }

        // ── 주 추이(기간 끝에서 거슬러 12주) ──
        var weeks = RecentWeeks(to, 12).Select(w =>
        {
            var wEnd = w.End > to ? to : w.End;
            var xs = sales.Where(s => s.Day >= w.Start && s.Day <= wEnd).ToList();
            return new WeekPoint(w.Label, w.Start.ToString("yyyy-MM-dd"), w.End.ToString("yyyy-MM-dd"), w.Days, w.End > to,
                xs.Sum(s => s.Amount), groupNames.ToDictionary(g => g, g => xs.Where(s => s.Group == g).Sum(s => s.Amount)));
        }).ToList();

        // ── 누적 페이스 — 이번 기간 · 작년 같은 기간 · 직전 기간(첫날부터 날짜별) ──
        DailySeries Daily(string label, DateOnly from, DateOnly end, DateOnly upTo)
        {
            var days = end.DayNumber - from.DayNumber + 1;
            var arr = new decimal[days];
            foreach (var s in sales.Where(s => s.Day >= from && s.Day <= upTo && s.Day <= end)) arr[s.Day.DayNumber - from.DayNumber] += s.Amount;
            return new DailySeries(label, from.ToString("yyyy-MM-dd"), days, arr, Math.Min(days, upTo.DayNumber - from.DayNumber + 1));
        }
        var prevLabel = p.Kind switch
        {
            PeriodKind.Month => $"{pv.From.Year}년 {pv.From.Month}월",
            PeriodKind.Quarter => $"{pv.From.Year}년 {(pv.From.Month - 1) / 3 + 1}분기",
            _ => $"{D(pv.From)}~{D(pv.End)}",
        };
        var daily = new List<DailySeries>
        {
            Daily(p.Label, p.From, p.End, to),
            Daily(p.Kind == PeriodKind.Range ? $"{D(ly.From)}~{D(ly.End)} (작년)" : p.Kind == PeriodKind.Month ? $"{ly.From.Year}년 {ly.From.Month}월" : $"{ly.From.Year}년 {(ly.From.Month - 1) / 3 + 1}분기", ly.From, ly.End, ly.End),
            Daily(prevLabel, pv.From, pv.End, pv.End),
        };

        // ── 일별(조회 기간 날마다) ──
        var byDay = sales.Where(s => s.Day >= p.From && s.Day <= to).GroupBy(s => s.Day).ToDictionary(g => g.Key, g => g.ToList());
        var lyByDay = sales.Where(s => s.Day >= ly.From && s.Day <= ly.End).GroupBy(s => s.Day).ToDictionary(g => g.Key, g => g.Sum(s => s.Amount));
        var dayPoints = Enumerable.Range(0, p.Days).Select(i =>
        {
            var d = p.From.AddDays(i);
            var xs = byDay.GetValueOrDefault(d) ?? new List<Sale>();
            return new DayPoint(d.ToString("yyyy-MM-dd"), (int)d.DayOfWeek, d <= to, xs.Sum(s => s.Amount),
                groupNames.ToDictionary(g => g, g => xs.Where(s => s.Group == g).Sum(s => s.Amount)), lyByDay.GetValueOrDefault(d.AddYears(-1)));
        }).ToList();

        // ── 증감 원인: 작년 같은 기간 → 구분별 → 이번 ──
        bool InCur(Sale s) => s.Day >= p.From && s.Day <= to;
        bool InLy(Sale s) => s.Day >= ly.From && s.Day <= ly.To;
        var groupBridge = new List<BridgeStep> { new(lyLabel, lyCur, "base") };
        foreach (var g in groupNames)
            groupBridge.Add(new(g, Sum(p.From, to, s => s.Group == g) - Sum(ly.From, ly.To, s => s.Group == g), "delta"));
        groupBridge.Add(new(curLabel, cur, "total"));

        // ── 물량·단가 효과(품목 단위) ──
        var curItems = sales.Where(InCur).GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => (Q: g.Sum(s => s.Qty), A: g.Sum(s => s.Amount)));
        var prevItems = sales.Where(InLy).GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => (Q: g.Sum(s => s.Qty), A: g.Sum(s => s.Amount)));
        decimal vol = 0, price = 0, added = 0, lost = 0;
        foreach (var k in curItems.Keys.Union(prevItems.Keys))
        {
            var hasC = curItems.TryGetValue(k, out var c) && c.Q > 0;
            var hasP = prevItems.TryGetValue(k, out var q) && q.Q > 0;
            if (hasC && hasP)
            {
                var p0 = q.A / q.Q; var p1 = c.A / c.Q;
                vol += (c.Q - q.Q) * p0;
                price += (p1 - p0) * c.Q;
            }
            else if (hasC) added += c.A;
            else if (hasP) lost -= q.A;
        }
        var priceVolume = new List<BridgeStep>
        {
            new(lyLabel, lyCur, "base"),
            new("물량 증감", Math.Round(vol), "delta"),
            new("단가 변동", Math.Round(price), "delta"),
            new("새 품목", added, "delta"),
            new("끊긴 품목", lost, "delta"),
            new(curLabel, cur, "total"),
        };

        // ── 증감 TOP ──
        var movers = Dims.Select(dim =>
        {
            var all = sales.Where(s => InCur(s) || InLy(s))
                .GroupBy(s => DimOf(s, dim.Key).Key)
                .Select(g =>
                {
                    var first = DimOf(g.First(), dim.Key);
                    var c = g.Where(InCur).ToList();
                    var q = g.Where(InLy).ToList();
                    return new Mover(g.Key, first.Name, first.Sub, c.Sum(s => s.Amount), q.Sum(s => s.Amount), c.Sum(s => s.Qty), q.Sum(s => s.Qty));
                }).ToList();
            return new MoverSet(dim.Key, dim.Label,
                all.Where(m => m.Diff > 0).OrderByDescending(m => m.Diff).Take(topN).ToList(),
                all.Where(m => m.Diff < 0).OrderBy(m => m.Diff).Take(topN).ToList(), all.Count);
        }).ToList();

        return new Report(p.Kind.ToString().ToLowerInvariant(), p.From.ToString("yyyy-MM-dd"), p.End.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"),
            p.Partial, p.Label, curLabel, lyLabel, groupNames, cards, groupRows, Months(y), Months(y - 1), weeks, daily, dayPoints, groupBridge, priceVolume, movers);
    }
}
