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
    /// <summary>작업자 사번 → 이름(MSECUSRDEF) — 30분마다 새로 읽는다. 칸 이름은 LegacyMes:UserNameColumn(기본 USER_DESC).</summary>
    private volatile IReadOnlyDictionary<string, string> _users = new Dictionary<string, string>();
    private readonly string _userNameCol;
    /// <summary>케미칼 출고의 기본 품목 — LegacyMes:ChemicalMatId(기본 900050011 SEMES_SD_1 소분)</summary>
    private readonly string _chemMat;
    /// <summary>고객사 구분(줄 순서) — LegacyMes:CustomerGroups "10001=MEMORY,10002=FOUNDRY". 나머지는 '기타세정'.</summary>
    public IReadOnlyList<(string Code, string Name)> CustomerGroups { get; }
    public const string OtherGroup = "기타세정";
    /// <summary>매출 트렌드의 영업 구분 — LegacyMes:SalesGroups "SEC:Memory=10001;SEC:Foundry=10002;OTHER:A급 Quartz=11008,11009;OTHER:기타=*"</summary>
    public IReadOnlyList<SalesTrend.SalesGroup> SalesGroups { get; }

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
        SalesGroups = SalesTrend.ParseGroups(cfg["LegacyMes:SalesGroups"]);
        _keyCol = SafeColumn(cfg["LegacyMes:CustomerKeyColumn"], "KEY_1");
        _userNameCol = SafeColumn(cfg["LegacyMes:UserNameColumn"], "USER_DESC");
        _chemMat = string.IsNullOrWhiteSpace(cfg["LegacyMes:ChemicalMatId"]) ? "900050011" : cfg["LegacyMes:ChemicalMatId"]!.Trim();
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

    /// <summary>
    /// MES 공통 코드표의 라인 코드(MGCMTBLDAT, TABLE_NAME='LINE' 의 KEY_1). 기존 MES 출하실적 화면은 이 표에 없는 라인의 LOT 을 빼고 센다
    /// (2026-01 대조: ZEUS 라인 5건 2,120,000원이 화면 합계에서 빠짐). 못 읽으면 null — 화면은 'MES 와 다른 이유' 칸을 숨긴다.
    /// </summary>
    public Task<HashSet<string>?> LineCodesAsync(CancellationToken ct = default)
        => Cached("linecodes", TimeSpan.FromMinutes(30), async () =>
        {
            try
            {
                var rows = await QueryAsync("SELECT TOP (@max) KEY_1 FROM MGCMTBLDAT WITH (NOLOCK) WHERE FACTORY = 'AF01' AND TABLE_NAME = 'LINE'",
                    new() { ["@max"] = 20_000 }, r => S(r, 0), ct);
                var set = rows.Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal);
                return set.Count > 0 ? set : null;
            }
            catch (LegacyMesUnavailableException) { return (HashSet<string>?)null; }
        });

    /// <summary>작업자 이름을 MES 사용자 표(MSECUSRDEF)에서 읽어 둔다. 못 읽으면 사번 그대로 보인다.</summary>
    public async Task LoadUsersAsync(CancellationToken ct = default)
    {
        if (!Configured || _cache.TryGetValue("legacy:users-loaded", out _)) return;
        _cache.Set("legacy:users-loaded", true, TimeSpan.FromMinutes(30));
        try
        {
            var sql = $"SELECT TOP (@max) USER_ID, {_userNameCol} FROM MSECUSRDEF WITH (NOLOCK)";
            var rows = await QueryAsync(sql, new Dictionary<string, object> { ["@max"] = 50_000 }, r => (S(r, 0), S(r, 1)), ct);
            _users = rows.Where(x => x.Item1.Length > 0 && x.Item2.Length > 0).GroupBy(x => x.Item1).ToDictionary(g => g.Key, g => g.First().Item2);
        }
        catch (LegacyMesUnavailableException) { /* 사번 그대로 */ }
    }

    public string UserName(string id) => id.Length == 0 ? "" : _users.TryGetValue(id, out var n) ? n : id;

    /// <summary>LOT 한 개 — 기본 정보 + 세정 이력 칸</summary>
    public async Task<LegacyLot?> LotAsync(string lotId, CancellationToken ct = default)
        => (await LotsAsync("A.LOT_ID = @lot", new() { ["@lot"] = lotId }, ct, steps: true)).FirstOrDefault();

    /// <summary>
    /// LOT 현황 조회 — LOT ID · S/N(반출·반입) · 반출번호 중 넣은 것으로 찾는다(기간 없이). 앞부분 일치라 키 색인을 탄다.
    /// 반출번호는 "C0204899013-1" 처럼 순번까지 넣으면 그 한 줄만.
    /// </summary>
    public Task<List<LegacyLot>> FindLotsAsync(string? lotId, string? sn, string? outNo, CancellationToken ct = default)
    {
        var args = new Dictionary<string, object>();
        var where = new List<string> { "A.FACTORY IN ('AF01', 'CUSTOMER')" };
        if (!string.IsNullOrWhiteSpace(lotId)) { args["@lot"] = EscapeLike(lotId.Trim()) + "%"; where.Add("A.LOT_ID LIKE @lot"); }
        if (!string.IsNullOrWhiteSpace(sn)) { args["@sn"] = EscapeLike(sn.Trim()) + "%"; where.Add("(A.LOT_CMF_1 LIKE @sn OR A.LOT_CMF_14 LIKE @sn)"); }
        if (!string.IsNullOrWhiteSpace(outNo))
        {
            var t = outNo.Trim();
            var dash = t.LastIndexOf('-');
            if (dash > 0 && dash < t.Length - 1 && t[(dash + 1)..].All(char.IsDigit))
            {
                args["@outno"] = t[..dash]; args["@outseq"] = t[(dash + 1)..];
                where.Add("A.LOT_CMF_4 = @outno AND A.LOT_CMF_5 = @outseq");
            }
            else { args["@outno"] = EscapeLike(t) + "%"; where.Add("A.LOT_CMF_4 LIKE @outno"); }
        }
        if (args.Count == 0) return Task.FromResult(new List<LegacyLot>());
        return LotsAsync(string.Join(" AND ", where), args, ct, top: 300);
    }

    /// <summary>LOT 이력 전체(삭제 표시된 줄 제외)</summary>
    /// <summary>
    /// 같은 BATH 의 케미칼 횟수 — 세정 START 줄 LOT_CMF_19 에 횟수가 없는 LOT(약품사용 미대상·BATCH 로 묶여 진행)은
    /// 카운트를 올리지 않는다(한 BATH 에 여러 제품이 들어가도 약액 사용은 1번). 그래서 같은 설비(RES_ID)에서 가까운 시각에
    /// 숫자로 카운트된 세정 START 줄을 찾아 그 횟수를 쓴다 — 그 시각 이전(같은 시각 포함) 것 우선, 없으면 뒤(앞뒤 6시간 안).
    /// </summary>
    public async Task<(string Count, string LotId, string Time)?> BathChemCountAsync(string resId, string tranTime, string selfLot, CancellationToken ct = default)
    {
        if (resId.Length == 0 || ParseTime(tranTime) is not { } t) return null;
        var rows = await QueryAsync("""
            SELECT TOP (@max) LOT_ID, TRAN_TIME, LOT_CMF_19
            FROM MWIPLOTHIS WITH (NOLOCK)
            WHERE TRAN_CODE = 'START' AND OPER = '3000' AND RES_ID = @res AND LOT_ID <> @self
              AND TRAN_TIME >= @from AND TRAN_TIME <= @to AND ISNULL(HIST_DEL_FLAG, '') <> 'Y'
            """, new()
            {
                ["@max"] = 200, ["@res"] = resId, ["@self"] = selfLot,
                ["@from"] = t.AddHours(-6).ToString("yyyyMMddHHmmss"), ["@to"] = t.AddHours(6).ToString("yyyyMMddHHmmss"),
            }, r => (Lot: S(r, 0), Time: S(r, 1), Count: S(r, 2).Trim()), ct);
        var self = tranTime.Trim();
        var counted = rows.Where(x => int.TryParse(x.Count, out _)).ToList();
        var pick = counted.Where(x => string.CompareOrdinal(x.Time, self) <= 0).OrderByDescending(x => x.Time, StringComparer.Ordinal).FirstOrDefault();
        if (pick.Lot is null) pick = counted.OrderBy(x => x.Time, StringComparer.Ordinal).FirstOrDefault();
        return pick.Lot is null ? null : (pick.Count, pick.Lot, pick.Time);
    }

    /// <summary>BATCH 대표 LOT 의 세정 START 줄 케미칼 횟수(LOT_CMF_19) — 묶음 LOT 은 대표 LOT 기준으로 진행한다.</summary>
    public async Task<(string Count, string Time, string Res)?> LotChemCountAsync(string lotId, CancellationToken ct = default)
    {
        var rows = await QueryAsync("""
            SELECT TOP (1) LOT_CMF_19, TRAN_TIME, RES_ID
            FROM MWIPLOTHIS WITH (NOLOCK)
            WHERE LOT_ID = @lot AND TRAN_CODE = 'START' AND OPER = '3000' AND ISNULL(HIST_DEL_FLAG, '') <> 'Y'
            ORDER BY HIST_SEQ DESC
            """, new() { ["@lot"] = lotId }, r => (Count: S(r, 0).Trim(), Time: S(r, 1), Res: S(r, 2).Trim()), ct);
        return rows.Count == 0 ? null : rows[0];
    }

    public Task<List<LegacyHist>> LotHistoryAsync(string lotId, CancellationToken ct = default)
        => QueryAsync("""
            SELECT TOP (@max) HIST_SEQ, TRAN_TIME, TRAN_CODE, OPER, OLD_OPER, RES_ID, TRAN_USER_ID, TRAN_COMMENT, LOT_CMF_3, LOT_CMF_19, LOT_CMF_18, TRAN_CMF_2
            FROM MWIPLOTHIS WITH (NOLOCK)
            WHERE LOT_ID = @lot AND ISNULL(HIST_DEL_FLAG, '') <> 'Y'
            ORDER BY HIST_SEQ
            """, new() { ["@max"] = 2000, ["@lot"] = lotId },
            r => new LegacyHist(S(r, 0), S(r, 1), S(r, 2), S(r, 3), S(r, 4), S(r, 5), S(r, 6), S(r, 7), S(r, 8), S(r, 9), S(r, 10), S(r, 11)), ct);

    /// <summary>
    /// 측정 항목 스펙(품목별) — 기존 MES 의 품목 측정 기준 표 CEDCMATPRM(FACTORY, MAT_ID, PARAMETER_ID, OPER, VALUE_COUNT,
    /// MIN_VALUE, MAX_VALUE, VISUAL_TYPE, DISABLE_FLAG …)를 품목으로 읽는다. 칸은 이름으로 고른다(설명 칸은 없어 화면이 붙인다).
    /// 표·칸이 없거나 못 읽으면 빈 목록 — 측정값은 스펙 없이 보인다. 30분 캐시.
    /// </summary>
    public Task<List<LegacySpec>> SpecAsync(string matId, CancellationToken ct = default)
        => Cached($"spec:{matId}", TimeSpan.FromMinutes(30), async () =>
        {
            try
            {
                var rows = await QueryAsync("SELECT TOP (@max) * FROM CEDCMATPRM WITH (NOLOCK) WHERE MAT_ID = @m",
                    new() { ["@max"] = 500, ["@m"] = matId }, ReadSpec, ct);
                return rows.Where(x => x is not null).Select(x => x!).ToList();
            }
            catch (LegacyMesUnavailableException) { return new List<LegacySpec>(); }
        });

    private static readonly string[] SpecParamCols = ["PARAMETER_ID", "PARA_ID", "PARAM_ID", "CHAR_ID", "ITEM_ID"];
    private static readonly string[] SpecDescCols = ["PARAMETER_DESC", "PARA_DESC", "PARAM_DESC", "CHAR_DESC", "ITEM_DESC", "DESCRIPTION"];
    private static readonly string[] SpecMinCols = ["LSL", "SPEC_MIN", "MIN_SPEC", "MIN_VALUE", "SPEC_LOW", "LOWER_LIMIT", "LOW_LIMIT", "MIN_VAL", "MIN"];
    private static readonly string[] SpecMaxCols = ["USL", "SPEC_MAX", "MAX_SPEC", "MAX_VALUE", "SPEC_HIGH", "UPPER_LIMIT", "HIGH_LIMIT", "MAX_VAL", "MAX"];

    private static LegacySpec? ReadSpec(SqlDataReader r)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = S(r, i);
        string Pick(string[] names) => names.Select(n => row.TryGetValue(n, out var v) ? v : null).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";
        var param = Pick(SpecParamCols);
        // DISABLE_FLAG = Y — 그 품목에서 쓰지 않는 항목(예: ROUGH·THK)
        if (param.Length == 0 || (row.TryGetValue("DISABLE_FLAG", out var off) && off.Equals("Y", StringComparison.OrdinalIgnoreCase))) return null;
        return new LegacySpec(param, row.TryGetValue("OPER", out var o) ? o : "", Pick(SpecDescCols), Pick(SpecMinCols), Pick(SpecMaxCols));
    }

    /// <summary>같은 반출 S/N 의 LOT — 최근 입고 순(입고검사 이력: 이번 + 이전 회차). 세정 이력 칸 포함.</summary>
    /// <summary>
    /// 같은 부품의 세정 이력 — 반출 S/N(LOT_CMF_1) 또는 반입 S/N(LOT_CMF_14) 이 이 값인 LOT 전부(최근 입고부터).
    /// S/N 은 반입 때 바뀌면 바뀐 값(반입 S/N)을 기준으로 찾는다(2026-10-15 결정) — 부르는 쪽이 반입 S/N 을 넘긴다.
    /// </summary>
    public Task<List<LegacyLot>> SnHistoryAsync(string sn, int take, CancellationToken ct = default)
        => LotsAsync("(A.LOT_CMF_1 = @sn OR A.LOT_CMF_14 = @sn) AND A.FACTORY IN ('AF01', 'CUSTOMER')", new() { ["@sn"] = sn }, ct, steps: true, top: take);

    public Task<List<LegacyLot>> SnLotsAsync(string sn, int take, CancellationToken ct = default)
        => LotsAsync("A.LOT_CMF_1 = @sn AND A.FACTORY IN ('AF01', 'CUSTOMER')", new() { ["@sn"] = sn }, ct, steps: true, top: take);

    /// <summary>
    /// S/N 마다 최근에 출하된 LOT 몇 개(최근 것부터) — 입고검사 대기 목록의 경고 표시용(최근 5회). S/N 이 많아도 500개씩 나눠 묻는다.
    /// </summary>
    public async Task<Dictionary<string, List<(string LotId, string UseCount)>>> RecentShippedBySnAsync(IEnumerable<string> sns, int take, CancellationToken ct = default)
    {
        var all = sns.Where(x => x.Length > 0).Distinct().ToList();
        var map = new Dictionary<string, List<(string, string)>>();
        foreach (var chunk in all.Chunk(500))
        {
            var args = new Dictionary<string, object> { ["@take"] = take };
            for (var i = 0; i < chunk.Length; i++) args[$"@s{i}"] = chunk[i];
            var sql = $"""
                SELECT LOT_ID, LOT_CMF_1, LOT_CMF_2, RN FROM (
                    SELECT LOT_ID, LOT_CMF_1, LOT_CMF_2, ROW_NUMBER() OVER (PARTITION BY LOT_CMF_1 ORDER BY CREATE_TIME DESC) AS RN
                    FROM MWIPLOTSTS WITH (NOLOCK)
                    WHERE FACTORY = 'CUSTOMER' AND LOT_CMF_1 IN ({string.Join(", ", args.Keys.Where(k => k != "@take"))})
                ) X WHERE RN <= @take
                ORDER BY LOT_CMF_1, RN
                """;
            foreach (var (lot, sn, use) in await QueryAsync(sql, args, r => (S(r, 0), S(r, 1), S(r, 2)), ct))
            {
                if (!map.TryGetValue(sn, out var list)) map[sn] = list = new List<(string, string)>();
                list.Add((lot, use));
            }
        }
        return map;
    }

    /// <summary>여러 LOT 의 측정값(외관·무게·먼지·판정 …) — 값이 있는 VALUE_01~20 만. 500개씩.</summary>
    public async Task<List<(string LotId, LegacyEdc Edc)>> EdcForLotsAsync(IEnumerable<string> lotIds, CancellationToken ct = default)
    {
        var list = new List<(string, LegacyEdc)>();
        foreach (var chunk in lotIds.Distinct().Chunk(500))
        {
            var args = new Dictionary<string, object>();
            for (var i = 0; i < chunk.Length; i++) args[$"@l{i}"] = chunk[i];
            var sql = $"""
                SELECT LOT_ID, HIST_SEQ, PARAMETER_ID, OPER, {string.Join(", ", Enumerable.Range(1, 20).Select(i => $"VALUE_{i:00}"))}, CREATE_USER_ID, CREATE_TIME
                FROM CEDCLOTDAT WITH (NOLOCK)
                WHERE FACTORY = 'AF01' AND LOT_ID IN ({string.Join(", ", args.Keys)})
                """;
            list.AddRange(await QueryAsync(sql, args, r => (S(r, 0), new LegacyEdc(S(r, 1), S(r, 2), S(r, 3),
                Enumerable.Range(4, 20).Select(i => S(r, i)).ToList(), S(r, 24), S(r, 25))), ct));
        }
        return list;
    }

    /// <summary>LOT 측정값(CEDCLOTDAT — 입/출하 검사: WEIGHT·CHIP·SCR … ) — 값이 있는 VALUE_01~20 만</summary>
    public Task<List<LegacyEdc>> LotEdcAsync(string lotId, CancellationToken ct = default)
        => QueryAsync($"""
            SELECT TOP (@max) HIST_SEQ, PARAMETER_ID, OPER, {string.Join(", ", Enumerable.Range(1, 20).Select(i => $"VALUE_{i:00}"))}, CREATE_USER_ID, CREATE_TIME
            FROM CEDCLOTDAT WITH (NOLOCK)
            WHERE FACTORY = 'AF01' AND LOT_ID = @lot
            ORDER BY HIST_SEQ, PARAMETER_ID
            """, new() { ["@max"] = 2000, ["@lot"] = lotId },
            r => new LegacyEdc(S(r, 0), S(r, 1), S(r, 2), Enumerable.Range(3, 20).Select(i => S(r, i)).ToList(), S(r, 23), S(r, 24)), ct);

    /// <summary>고객사 코드 → 구분(MEMORY·FOUNDRY·기타세정)</summary>
    public string GroupOf(string customer)
    {
        foreach (var (code, name) in CustomerGroups) if (code == customer) return name;
        return OtherGroup;
    }

    /// <summary>누적(A) — 반출 표의 LIFE 는 비어 있고 값은 CBM_* 칸(대부분 CBM_WET)에 들어온다. 처음 값이 있는 칸.</summary>
    private const string LifeExpr = """
        COALESCE(NULLIF(LTRIM(CAST(O.LIFE AS varchar(50))), ''), CAST(O.CBM_WET AS varchar(50)), CAST(O.CBM_NF3_CLN AS varchar(50)),
                 CAST(O.CBM_HF_CLEAN AS varchar(50)), CAST(O.CBM_BOAT AS varchar(50)), CAST(O.CBM_CLF3_CLN AS varchar(50)),
                 CAST(O.CBM_INNER AS varchar(50)), CAST(O.CBM_NOZZLE AS varchar(50)), CAST(O.CBM_TRAP AS varchar(50)))
        """;

    /// <summary>
    /// 세정 이력 칸 — 이력(MWIPLOTHIS)에서 LOT 마다 키(LOT_ID, HIST_SEQ)로 찾아 이력 표 전체를 훑지 않는다. 세정 이력 조회에서만 붙인다.
    /// 값은 "시각|작업자 사번|설비" 로 묶어 MAX — 시각이 앞에 있어 같은 일이 여러 번이면 마지막 것.
    ///  입고 = CREATE · 입고검사/출하검사 = 그 공정을 끝낸 END(OLD_OPER) · 세정/DRY/BAKE = 시작 START(OPER) 와 끝 END(OLD_OPER) · 출고 = SHIP.
    /// 실제 LOT(1SB261013003P 등)과 기존 '세정 이력 조회' 화면으로 맞춰 봤다.
    /// </summary>
    public static readonly (string Key, string Cond)[] HistCols =
    [
        ("in", "H.TRAN_CODE = 'CREATE'"),
        ("insp", "H.TRAN_CODE = 'END' AND H.OLD_OPER = '2100'"),
        ("cleanS", "H.TRAN_CODE = 'START' AND H.OPER = '3000'"), ("cleanE", "H.TRAN_CODE = 'END' AND H.OLD_OPER = '3000'"),
        ("dryS", "H.TRAN_CODE = 'START' AND H.OPER = '4000'"), ("dryE", "H.TRAN_CODE = 'END' AND H.OLD_OPER = '4000'"),
        ("bakeS", "H.TRAN_CODE = 'START' AND H.OPER = '5000'"), ("bakeE", "H.TRAN_CODE = 'END' AND H.OLD_OPER = '5000'"),
        ("final", "H.TRAN_CODE = 'END' AND H.OLD_OPER = '7000'"),
        ("ship", "H.TRAN_CODE = 'SHIP'"),
    ];

    private string LotSelect(bool steps) => $"""
        SELECT TOP (@max)
            A.LOT_ID, A.MAT_ID, A.OPER, A.FACTORY, A.RWK_FLAG,
            A.LOT_CMF_1, A.LOT_CMF_2, A.LOT_CMF_4, A.LOT_CMF_5, A.LOT_CMF_6, A.LOT_CMF_7, A.LOT_CMF_8,
            A.LOT_CMF_9, A.LOT_CMF_10, A.LOT_CMF_14, A.CREATE_TIME,
            M.MAT_DESC, M.MAT_CMF_1, M.MAT_CMF_4, M.MAT_TYPE,
            O.OUTDAY, O.CIRCLENM, {LifeExpr}, M.{_matGroupCol},
            A.SHIP_TIME, A.LAST_COMMENT, O.SHIPTO, A.QTY_1{(steps ? ", " + string.Join(", ", HistCols.Select(c => "H.H_" + c.Key)) + ", H.H_cmt, E.W_IN, E.W_OUT, E.RES" : "")}
        FROM MWIPLOTSTS A WITH (NOLOCK)
        LEFT JOIN MWIPMATDEF M WITH (NOLOCK) ON M.FACTORY = 'AF01' AND M.MAT_ID = A.MAT_ID
        LEFT JOIN TB_OUT_LIST_IF O WITH (NOLOCK) ON O.OUTNO = A.LOT_CMF_4 AND O.OUTSEQ = A.LOT_CMF_5
        {(steps ? StepApply : "")}
        """;

    private static string StepApply => $"""
        OUTER APPLY (
            SELECT {string.Join(", ", HistCols.Select(c =>
                    $"MAX(CASE WHEN {c.Cond} THEN H.TRAN_TIME + '|' + ISNULL(H.TRAN_USER_ID, '') + '|' + ISNULL(H.RES_ID, '') END) AS H_{c.Key}"))},
                MAX(CASE WHEN LTRIM(ISNULL(H.TRAN_COMMENT, '')) <> '' THEN H.TRAN_TIME + '|' + H.TRAN_COMMENT END) AS H_cmt
            FROM MWIPLOTHIS H WITH (NOLOCK)
            WHERE H.LOT_ID = A.LOT_ID AND ISNULL(H.HIST_DEL_FLAG, '') <> 'Y'
        ) H
        OUTER APPLY (
            SELECT MAX(CASE WHEN E.PARAMETER_ID = 'WEIGHT' AND E.OPER = '2100' THEN E.VALUE_01 END) AS W_IN,
                   MAX(CASE WHEN E.PARAMETER_ID = 'WEIGHT' AND E.OPER NOT IN ('2100', 'RCV') THEN E.VALUE_01 END) AS W_OUT,
                   -- 세정상태 = 합부판정(RESULT) — 여러 번 했으면 마지막(HIST_SEQ 가 큰 것)
                   MAX(CASE WHEN E.PARAMETER_ID = 'RESULT' THEN RIGHT('0000000000' + CAST(E.HIST_SEQ AS varchar(10)), 10) + '|' + ISNULL(E.VALUE_01, '') END) AS RES
            FROM CEDCLOTDAT E WITH (NOLOCK)
            WHERE E.FACTORY = 'AF01' AND E.LOT_ID = A.LOT_ID AND E.PARAMETER_ID IN ('WEIGHT', 'RESULT')
        ) E
        """;

    /// <summary>지금 공장 안에 있는 LOT(재공) — FACTORY = AF01.</summary>
    public Task<List<LegacyLot>> WipAsync(CancellationToken ct = default)
        => Cached("wip", TimeSpan.FromSeconds(60), () => LotsAsync("A.FACTORY = 'AF01'", new(), ct));

    /// <summary>
    /// 재공 LOT 의 판정(측정값 합부판정 RESULT, 마지막 HIST_SEQ) — LOT ID → OK·NG·CC. 재공 목록(입·출고 현황 LOT 목록)의 판정 칸.
    /// </summary>
    public Task<Dictionary<string, string>> WipResultsAsync(CancellationToken ct = default)
        => Cached("wipresult", TimeSpan.FromSeconds(60), async () =>
        {
            const string sql = """
                SELECT TOP (@max) E.LOT_ID,
                       MAX(RIGHT('0000000000' + CAST(E.HIST_SEQ AS varchar(10)), 10) + '|' + ISNULL(E.VALUE_01, '')) AS RES
                FROM CEDCLOTDAT E WITH (NOLOCK)
                JOIN MWIPLOTSTS A WITH (NOLOCK) ON A.LOT_ID = E.LOT_ID AND A.FACTORY = 'AF01'
                WHERE E.FACTORY = 'AF01' AND E.PARAMETER_ID = 'RESULT'
                GROUP BY E.LOT_ID
                """;
            var rows = await QueryAsync(sql, new() { ["@max"] = 50_000 }, r => (S(r, 0), S(r, 1)), ct);
            return rows.Where(x => x.Item2.Contains('|'))
                .ToDictionary(x => x.Item1, x => x.Item2.Split('|', 2)[1].Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);
        });

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
        return LotsAsync(string.Join(" AND ", where), args, ct, steps: true);
    }

    /// <summary>LIKE 특수문자(% _ [)를 글자 그대로 찾게</summary>
    public static string EscapeLike(string v) => v.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    /// <summary>그 해 출하된 LOT — FACTORY = CUSTOMER, AETS 출고일(LOT_CMF_6)이 그 해.</summary>
    public Task<List<LegacyLot>> ShippedAsync(int year, CancellationToken ct = default)
        => Cached($"ship:{year}", TimeSpan.FromMinutes(5), () => LotsAsync(
            // 출하 월 = 실제 출하 시각(SHIP_TIME). 없으면 예전처럼 LOT_CMF_6(출하예정일) — SHIP_TIME 이 빈 옛 자료 대비
            "A.FACTORY = 'CUSTOMER' AND ((A.SHIP_TIME >= @y AND A.SHIP_TIME < @y1) OR (ISNULL(A.SHIP_TIME, '') = '' AND A.LOT_CMF_6 >= @y AND A.LOT_CMF_6 < @y1))",
            new() { ["@y"] = year.ToString(CultureInfo.InvariantCulture), ["@y1"] = (year + 1).ToString(CultureInfo.InvariantCulture) }, ct,
            top: ShipMaxRows));

    /// <summary>
    /// 그 해 출하 LOT 중 세정 없이 돌려준 LOT — 이력(MWIPLOTHIS)에 세정(3000) 앞에서 세정 뒤로 건너뛴 SKIP 이 있고
    /// 세정을 한 번도 시작(START 3000)하지 않은 것(예: 입고검사 NG → SKIP 2100→7000, RETAINER RECYCLE).
    /// 삼성(MEMORY·FOUNDRY) LOT 은 기존 MES 출하실적 매출에서 빠진다. 세정을 시작했다가 SKIP 한 LOT(외주가공 CRACK DOME,
    /// S1 LINE 1차세정 Membrane 등)은 기존 MES 도 매출에 넣는다 — 2026-05·07·08·09 LOT 대조로 확인(유/무상과는 상관없음).
    /// 기타세정 고객사는 SKIP 이어도 매출에 넣는다 — 거르는 곳은 출하실적(ShipDataAsync).
    /// </summary>
    public Task<HashSet<string>> ShippedSkipAsync(int year, CancellationToken ct = default)
        => Cached($"shipskip:{year}", TimeSpan.FromMinutes(5), async () =>
        {
            const string sql = """
                SELECT DISTINCT TOP (@max) H.LOT_ID
                FROM MWIPLOTHIS H WITH (NOLOCK)
                JOIN MWIPLOTSTS A WITH (NOLOCK) ON A.LOT_ID = H.LOT_ID
                WHERE H.TRAN_CODE = 'SKIP' AND ISNULL(H.HIST_DEL_FLAG, '') <> 'Y'
                  AND H.OLD_OPER <= '3000' AND H.OPER > '3000'
                  AND NOT EXISTS (SELECT 1 FROM MWIPLOTHIS S WITH (NOLOCK)
                                  WHERE S.LOT_ID = A.LOT_ID AND S.TRAN_CODE = 'START' AND S.OPER = '3000' AND ISNULL(S.HIST_DEL_FLAG, '') <> 'Y')
                  AND A.FACTORY = 'CUSTOMER'
                  AND ((A.SHIP_TIME >= @y AND A.SHIP_TIME < @y1) OR (ISNULL(A.SHIP_TIME, '') = '' AND A.LOT_CMF_6 >= @y AND A.LOT_CMF_6 < @y1))
                """;
            var rows = await QueryAsync(sql, new()
            {
                ["@max"] = ShipMaxRows, ["@y"] = year.ToString(CultureInfo.InvariantCulture), ["@y1"] = (year + 1).ToString(CultureInfo.InvariantCulture),
            }, r => S(r, 0), ct);
            return rows.ToHashSet(StringComparer.OrdinalIgnoreCase);
        });

    /// <summary>
    /// 우리 공장(AF01) 품목만 — 품목 마스터 AF01 에 없는 품목(예: AF04 의 Kioxia 시험 품목)은 모든 MES 조회에서 뺀다
    /// (재공·출하 집계, 세정 이력·LOT 현황 조회, LOT 상세, 입고검사 이력). LOT 을 읽는 곳(LotsAsync)에서 한 번에 건다.
    /// </summary>
    private const string OwnMaterial = "M.MAT_ID IS NOT NULL";

    /// <summary>한 해 출하 LOT 상한 — 재공(2만)보다 넉넉히. 넘으면 화면에 알린다.</summary>
    public const int ShipMaxRows = 150_000;

    /// <summary>
    /// 품목 단가 이력(CMATPRICE: FACTORY, MAT_ID, APPLY_DATE, MAT_PRICE) — 출하실적 금액은 출하일에 적용 중인 단가로 계산한다.
    /// 수천 건이라 통째로 읽어 30분 캐시. 못 읽으면 빈 단가표(품목 마스터 단가로 계산).
    /// </summary>
    public Task<PriceBook> PriceBookAsync(CancellationToken ct = default)
        => Cached("pricebook", TimeSpan.FromMinutes(30), async () =>
        {
            try
            {
                var rows = await QueryAsync("SELECT TOP (@max) MAT_ID, APPLY_DATE, MAT_PRICE FROM CMATPRICE WITH (NOLOCK) WHERE FACTORY = 'AF01'",
                    new() { ["@max"] = 200_000 }, r => (S(r, 0), S(r, 1), ParsePrice(S(r, 2))), ct);
                return new PriceBook(rows.Where(x => x.Item3 is not null).Select(x => (x.Item1, x.Item2, x.Item3!.Value)));
            }
            catch (LegacyMesUnavailableException) { return new PriceBook(Array.Empty<(string, string, decimal)>()); }
        });

    /// <summary>
    /// 케미칼(SD-1 소분) 출고 — 기존 MES [소분관리 › 케미칼 출고등록]의 CCHEMICALOUT(FACTORY, LOT_ID, LOT_SEQ, PAT_DATE, SERIAL_NO, OUT_DATE …).
    /// 세정 LOT 이 아니라 약품을 나눠 판 것인데, 기존 출하실적은 이것도 합친다(1건 = 수량 1, 입고일 = PAT_DATE, 출고일 = OUT_DATE).
    /// 품목은 입고 표(CCHEMICALIN)의 LOT_ID → MAT_ID, 못 읽으면 LegacyMes:ChemicalMatId(기본 900050011 SEMES_SD_1 소분).
    /// </summary>
    public Task<List<LegacyLot>> ChemicalOutAsync(int year, CancellationToken ct = default)
        => Cached($"chem:{year}", TimeSpan.FromMinutes(5), async () =>
        {
            static string Day(string col) => $"REPLACE(LEFT(CONVERT(varchar(30), {col}, 120), 10), '-', '')";
            string Sql(bool joinIn) => $"""
                SELECT TOP (@max) O.SERIAL_NO, O.LOT_ID, O.LOT_SEQ, {Day("O.PAT_DATE")}, {Day("O.OUT_DATE")},
                       M.MAT_ID, M.MAT_DESC, M.MAT_CMF_1, M.MAT_CMF_4, M.MAT_TYPE
                FROM CCHEMICALOUT O WITH (NOLOCK)
                {(joinIn ? "LEFT JOIN (SELECT LOT_ID, MAX(MAT_ID) AS MAT_ID FROM CCHEMICALIN WITH (NOLOCK) GROUP BY LOT_ID) I ON I.LOT_ID = O.LOT_ID" : "")}
                JOIN MWIPMATDEF M WITH (NOLOCK) ON M.FACTORY = 'AF01' AND M.MAT_ID = {(joinIn ? "ISNULL(I.MAT_ID, @mat)" : "@mat")}
                WHERE O.FACTORY = 'AF01' AND LEFT({Day("O.OUT_DATE")}, 4) = @yy
                """;
            var args = new Dictionary<string, object> { ["@max"] = ShipMaxRows, ["@mat"] = _chemMat, ["@yy"] = year.ToString(CultureInfo.InvariantCulture) };
            LegacyLot Map(SqlDataReader r) => new()
            {
                LotId = S(r, 0).Length > 0 ? S(r, 0) : $"{S(r, 1)}-{S(r, 2)}", OutSn = S(r, 0), InSn = S(r, 0), OutNo = S(r, 1), OutSeq = S(r, 2),
                CreateTime = S(r, 3), ShipTime = S(r, 4), MatId = S(r, 5), MatDesc = S(r, 6), Customer = S(r, 7), Price = ParsePrice(S(r, 8)), MatType = S(r, 9),
                Factory = "CUSTOMER", Qty = 1, ProcName = "케미칼", Paid = "유상", Kind = LegacyLot.KindChemical,
            };
            try { return await QueryAsync(Sql(true), args, Map, ct); }
            catch (LegacyMesUnavailableException) { return await QueryAsync(Sql(false), args, Map, ct); }   // 입고 표 칸이 다르면 기본 품목으로
        });

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

    private async Task<List<LegacyLot>> LotsAsync(string where, Dictionary<string, object> args, CancellationToken ct, bool steps = false, int? top = null)
    {
        var p = CustomerParams(out var inList);
        foreach (var kv in args) p[kv.Key] = kv.Value;
        if (top is int n) p["@max"] = n;
        var sql = $"{LotSelect(steps)}\nWHERE ({where}) AND {OwnMaterial} {inList}\nORDER BY A.CREATE_TIME DESC";
        return await QueryAsync(sql, p, r => new LegacyLot
        {
            LotId = S(r, 0), MatId = S(r, 1), Oper = S(r, 2), Factory = S(r, 3), Rework = S(r, 4),
            OutSn = S(r, 5), UseCount = S(r, 6), OutNo = S(r, 7), OutSeq = S(r, 8), DueTime = S(r, 9),
            Line = S(r, 10), ProcName = S(r, 11), PmEq = S(r, 12), Paid = S(r, 13), InSn = S(r, 14), CreateTime = S(r, 15),
            MatDesc = S(r, 16), Customer = S(r, 17), Price = ParsePrice(S(r, 18)), MatType = S(r, 19),
            SecOutDay = S(r, 20), Circle = S(r, 21), Life = S(r, 22), MatGroup = S(r, 23),
            ShipTime = S(r, 24), Comment = S(r, 25), ShipTo = S(r, 26), Qty = ParsePrice(S(r, 27)) ?? 0,
            Hist = steps ? HistCols.Select(c => c.Key).Append("cmt").Append("wIn").Append("wOut").Append("result")
                .Select((k, i) => (k, S(r, 28 + i))).Where(x => x.Item2.Length > 0).ToDictionary(x => x.k, x => x.Item2) : null,
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

    /// <summary>
    /// 반입 위치에서 라인만 — "6LINE 3층 C반입구" → "6LINE", "P1 LINE 7층 #10 Core" → "P1 LINE".
    /// 층(숫자+층)이나 # 앞까지. 그런 표시가 없으면 전체.
    /// </summary>
    public static string ShipLine(string shipTo)
    {
        var t = (shipTo ?? "").Trim();
        if (t.Length == 0) return "";
        var m = System.Text.RegularExpressions.Regex.Match(t, @"\s(B?\d+\s*층|#)");
        return (m.Success ? t[..m.Index] : t).Trim();
    }

    /// <summary>MES 시각 글자 → 초까지(14자리), 분까지(12자리). 시각이 없으면 null.</summary>
    public static DateTime? ParseTime(string s)
    {
        var digits = new string((s ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length >= 14 && DateTime.TryParseExact(digits[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return t;
        return ParseMinute(s);
    }

    /// <summary>MES 시각 글자("20261013154210", "2026-10-12 14:29:15.913") → 분까지. 시각이 없으면 null.</summary>
    public static DateTime? ParseMinute(string s)
    {
        var digits = new string((s ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length < 12) return null;
        return DateTime.TryParseExact(digits[..12], "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
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
    /// <summary>LOT_CMF_6 — 공정이 넘어갈 때마다 다시 계산되는 목표 시각(출하예정일). 출고일이 아니다.</summary>
    public string DueTime { get; init; } = "";
    /// <summary>MWIPLOTSTS.SHIP_TIME — 출하(FACTORY → CUSTOMER) 처리 시각</summary>
    public string ShipTime { get; init; } = "";
    /// <summary>출하 시각(출하실적 월 기준) — SHIP_TIME, 없으면 LOT_CMF_6.</summary>
    public string ShipDay => ShipTime.Length > 0 ? ShipTime : DueTime;
    /// <summary>AETS 출고일 — 출하된 LOT 만. SHIP_TIME, 없으면 예전처럼 LOT_CMF_6.</summary>
    public string AetsOutDay => Factory == "CUSTOMER" ? (ShipTime.Length > 0 ? ShipTime : DueTime) : "";
    public string Comment { get; init; } = "";
    public const string KindChemical = "chemical";
    /// <summary>"" = 세정 LOT, "chemical" = 케미칼(SD-1 소분) 출고</summary>
    public string Kind { get; init; } = "";
    /// <summary>LOT 안의 개수(MWIPLOTSTS.QTY_1) — 삼성 LOT 은 1, 기타세정 노즐·분석 의뢰 등은 24·60·100 처럼 여러 개</summary>
    public decimal Qty { get; init; }
    /// <summary>반입 위치 — 반출 표 TB_OUT_LIST_IF.SHIPTO("6LINE 3층 C반입구")</summary>
    public string ShipTo { get; init; } = "";
    /// <summary>SEC 반입라인 — 반입 위치의 라인 부분(출하 관리(SCM) 화면과 같다). 기존 '세정 이력 조회'는 반출라인을 두 번 보여 줬다.</summary>
    public string InLine => LegacyMesDb.ShipLine(ShipTo);
    /// <summary>세정 이력 칸(HistCols 키 → "시각|사번|설비", cmt → "시각|메모", wIn/wOut 무게) — 세정 이력 조회에서만</summary>
    public Dictionary<string, string>? Hist { get; init; }
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

/// <summary>
/// LOT 이력 한 줄. Recipe = 그 때의 LOT_CMF_3(작업 시작 줄이면 그 공정에 건 레시피 — 세정 R035·건조 DRY36 등),
/// ChemCount = 그 때의 LOT_CMF_19(세정 시작 줄이면 설비의 케미칼(약액) 사용 횟수 — 성적서 '케미칼 횟수'. 2026-10-15 확인: 1SU261013009P = 30),
/// ChemFlag = LOT_CMF_18(Y·빈칸만 들어가는 표시 칸 — 숫자 아님), Batch = TRAN_CMF_2(BATCH 로 묶여 진행했으면 대표 LOT).
/// </summary>
public sealed record LegacyHist(string Seq, string Time, string Code, string Oper, string OldOper, string Res, string User, string Comment,
    string Recipe = "", string ChemCount = "", string ChemFlag = "", string Batch = "");
/// <summary>품목 단가 이력 — 그 날짜에 적용 중인 단가(적용일이 그 날 이전·같은 것 중 가장 최근).</summary>
public sealed class PriceBook
{
    private readonly Dictionary<string, List<(string Day, decimal Price)>> _map;
    public PriceBook(IEnumerable<(string MatId, string ApplyDate, decimal Price)> rows)
        => _map = rows.Select(r => (r.MatId.Trim(), Day: Digits8(r.ApplyDate), r.Price)).Where(r => r.Item1.Length > 0 && r.Day.Length == 8)
            .GroupBy(r => r.Item1).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Day, StringComparer.Ordinal).Select(r => (r.Day, r.Price)).ToList());
    public int Count => _map.Count;

    private static string Digits8(string s) { var d = new string((s ?? "").Where(char.IsDigit).ToArray()); return d.Length >= 8 ? d[..8] : ""; }

    /// <summary>그 날짜(yyyyMMdd… 아무 형식)에 적용 중인 단가 — 단가표에 없거나 첫 적용일보다 이르면 null</summary>
    public decimal? At(string matId, string day)
    {
        var d = Digits8(day);
        if (d.Length == 0 || !_map.TryGetValue(matId.Trim(), out var list)) return null;
        decimal? hit = null;
        foreach (var (applied, price) in list) { if (string.CompareOrdinal(applied, d) <= 0) hit = price; else break; }
        return hit;
    }
}

public sealed record LegacySpec(string Param, string Oper, string Desc, string Min, string Max);
public sealed record LegacyEdc(string Seq, string Param, string Oper, List<string> Values, string User, string Time);

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

    /// <summary>창고 재고 칸 — [동탄창고 수량, 동탄창고 금액, 천안창고 수량, 천안창고 금액]</summary>
    public sealed record ShipLine(string Line, decimal[] Qty, decimal[] Amount, decimal QtyTotal, decimal AmountTotal, decimal[] Stock);
    public sealed record ShipGroup(string Key, string Name, List<ShipLine> Lines, decimal[] Qty, decimal[] Amount, decimal QtyTotal, decimal AmountTotal, decimal[] Stock);
    public sealed record ShipReport(int Year, List<ShipGroup> Groups, decimal[] Qty, decimal[] Amount, decimal QtyTotal, decimal AmountTotal,
        decimal[] Stock, int NoDate, bool Truncated)
    {
        /// <summary>업체·라인 고르기 목록(업체·라인 필터 전 자료, 라인은 고른 업체 안에서)</summary>
        public ShipOptions? Options { get; init; }
        /// <summary>기존 MES 출하실적 화면과 다른 이유 — 라인 코드표에 없는 라인·무상세정. 코드표를 못 읽으면 null.</summary>
        public MesGap? Gap { get; init; }
    }
    public sealed record ShipOption(string Key, string Name, decimal Amount);
    public sealed record ShipOptions(List<ShipOption> Customers, List<ShipOption> Lines);
    /// <summary>MES 화면에서 빠지는 라인 하나 — 고객사 이름들, LOT 수, 수량, 월별 금액</summary>
    public sealed record GapLine(string Line, string Customers, int Lots, decimal Qty, decimal[] Amount, decimal AmountTotal);
    /// <summary>
    /// NoPrice = 단가 이력(CMATPRICE)에 입고일 단가가 없는 LOT(웹은 품목 마스터 단가로 계산, MES 는 뺌 — 2026-06 SEMES HEATER 3건),
    /// Skip = 삼성 세정 SKIP(MES 화면은 뺌), Lines = 라인 코드표에 없어 MES 화면에서 빠지는 라인(SKIP 은 빼고 셈 — 한 LOT 을 두 번 세지 않게),
    /// Amount = 둘을 합친 월별 금액, FreeLots/FreeQty = 무상세정(웹은 수량에 넣음, 금액 0 → 금액 차이 없음)
    /// </summary>
    public sealed record MesGap(GapLine? Skip, GapLine? NoPrice, List<GapLine> Lines, decimal[] Amount, decimal AmountTotal, int Lots, int FreeLots, decimal FreeQty);

    /// <summary>
    /// 기존 MES 출하실적 화면과 다른 이유 — ① 라인 코드표(LINE)에 없는 라인의 LOT(MES 화면은 뺀다. 케미칼은 라인이 없으니 셈에서 뺀다)
    /// ② 무상세정 LOT(웹은 수량에 넣고 금액 0원, MES 는 뺀다). 그 해 출하만 센다.
    /// </summary>
    public static MesGap Gap(IEnumerable<LegacyLot> shipped, int year, ISet<string> lineCodes, Func<string, string> customerName, PriceBook? prices,
        Func<LegacyLot, bool>? skipped = null)
    {
        var ship = shipped.Select(l => (Lot: l, Day: LegacyMesDb.ParseDay(l.ShipDay))).Where(x => x.Day is { } d && d.Year == year).ToList();
        GapLine Make(string name, IEnumerable<(LegacyLot Lot, DateOnly? Day)> xs)
        {
            var list = xs.ToList();
            var amt = new decimal[12];
            foreach (var (lot, day) in list) amt[day!.Value.Month - 1] += UnitPrice(lot, prices, PriceDay(lot)) * QtyOf(lot);
            var custs = string.Join(", ", list.Select(x => customerName(x.Lot.Customer)).Distinct().OrderBy(x => x, StringComparer.Ordinal));
            return new GapLine(name, custs, list.Count, list.Sum(x => QtyOf(x.Lot)), amt, amt.Sum());
        }
        var skipRows = skipped is null ? new() : ship.Where(x => skipped(x.Lot)).ToList();
        var skip = skipRows.Count > 0 ? Make(SkipGap, skipRows) : null;
        bool Skip(LegacyLot l) => skipped is not null && skipped(l);
        // 한 LOT 은 한 줄에만 — SKIP → 단가 이력 없음 → 라인 코드표 순서로
        var priceRows = ship.Where(x => !Skip(x.Lot) && NoPriceHistory(x.Lot, prices)).ToList();
        var noPrice = priceRows.Count > 0 ? Make(NoPriceGap, priceRows) : null;
        var lines = ship.Where(x => x.Lot.Kind != LegacyLot.KindChemical && !lineCodes.Contains(x.Lot.Line) && !Skip(x.Lot) && !NoPriceHistory(x.Lot, prices))
            .GroupBy(x => LineOf(x.Lot)).Select(g => Make(g.Key, g))
            .OrderByDescending(x => x.AmountTotal).ThenBy(x => x.Line, StringComparer.Ordinal).ToList();
        var all = new[] { skip, noPrice }.Where(x => x is not null).Select(x => x!).Concat(lines).ToList();
        var total = new decimal[12];
        foreach (var l in all) for (var i = 0; i < 12; i++) total[i] += l.Amount[i];
        var free = ship.Where(x => IsFree(x.Lot)).ToList();
        return new MesGap(skip, noPrice, lines, total, total.Sum(), all.Sum(l => l.Lots), free.Count, free.Sum(x => QtyOf(x.Lot)));
    }
    /// <summary>
    /// 단가 이력(CMATPRICE)에 그 날 단가가 없어 웹이 품목 마스터 단가(MAT_CMF_4)로 계산한 LOT — 기존 MES 는 이런 LOT 을 출하실적에서 뺀다
    /// (2026-06 SEMES_HEATER·HEAT EXCHANGER 3건 600,000원: 단가 이력 없음, 품목 단가 200,000). 무상·케미칼·단가 0 은 금액 차이가 없어 뺀다.
    /// </summary>
    public static bool NoPriceHistory(LegacyLot l, PriceBook? prices)
        => prices is { Count: > 0 } && l.Kind != LegacyLot.KindChemical && !IsFree(l) && (l.Price ?? 0) > 0 && prices.At(l.MatId, PriceDay(l)) is null;
    public const string NoPriceGap = "단가 이력 없음";
    /// <summary>MES 차이 칸의 삼성 세정 SKIP 줄 이름</summary>
    public const string SkipGap = "삼성 미세정 SKIP";

    public const string NoLine = "(라인 없음)";
    public static string LineOf(LegacyLot l) => l.Line.Length > 0 ? l.Line : NoLine;
    /// <summary>
    /// LOT 하나의 출하 수량 = LOT 안의 개수(QTY_1, 없거나 0 이면 1). 금액 = 단가 × 이 수량 — 기존 MES 금액과 같다
    /// (2026-01 대조: 노즐 팁 60개 LOT = 단가 × 60). 기존 MES 화면의 세트 품목 0.25 표시는 따르지 않는다(2026-10-14 결정).
    /// </summary>
    public static decimal QtyOf(LegacyLot l) => l.Qty > 0 ? l.Qty : 1m;
    /// <summary>
    /// 단가를 고르는 날 = 입고일(CREATE_TIME). 기존 MES 와 같다 — 2026-01 대조: WOOAM_TUBE-TANK CIRCULATION 단가가 01-13 에 100,000 → 35,000 으로
    /// 바뀌었는데, 01-12 입고·01-14 출고 LOT 은 100,000, 01-13 입고 LOT 은 35,000 이었다.
    /// </summary>
    public static string PriceDay(LegacyLot l) => l.CreateTime.Length > 0 ? l.CreateTime : l.ShipDay;
    /// <summary>금액 = 단가 × 수량. 단가 = 그 날 단가표(CMATPRICE) 값, 없으면 품목 마스터 단가(MAT_CMF_4), 그것도 없으면 0.</summary>
    /// <summary>단가 — 무상세정 LOT 은 청구하지 않으니 0원([무상세정 포함]을 켜도 수량만 늘고 금액은 그대로)</summary>
    public static decimal UnitPrice(LegacyLot l, PriceBook? book, string day) => IsFree(l) ? 0 : book?.At(l.MatId, day) ?? l.Price ?? 0;
    /// <summary>무상세정 LOT — 청구하지 않아 기존 MES 출하실적에서 빠진다(2026-01 대조: 웹에만 있던 무상 6건)</summary>
    public static bool IsFree(LegacyLot l) => l.Paid.Contains("무상", StringComparison.Ordinal);

    /// <summary>창고 재고 칸 번호 — 동탄창고(8100) 0, 천안창고(8000) 1, 그 밖은 -1</summary>
    public static int StockSlot(LegacyLot l) => l.Oper switch { "8100" => 0, "8000" => 1, _ => -1 };

    /// <summary>
    /// 출하실적 — 구분(삼성은 MEMORY·FOUNDRY, 나머지는 고객사) → 라인(반출라인) → 월(실제 출하 시각). 옆에 지금 창고(동탄·천안) 재고.
    /// groupOf 는 (키, 이름, 순서) — 순서가 같으면 이름 순.
    /// </summary>
    public static ShipReport Shipments(IEnumerable<LegacyLot> shipped, IEnumerable<LegacyLot> stock, int year,
        Func<LegacyLot, (string Key, string Name, int Order)> groupOf, bool truncated = false, PriceBook? prices = null)
    {
        // 단가 = 입고일(AETS 입고 = CREATE_TIME)에 적용 중인 단가 — 기존 MES 와 같다(출하·창고 재고 모두)
        decimal AmountOf(LegacyLot l, int month) => UnitPrice(l, prices, PriceDay(l)) * QtyOf(l);
        var noDate = 0;
        var rows = new List<(LegacyLot Lot, int Month)>();   // Month -1 = 재고
        foreach (var l in shipped)
        {
            var d = LegacyMesDb.ParseDay(l.ShipDay);
            if (d is null || d.Value.Year != year) { noDate++; continue; }
            rows.Add((l, d.Value.Month - 1));
        }
        rows.AddRange(stock.Where(l => StockSlot(l) >= 0).Select(l => (l, -1)));

        (decimal[] Q, decimal[] A, decimal[] S) Sum(IEnumerable<(LegacyLot Lot, int Month)> xs)
        {
            var q = new decimal[12]; var a = new decimal[12]; var st = new decimal[4];
            foreach (var (lot, m) in xs)
            {
                if (m >= 0) { q[m] += QtyOf(lot); a[m] += AmountOf(lot, m); continue; }
                var slot = StockSlot(lot) * 2;
                st[slot] += QtyOf(lot); st[slot + 1] += AmountOf(lot, m);
            }
            return (q, a, st);
        }
        var groups = rows.GroupBy(x => groupOf(x.Lot).Key).Select(g =>
        {
            var info = groupOf(g.First().Lot);
            var lines = g.GroupBy(x => LineOf(x.Lot)).OrderBy(lg => lg.Key, StringComparer.Ordinal)
                .Select(lg => { var (q, a, st) = Sum(lg); return new ShipLine(lg.Key, q, a, q.Sum(), a.Sum(), st); }).ToList();
            var (gq, ga, gs) = Sum(g);
            return (info.Order, Group: new ShipGroup(info.Key, info.Name, lines, gq, ga, gq.Sum(), ga.Sum(), gs));
        }).OrderBy(x => x.Order).ThenBy(x => x.Group.Name, StringComparer.Ordinal).Select(x => x.Group).ToList();
        var (tq, ta, ts) = Sum(rows);
        return new ShipReport(year, groups, tq, ta, tq.Sum(), ta.Sum(), ts, noDate, truncated);
    }
}
