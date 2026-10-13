using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 기존 MES DB(MESplus 계열, 운영 중) 읽기 — MES 조회(입출고 현황·출하실적·제품 단가) 화면이 쓴다.
///
///  - 접속: ConnectionStrings:LegacyMes (읽기 전용 계정, appsettings.local.json 에만). 없으면 화면이 "연결 안 됨" 안내.
///  - 읽기만: 이 클래스에는 정해 둔 SELECT 만 있다. 바깥에서 SQL 을 받지 않는다.
///  - 현장 MES 를 느리게 하지 않게: 모든 표에 WITH (NOLOCK), 명령 시간 제한, 결과 건수 제한, 짧은 캐시.
///  - 칸 뜻은 사용자가 쓰던 조회 쿼리 기준(LOT_CMF_4/5 반출번호, LOT_CMF_6 AETS 출고일, LOT_CMF_7 반출라인,
///    MAT_CMF_1 고객사 코드, MAT_CMF_4 단가 …).
/// </summary>
public sealed class LegacyMesDb
{
    private readonly string? _conn;
    private readonly IMemoryCache _cache;
    /// <summary>조회할 고객사 코드(MAT_CMF_1) — LegacyMes:Customers. "*" 이거나 비어 있으면 전체 고객사.</summary>
    public IReadOnlyList<string> Customers { get; }
    public bool AllCustomers => Customers.Count == 0 || Customers.Contains("*");
    /// <summary>고객사 코드 → 이름을 설정으로 덮어쓸 때(LegacyMes:CustomerNames "10001=MEMORY,…"). 보통은 MES 공통 코드표에서 읽는다.</summary>
    public IReadOnlyDictionary<string, string> CustomerNames { get; }
    /// <summary>MES 공통 코드표(MGCMTBLDAT, TABLE_NAME='CUSTOMER')에서 읽은 이름 — 30분마다 새로 읽는다.</summary>
    private volatile IReadOnlyDictionary<string, string> _dbNames = new Dictionary<string, string>();
    /// <summary>공통 코드표의 이름 칸(LegacyMes:CustomerNameColumn, 기본 DATA_1)과 코드 칸(기본 KEY_1).</summary>
    private readonly string _nameCol, _keyCol;
    /// <summary>제품군(MAT GROUP: Accessory·QTZ Boat …)이 든 MWIPMATDEF 칸 — LegacyMes:MatGroupColumn. 비우면 MAT_TYPE.</summary>
    private readonly string _matGroupCol;
    /// <summary>고객사 구분(줄 순서) — LegacyMes:CustomerGroups "10001=MEMORY,10002=FOUNDRY". 나머지는 '기타세정'.</summary>
    public IReadOnlyList<(string Code, string Name)> CustomerGroups { get; }
    public const string OtherGroup = "기타세정";

    public const int MaxRows = 20_000;

    public LegacyMesDb(IConfiguration cfg, IMemoryCache cache)
    {
        _conn = cfg.GetConnectionString("LegacyMes");
        _cache = cache;
        Customers = Split(cfg["LegacyMes:Customers"] ?? "*");
        _nameCol = SafeColumn(cfg["LegacyMes:CustomerNameColumn"], "DATA_1");
        _matGroupCol = SafeColumn(cfg["LegacyMes:MatGroupColumn"], "MAT_TYPE");
        CustomerGroups = Split(cfg["LegacyMes:CustomerGroups"] ?? "10001=MEMORY,10002=FOUNDRY")
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2).Select(p => (p[0].Trim(), p[1].Trim())).ToList();
        _keyCol = SafeColumn(cfg["LegacyMes:CustomerKeyColumn"], "KEY_1");
        CustomerNames = Split(cfg["LegacyMes:CustomerNames"] ?? "")
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
    }

    private static List<string> Split(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public bool Configured => !string.IsNullOrWhiteSpace(_conn);
    public string CustomerName(string code)
        => CustomerNames.TryGetValue(code, out var n) ? n : _dbNames.TryGetValue(code, out var d) && d.Length > 0 ? d : code;

    /// <summary>칸 이름은 SQL 에 그대로 들어가므로 영문·숫자·밑줄만 받는다.</summary>
    private static string SafeColumn(string? v, string fallback)
        => !string.IsNullOrWhiteSpace(v) && v.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? v : fallback;

    /// <summary>
    /// 고객사 이름을 MES 공통 코드표에서 읽어 둔다(AF01, TABLE_NAME='CUSTOMER'). 못 읽으면 코드 그대로 보인다 —
    /// 칸 이름이 다르면 LegacyMes:CustomerNameColumn / CustomerKeyColumn 으로 맞춘다.
    /// </summary>
    public async Task LoadNamesAsync(CancellationToken ct = default)
    {
        if (!Configured || _cache.TryGetValue("legacy:names-loaded", out _)) return;
        _cache.Set("legacy:names-loaded", true, TimeSpan.FromMinutes(30));
        try
        {
            var sql = $"""
                SELECT TOP (@max) {_keyCol}, {_nameCol}
                FROM MGCMTBLDAT WITH (NOLOCK)
                WHERE FACTORY = 'AF01' AND TABLE_NAME = 'CUSTOMER'
                """;
            var rows = await QueryAsync(sql, new Dictionary<string, object> { ["@max"] = MaxRows }, r => (S(r, 0), S(r, 1)), ct);
            _dbNames = rows.Where(x => x.Item1.Length > 0).GroupBy(x => x.Item1).ToDictionary(g => g.Key, g => g.First().Item2);
        }
        catch (LegacyMesUnavailableException) { /* 이름 없이 코드로 보인다 — 로그는 QueryAsync 가 남긴다 */ }
    }

    /// <summary>고객사 코드 → 구분(MEMORY·FOUNDRY·기타세정)</summary>
    public string GroupOf(string customer)
    {
        foreach (var (code, name) in CustomerGroups) if (code == customer) return name;
        return OtherGroup;
    }

    private string LotSelect => $"""
        SELECT TOP (@max)
            A.LOT_ID, A.MAT_ID, A.OPER, A.FACTORY, A.RWK_FLAG,
            A.LOT_CMF_1, A.LOT_CMF_2, A.LOT_CMF_4, A.LOT_CMF_5, A.LOT_CMF_6, A.LOT_CMF_7, A.LOT_CMF_8,
            A.LOT_CMF_9, A.LOT_CMF_10, A.LOT_CMF_14, A.CREATE_TIME,
            M.MAT_DESC, M.MAT_CMF_1, M.MAT_CMF_4, M.MAT_TYPE,
            O.OUTDAY, O.CIRCLENM, O.LIFE, M.{_matGroupCol}
        FROM MWIPLOTSTS A WITH (NOLOCK)
        LEFT JOIN MWIPMATDEF M WITH (NOLOCK) ON M.FACTORY = 'AF01' AND M.MAT_ID = A.MAT_ID
        LEFT JOIN TB_OUT_LIST_IF O WITH (NOLOCK) ON O.OUTNO = A.LOT_CMF_4 AND O.OUTSEQ = A.LOT_CMF_5
        """;

    /// <summary>지금 공장 안에 있는 LOT(재공) — FACTORY = AF01.</summary>
    public Task<List<LegacyLot>> WipAsync(CancellationToken ct = default)
        => Cached("wip", TimeSpan.FromSeconds(60), () => LotsAsync("A.FACTORY = 'AF01'", new(), ct));

    /// <summary>
    /// 세정 이력 조회 — 입고일(CREATE_TIME) 기간의 LOT(사용자가 쓰던 조회 쿼리 기준). WIP(공장 안)·SHIP(출하) 고르기와
    /// 칸별 조건(포함 검색)은 SQL 안에서 거른다 — 기간이 길어도 최근 2만 건에 잘려 옛 LOT 를 놓치지 않게.
    /// </summary>
    public Task<List<LegacyLot>> LotsByCreateAsync(DateOnly from, DateOnly toInclusive, bool wip, bool ship, LotSearch? f = null, CancellationToken ct = default)
    {
        var args = new Dictionary<string, object> { ["@from"] = from.ToString("yyyyMMdd"), ["@to"] = toInclusive.AddDays(1).ToString("yyyyMMdd") };
        var factories = wip && ship ? "'AF01', 'CUSTOMER'" : ship ? "'CUSTOMER'" : "'AF01'";
        var where = new List<string> { $"A.FACTORY IN ({factories})", "A.CREATE_TIME >= @from", "A.CREATE_TIME < @to" };
        void Like(string? v, string name, params string[] cols)
        {
            if (string.IsNullOrWhiteSpace(v)) return;
            args[name] = "%" + EscapeLike(v.Trim()) + "%";
            where.Add("(" + string.Join(" OR ", cols.Select(c => $"{c} LIKE {name}")) + ")");
        }
        if (f is not null)
        {
            Like(f.Q, "@q", "A.LOT_ID", "A.MAT_ID", "M.MAT_DESC", "M.MAT_TYPE", "A.LOT_CMF_1", "A.LOT_CMF_14", "A.LOT_CMF_4");
            Like(f.Line, "@line", "A.LOT_CMF_7");
            Like(f.MatType, "@mtype", "M.MAT_TYPE");
            Like(f.MatId, "@mid", "A.MAT_ID", "M.MAT_DESC");
            Like(f.Sn, "@sn", "A.LOT_CMF_1", "A.LOT_CMF_14");
            Like(f.Proc, "@proc", "A.LOT_CMF_8");
            Like(f.OutNo, "@outno", "A.LOT_CMF_4");
            Like(f.Eq, "@eq", "A.LOT_CMF_9");
            Like(f.Circle, "@circle", "O.CIRCLENM");
            Like(f.LotId, "@lot", "A.LOT_ID");
        }
        return LotsAsync(string.Join(" AND ", where), args, ct);
    }

    /// <summary>LIKE 특수문자(% _ [)를 글자 그대로 찾게</summary>
    public static string EscapeLike(string v) => v.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    /// <summary>그 해 출하된 LOT — FACTORY = CUSTOMER, AETS 출고일(LOT_CMF_6)이 그 해.</summary>
    public Task<List<LegacyLot>> ShippedAsync(int year, CancellationToken ct = default)
        => Cached($"ship:{year}", TimeSpan.FromMinutes(5), () => LotsAsync(
            "A.FACTORY = 'CUSTOMER' AND A.LOT_CMF_6 >= @y AND A.LOT_CMF_6 < @y1",
            new() { ["@y"] = year.ToString(CultureInfo.InvariantCulture), ["@y1"] = (year + 1).ToString(CultureInfo.InvariantCulture) }, ct));

    /// <summary>제품(품목) 목록과 단가(MAT_CMF_4).</summary>
    public Task<List<LegacyMaterial>> MaterialsAsync(CancellationToken ct = default)
        => Cached("mats", TimeSpan.FromMinutes(5), async () =>
        {
            var p = CustomerParams(out var inList);
            var sql = $"""
                SELECT TOP (@max) M.MAT_ID, M.MAT_DESC, M.MAT_TYPE, M.MAT_CMF_1, M.MAT_CMF_4
                FROM MWIPMATDEF M WITH (NOLOCK)
                WHERE M.FACTORY = 'AF01' {inList}
                ORDER BY M.MAT_ID
                """;
            return await QueryAsync(sql, p, r => new LegacyMaterial(S(r, 0), S(r, 1), S(r, 2), S(r, 3), ParsePrice(S(r, 4))), ct);
        });

    private async Task<List<LegacyLot>> LotsAsync(string where, Dictionary<string, object> args, CancellationToken ct)
    {
        var p = CustomerParams(out var inList);
        foreach (var kv in args) p[kv.Key] = kv.Value;
        var sql = $"{LotSelect}\nWHERE {where} {inList}\nORDER BY A.CREATE_TIME DESC";
        return await QueryAsync(sql, p, r => new LegacyLot
        {
            LotId = S(r, 0), MatId = S(r, 1), Oper = S(r, 2), Factory = S(r, 3), Rework = S(r, 4),
            OutSn = S(r, 5), UseCount = S(r, 6), OutNo = S(r, 7), OutSeq = S(r, 8), AetsOutDay = S(r, 9),
            Line = S(r, 10), ProcName = S(r, 11), PmEq = S(r, 12), Paid = S(r, 13), InSn = S(r, 14), CreateTime = S(r, 15),
            MatDesc = S(r, 16), Customer = S(r, 17), Price = ParsePrice(S(r, 18)), MatType = S(r, 19),
            SecOutDay = S(r, 20), Circle = S(r, 21), Life = S(r, 22), MatGroup = S(r, 23),
        }, ct);
    }

    /// <summary>고객사 조건 — 전체면 빈 글, 아니면 "AND M.MAT_CMF_1 IN (@c0, …)".</summary>
    private Dictionary<string, object> CustomerParams(out string inList)
    {
        var p = new Dictionary<string, object> { ["@max"] = MaxRows };
        inList = "";
        if (AllCustomers) return p;
        var names = new List<string>();
        for (var i = 0; i < Customers.Count; i++) { p[$"@c{i}"] = Customers[i]; names.Add($"@c{i}"); }
        inList = $"AND M.MAT_CMF_1 IN ({string.Join(", ", names)})";
        return p;
    }

    /// <summary>품목에 쓰인 고객사 코드와 품목 수 — 화면의 고객사 고르기용.</summary>
    public Task<List<(string Code, int Items)>> CustomerCodesAsync(CancellationToken ct = default)
        => Cached("custs", TimeSpan.FromMinutes(10), async () =>
        {
            var p = CustomerParams(out var inList);
            var sql = $"""
                SELECT TOP (@max) M.MAT_CMF_1, COUNT(*)
                FROM MWIPMATDEF M WITH (NOLOCK)
                WHERE M.FACTORY = 'AF01' AND ISNULL(M.MAT_CMF_1, '') <> '' {inList}
                GROUP BY M.MAT_CMF_1 ORDER BY M.MAT_CMF_1
                """;
            return await QueryAsync(sql, p, r => (S(r, 0), int.TryParse(S(r, 1), out var n) ? n : 0), ct);
        });

    private async Task<List<T>> QueryAsync<T>(string sql, Dictionary<string, object> args, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        if (!Configured) throw new LegacyMesUnavailableException("기존 MES DB 연결이 설정되지 않았습니다(ConnectionStrings:LegacyMes).");
        try
        {
            await using var conn = new SqlConnection(_conn);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;\n" + sql;
            cmd.CommandTimeout = 60;
            foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var list = new List<T>();
            while (await r.ReadAsync(ct)) list.Add(map(r));
            return list;
        }
        catch (SqlException ex)
        {
            // 접속 정보는 메시지에 싣지 않는다
            Console.WriteLine($"[legacy-mes][경고] 조회 실패: {ex.Number} {ex.Message}");
            throw new LegacyMesUnavailableException($"기존 MES DB 조회에 실패했습니다({ex.Number}). 잠시 뒤 다시 시도하세요.");
        }
    }

    private async Task<T> Cached<T>(string key, TimeSpan ttl, Func<Task<T>> load)
    {
        if (_cache.TryGetValue("legacy:" + key, out T? hit) && hit is not null) return hit;
        var v = await load();
        _cache.Set("legacy:" + key, v, ttl);
        return v;
    }

    private static string S(SqlDataReader r, int i) => r.IsDBNull(i) ? "" : (Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture) ?? "").Trim();

    /// <summary>단가 글자("330,000", "330000.0") → 숫자. 읽을 수 없으면 null.</summary>
    public static decimal? ParsePrice(string s)
    {
        var t = (s ?? "").Replace(",", "").Trim();
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>MES 날짜 글자("20261012153000", "2026-10-12", "2026/10/12 …") → 날짜. 읽을 수 없으면 null.</summary>
    public static DateOnly? ParseDay(string s)
    {
        var digits = new string((s ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length < 8) return null;
        return DateOnly.TryParseExact(digits[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }
}

public sealed class LegacyMesUnavailableException(string message) : Exception(message);

/// <summary>LOT 한 줄(보내 준 조회 쿼리의 칸).</summary>
public sealed class LegacyLot
{
    public string LotId { get; init; } = "";
    public string MatId { get; init; } = "";
    public string Oper { get; init; } = "";
    public string Factory { get; init; } = "";
    public string Rework { get; init; } = "";
    public string OutSn { get; init; } = "";
    public string UseCount { get; init; } = "";
    public string OutNo { get; init; } = "";
    public string OutSeq { get; init; } = "";
    public string AetsOutDay { get; init; } = "";
    public string Line { get; init; } = "";
    public string ProcName { get; init; } = "";
    public string PmEq { get; init; } = "";
    public string Paid { get; init; } = "";
    public string InSn { get; init; } = "";
    public string CreateTime { get; init; } = "";
    public string MatDesc { get; init; } = "";
    public string Customer { get; init; } = "";
    public decimal? Price { get; init; }
    public string MatType { get; init; } = "";
    /// <summary>제품군(MAT GROUP) — 설정한 칸, 없으면 MAT_TYPE</summary>
    public string MatGroup { get; init; } = "";
    public string SecOutDay { get; init; } = "";
    public string Circle { get; init; } = "";
    public string Life { get; init; } = "";
}

/// <summary>세정 이력 조회 칸별 조건 — 모두 "포함" 검색. 빈 칸은 조건 없음.</summary>
public sealed record LotSearch(string? Q = null, string? Line = null, string? MatType = null, string? MatId = null, string? Sn = null,
    string? Proc = null, string? OutNo = null, string? Eq = null, string? Circle = null, string? LotId = null)
{
    public bool Any => new[] { Q, Line, MatType, MatId, Sn, Proc, OutNo, Eq, Circle, LotId }.Any(v => !string.IsNullOrWhiteSpace(v));
}

public sealed record LegacyMaterial(string MatId, string MatDesc, string MatType, string Customer, decimal? Price);

/// <summary>재공·출하 표 만들기(DB 와 떨어진 순수 계산 — 시험할 수 있게).</summary>
public static class LegacyMesReports
{
    /// <summary>공정 칸 순서와 이름(기존 입출고 현황 화면 기준). 목록에 없는 공정은 코드 그대로 뒤에 붙는다.</summary>
    public static readonly (string Oper, string Label)[] Opers =
    {
        ("2000", "INPUT"), ("2100", "IN INSP"), ("3000", "CLEANING"), ("4000", "DRY"), ("4100", "LASER&CO2"),
        ("5000", "BAKE"), ("6000", "REPAIR"), ("7000", "FI INSP"), ("7100", "BOXING"), ("8000", "천안창고"), ("8100", "동탄창고"),
    };

    public static string OperName(string oper, string factory = "AF01")
    {
        if (factory == "CUSTOMER") return "출하완료";
        return oper switch
        {
            "2000" => "입고", "2100" => "입고검사", "3000" => "세정", "4000" => "건조", "4100" => "LASER", "5000" => "BAKE",
            "7000" => "최종검사", "7100" => "포장", "8000" => "천안창고", "8100" => "동탄창고", _ => oper,
        };
    }

    public sealed record WipColumn(string Oper, string Label);
    public sealed record WipRow(string MatType, string MatId, string MatDesc, Dictionary<string, int> Opers, int Total, Dictionary<string, int> Lines);
    public sealed record WipReport(List<WipColumn> Columns, List<string> Lines, List<WipRow> Rows, Dictionary<string, int> OperTotals,
        int Total, Dictionary<string, int> LineTotals);

    public static WipReport Wip(IEnumerable<LegacyLot> lots)
    {
        var list = lots.ToList();
        var known = Opers.Select(o => o.Oper).ToHashSet();
        var columns = Opers.Select(o => new WipColumn(o.Oper, o.Label))
            .Concat(list.Select(l => l.Oper).Where(o => o.Length > 0 && !known.Contains(o)).Distinct().OrderBy(o => o, StringComparer.Ordinal)
                .Select(o => new WipColumn(o, o)))
            .ToList();
        var lines = list.Select(l => l.Line).Where(l => l.Length > 0).Distinct().OrderBy(l => l, StringComparer.Ordinal).ToList();
        var rows = list
            .GroupBy(l => (Type: l.MatType, l.MatId))
            .Select(g => new WipRow(g.Key.Type, g.Key.MatId, g.First().MatDesc,
                g.GroupBy(l => l.Oper).ToDictionary(x => x.Key, x => x.Count()), g.Count(),
                g.Where(l => l.Line.Length > 0).GroupBy(l => l.Line).ToDictionary(x => x.Key, x => x.Count())))
            .OrderBy(r => r.MatType, StringComparer.Ordinal).ThenBy(r => r.MatDesc, StringComparer.Ordinal).ThenBy(r => r.MatId, StringComparer.Ordinal)
            .ToList();
        return new WipReport(columns, lines, rows,
            list.GroupBy(l => l.Oper).ToDictionary(x => x.Key, x => x.Count()), list.Count,
            list.Where(l => l.Line.Length > 0).GroupBy(l => l.Line).ToDictionary(x => x.Key, x => x.Count()));
    }

    public sealed record ShipLine(string Line, decimal[] Qty, decimal[] Amount, decimal QtyTotal, decimal AmountTotal);
    public sealed record ShipCustomer(string Code, string Name, List<ShipLine> Lines, decimal[] Qty, decimal[] Amount, decimal QtyTotal, decimal AmountTotal);
    public sealed record ShipReport(int Year, List<ShipCustomer> Customers, decimal[] Qty, decimal[] Amount, decimal QtyTotal, decimal AmountTotal, int NoDate);

    /// <summary>
    /// 출하실적 — 고객사 → 반출라인 → 월(AETS 출고일 기준) 수량·금액. 수량은 LOT 수, 금액은 품목 단가(MAT_CMF_4) 합.
    /// (기존 프로그램의 0.25 같은 소수 수량 계산은 프로시저를 확인한 뒤 맞춘다.)
    /// </summary>
    public static ShipReport Shipments(IEnumerable<LegacyLot> lots, int year, Func<string, string> customerName)
    {
        var noDate = 0;
        var dated = new List<(LegacyLot Lot, int Month)>();
        foreach (var l in lots)
        {
            var d = LegacyMesDb.ParseDay(l.AetsOutDay);
            if (d is null || d.Value.Year != year) { noDate++; continue; }
            dated.Add((l, d.Value.Month - 1));
        }
        static (decimal[] Q, decimal[] A) Sum(IEnumerable<(LegacyLot Lot, int Month)> xs)
        {
            var q = new decimal[12]; var a = new decimal[12];
            foreach (var (lot, m) in xs) { q[m] += 1; a[m] += lot.Price ?? 0; }
            return (q, a);
        }
        var customers = dated.GroupBy(x => x.Lot.Customer).OrderBy(g => g.Key, StringComparer.Ordinal).Select(cg =>
        {
            var lines = cg.GroupBy(x => x.Lot.Line.Length > 0 ? x.Lot.Line : "(라인 없음)")
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(lg => { var (q, a) = Sum(lg); return new ShipLine(lg.Key, q, a, q.Sum(), a.Sum()); }).ToList();
            var (cq, ca) = Sum(cg);
            return new ShipCustomer(cg.Key, customerName(cg.Key), lines, cq, ca, cq.Sum(), ca.Sum());
        }).ToList();
        var (tq, ta) = Sum(dated);
        return new ShipReport(year, customers, tq, ta, tq.Sum(), ta.Sum(), noDate);
    }
}
