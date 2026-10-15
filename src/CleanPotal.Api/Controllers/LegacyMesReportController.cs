using System.Globalization;
using CleanPotal.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using ProductionManagement.Application.Interfaces;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// MES 조회 — 기존 MES DB(운영 중)를 읽기만 해서 입출고(재공) 현황·출하실적·제품 단가를 보여 준다.
/// 금액·단가가 있어 OFFICE 업무 조회 권한이 있어야 한다. 쓰는 동작은 없다.
/// </summary>
[ApiController]
[Route("api/mes-report")]
[Authorize(Policy = "ViewOffice")]
public class LegacyMesReportController : ControllerBase
{
    private readonly LegacyMesDb _db;
    public LegacyMesReportController(LegacyMesDb db) => _db = db;

    private async Task<ActionResult> Run(Func<Task<ActionResult>> f)
    {
        try { await _db.LoadNamesAsync(); return await f(); }
        catch (LegacyMesUnavailableException ex) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message }); }
    }

    public record StatusDto(bool Configured, List<string> Customers, Dictionary<string, string> CustomerNames, List<string> Groups);

    /// <summary>연결 여부와 고객사 목록(품목에 쓰인 코드 전체, 또는 설정에 적은 코드).</summary>
    [HttpGet("status")]
    public async Task<ActionResult<StatusDto>> Status(CancellationToken ct)
    {
        var codes = _db.AllCustomers ? new List<string>() : _db.Customers.ToList();
        await _db.LoadNamesAsync(ct);
        if (_db.Configured)
            try { codes = (await _db.CustomerCodesAsync(ct)).Select(c => c.Code).ToList(); }
            catch (LegacyMesUnavailableException) { /* 목록 없이도 화면은 연다 */ }
        return Ok(new StatusDto(_db.Configured, codes, codes.ToDictionary(c => c, _db.CustomerName),
            _db.CustomerGroups.Select(g => g.Name).Distinct().Append(LegacyMesDb.OtherGroup).ToList()));
    }

    private static bool Match(LegacyLot l, string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return true;
        var t = q.Trim();
        return l.MatId.Contains(t, StringComparison.OrdinalIgnoreCase) || l.MatDesc.Contains(t, StringComparison.OrdinalIgnoreCase)
            || l.LotId.Contains(t, StringComparison.OrdinalIgnoreCase) || l.MatType.Contains(t, StringComparison.OrdinalIgnoreCase)
            || l.OutSn.Contains(t, StringComparison.OrdinalIgnoreCase) || l.InSn.Contains(t, StringComparison.OrdinalIgnoreCase)
            || l.OutNo.Contains(t, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>재공 현황 — 지금 공장 안의 LOT 를 품목 × 공정(·반출라인)으로 센다.</summary>
    [HttpGet("wip")]
    public Task<ActionResult> Wip([FromQuery] string? q, [FromQuery] string? line, [FromQuery] string? customer, CancellationToken ct) => Run(async () =>
    {
        var lots = (await _db.WipAsync(ct)).Where(l => Match(l, q)
            && (string.IsNullOrWhiteSpace(line) || l.Line == line)
            && (string.IsNullOrWhiteSpace(customer) || l.Customer == customer));
        return Ok(LegacyMesReports.Wip(lots));
    });

    /// <summary>LOT 한 줄 — 단가는 넣지 않는다(LOT 조회 화면마다 단가가 보이면 보안상 곤란, 2026-10-15). 금액은 출하실적·제품 단가 화면에서만.</summary>
    public record LotRow(string LotId, string OutNo, string MatDesc, string OutSn, string InSn, string MatId, string Customer,
        string Rework, string Paid, string Line, string PmEq, string Circle, string ProcName, string MatType, string Life, string UseCount,
        string? AetsInDay, string Status, string? AetsOutDay, string? SecOutDay, string Group, string MatGroup, string Oper,
        string Comment, Dictionary<string, string?>? Hist, string InLine, string ShipTo);

    private LotRow Row(LegacyLot l)
    {
        var h = l.Hist is null ? null : HistFields(l.Hist, _db.UserName);
        var shipped = l.Hist is not null && l.Hist.TryGetValue("ship", out var sp) ? Day(sp.Split('|')[0]) : null;
        var cmt = l.Hist is not null && l.Hist.TryGetValue("cmt", out var c) && c.Split('|', 2) is { Length: 2 } cp ? cp[1].Trim() : l.Comment;
        return new(l.LotId, l.OutNo.Length > 0 ? $"{l.OutNo}-{l.OutSeq}" : "", l.MatDesc, l.OutSn, l.InSn, l.MatId,
            _db.CustomerName(l.Customer), l.Rework, l.Paid, l.Line, l.PmEq, l.Circle, l.ProcName, l.MatType, l.Life, l.UseCount,
            Day(l.CreateTime), LegacyMesReports.OperName(l.Oper, l.Factory), shipped ?? Day(l.AetsOutDay), Day(l.SecOutDay),
            _db.GroupOf(l.Customer), l.MatGroup, l.Oper, cmt, h, l.InLine.Length > 0 ? l.InLine : l.Line, l.ShipTo);
    }

    /// <summary>세정 이력 칸 — 화면 표의 칸 이름(tIn·uIn·cleanEq·cleanIn …)으로 펼친다. 작업자는 이름으로.</summary>
    public static Dictionary<string, string?> HistFields(Dictionary<string, string> raw, Func<string, string> userName)
    {
        (DateTime? T, string U, string R) Part(string key)
        {
            if (!raw.TryGetValue(key, out var v)) return (null, "", "");
            var p = v.Split('|');
            return (LegacyMesDb.ParseTime(p[0]), p.Length > 1 ? userName(p[1].Trim()) : "", p.Length > 2 ? p[2].Trim() : "");
        }
        static string? Fmt(DateTime? t) => t?.ToString("yyyy-MM-dd HH:mm");
        var d = new Dictionary<string, string?>();
        void Stamp(string key, string name) { var x = Part(key); d["t" + name] = Fmt(x.T); d["u" + name] = x.U; }
        void Stage(string s, string name)
        {
            var a = Part(s + "S"); var b = Part(s + "E");
            d[name + "Eq"] = a.R.Length > 0 ? a.R : b.R;
            d[name + "In"] = Fmt(a.T); d[name + "InUser"] = a.U;
            d[name + "Out"] = Fmt(b.T); d[name + "OutUser"] = b.U;
            d[name + "Hours"] = a.T is { } i && b.T is { } o && o >= i ? (o - i).TotalHours.ToString("0.00", CultureInfo.InvariantCulture) : null;
        }
        Stamp("in", "In"); Stamp("insp", "Insp"); Stamp("final", "Final");
        Stage("clean", "clean"); Stage("dry", "dry"); Stage("bake", "bake");
        raw.TryGetValue("wIn", out var wi); raw.TryGetValue("wOut", out var wo);
        d["weightIn"] = wi; d["weightOut"] = wo;
        d["cleanState"] = raw.TryGetValue("result", out var rs) && rs.Split('|', 2) is { Length: 2 } rp ? rp[1].Trim() : null;
        d["etch"] = decimal.TryParse(wi, NumberStyles.Number, CultureInfo.InvariantCulture, out var win)
            ? (win - (decimal.TryParse(wo, NumberStyles.Number, CultureInfo.InvariantCulture, out var wout) ? wout : 0)).ToString("0.##", CultureInfo.InvariantCulture)
            : null;
        return d;
    }

    private static string? Day(string s) => LegacyMesDb.ParseDay(s)?.ToString("yyyy-MM-dd");

    /// <summary>재공 표의 칸을 누른 LOT 목록(공정·품목·라인으로 거름).</summary>
    [HttpGet("wip/lots")]
    public Task<ActionResult> WipLots([FromQuery] string? oper, [FromQuery] string? matId, [FromQuery] string? line,
        [FromQuery] string? q, [FromQuery] string? customer, CancellationToken ct) => Run(async () =>
    {
        var rows = (await _db.WipAsync(ct)).Where(l => Match(l, q)
                && (string.IsNullOrWhiteSpace(oper) || l.Oper == oper)
                && (string.IsNullOrWhiteSpace(matId) || l.MatId == matId)
                && (string.IsNullOrWhiteSpace(line) || l.Line == line)
                && (string.IsNullOrWhiteSpace(customer) || l.Customer == customer))
            .Select(Row).ToList();
        return Ok(rows);
    });

    /// <summary>
    /// 세정 이력 조회 — AETS 입고일 기간(길이 제한 없음). 결과는 최근 입고 순 최대 2만 건(넘으면 truncated).
    /// wip·ship 으로 공장 안 / 출하된 LOT 를 고른다(둘 다 끄면 둘 다).
    /// </summary>
    [HttpGet("lots")]
    public Task<ActionResult> Lots([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] string? q,
        [FromQuery] bool wip = true, [FromQuery] bool ship = true,
        [FromQuery] string? line = null, [FromQuery] string? matType = null, [FromQuery] string? matId = null, [FromQuery] string? sn = null,
        [FromQuery] string? proc = null, [FromQuery] string? outNo = null, [FromQuery] string? eq = null, [FromQuery] string? circle = null,
        [FromQuery] string? lotId = null, CancellationToken ct = default) => Run(async () =>
    {
        if (to < from) return BadRequest(new { error = "기간을 확인하세요." });
        var f = new LotSearch(q, line, matType, matId, sn, proc, outNo, eq, circle, lotId);
        if (!wip && !ship) wip = ship = true;
        await _db.LoadUsersAsync(ct);
        var lots = await _db.LotsByCreateAsync(from, to, wip, ship, f, ct);
        return Ok(new { truncated = lots.Count >= LegacyMesDb.MaxRows, rows = lots.Select(Row).ToList() });
    });

    /// <summary>LOT 현황 조회 — LOT ID · S/N · 반출번호(앞부분 일치, 기간 없음, 최대 300건).</summary>
    [HttpGet("lot-search")]
    public Task<ActionResult> LotSearch([FromQuery] string? lotId, [FromQuery] string? sn, [FromQuery] string? outNo, CancellationToken ct) => Run(async () =>
    {
        // 한글 입력기로 찍힌 바코드도 영문 키로
        lotId = KoreanKeys.ToKeys(lotId); sn = KoreanKeys.ToKeys(sn); outNo = KoreanKeys.ToKeys(outNo);
        if (new[] { lotId, sn, outNo }.All(v => string.IsNullOrWhiteSpace(v) || v.Trim().Length < 3))
            return BadRequest(new { error = "LOT ID · S/N · 반출번호 중 하나를 3글자 이상 넣으세요." });
        var rows = (await _db.FindLotsAsync(lotId, sn, outNo, ct)).Select(Row).ToList();
        return Ok(rows);
    });

    public record HistLine(string Seq, string? Time, string Code, string Oper, string OperName, string OldOper, string OldOperName,
        string Res, string User, string UserName, string Comment, string Recipe, string ChemCount, string Batch);
    public record EdcLine(string Seq, string Param, string Oper, string OperName, List<string> Values, string User, string UserName, string? Time);

    /// <summary>LOT 상세 — 기본 정보 + 이력 전체(시각·공정·설비·작업자·메모) + 측정값(입/출하 검사).</summary>
    [HttpGet("lot/{lotId}")]
    public Task<ActionResult> Lot(string lotId, CancellationToken ct) => Run(async () =>
    {
        lotId = (lotId ?? "").Trim();
        if (lotId.Length is 0 or > 40) return BadRequest(new { error = "LOT ID 를 확인하세요." });
        await _db.LoadUsersAsync(ct);
        var lot = await _db.LotAsync(lotId, ct);
        if (lot is null) return NotFound(new { error = "LOT 를 찾지 못했습니다." });
        static string? Sec(string s) => LegacyMesDb.ParseTime(s)?.ToString("yyyy-MM-dd HH:mm:ss");
        static string OpName(string o) => o.Length == 0 ? "" : LegacyMesReports.OperName(o);
        var rawHist = await _db.LotHistoryAsync(lotId, ct);
        var hist = rawHist.Select(h => new HistLine(h.Seq, Sec(h.Time), h.Code, h.Oper, OpName(h.Oper),
            h.OldOper, OpName(h.OldOper), h.Res, h.User, _db.UserName(h.User), h.Comment, h.Recipe.Trim(), h.ChemCount.Trim(), h.Batch.Trim())).ToList();
        var edc = (await _db.LotEdcAsync(lotId, ct)).Select(e => new EdcLine(e.Seq, e.Param, e.Oper, OpName(e.Oper),
            e.Values.Where(v => v.Length > 0).ToList(), e.User, _db.UserName(e.User), Sec(e.Time))).ToList();
        var spec = await _db.SpecAsync(lot.MatId, ct);
        // 작업 시작 줄에 남은 레시피 코드(R035·DRY36 …)의 READ TIME — 제품 셋업 '레시피 정의'에서 같은 코드
        var codes = hist.Where(h => h.Code == "START" && h.Recipe.Length > 0).Select(h => h.Recipe).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // 케미칼 횟수 — 세정 START 줄 LOT_CMF_19. 없으면 ① BATCH 대표 LOT(TRAN_CMF_2)의 횟수 ② 같은 BATH(설비·가까운 시각)에서 카운트된 LOT 의 횟수
        object? chem = null;
        var cleanStart = rawHist.LastOrDefault(h => h.Code == "START" && h.Oper == "3000");
        if (cleanStart is not null)
        {
            var v = cleanStart.ChemCount.Trim();
            var batch = cleanStart.Batch.Trim();
            if (int.TryParse(v, out _)) chem = new { count = v, shared = false, flag = cleanStart.ChemFlag.Trim(), via = "", fromLot = "", fromTime = (string?)null, res = cleanStart.Res, batch };
            else
            {
                (string Count, string LotId, string Time)? found = null;
                var via = "";
                if (batch.Length > 0 && !batch.Equals(lotId, StringComparison.OrdinalIgnoreCase)
                    && await _db.LotChemCountAsync(batch, ct) is { } rep && int.TryParse(rep.Count, out _))
                { found = (rep.Count, batch, rep.Time); via = "batch"; }
                if (found is null && await _db.BathChemCountAsync(cleanStart.Res.Trim(), cleanStart.Time, lotId, ct) is { } bath)
                { found = bath; via = "bath"; }
                chem = new { count = found?.Count ?? "", shared = true, flag = cleanStart.ChemFlag.Trim(), via, fromLot = found?.LotId ?? "", fromTime = Sec(found?.Time ?? ""), res = cleanStart.Res, batch };
            }
        }
        return Ok(new { lot = Row(lot), history = hist, edc, spec, recipes = await RecipeTimesAsync(lot.MatId, ct), recipeDefs = await RecipeDefsAsync(codes, ct), chem });
    });

    /// <summary>제품 셋업(기타세정 MES)의 공정별 레시피 — Oper(3000 세정·4000 건조·4100 LASER·5000 BAKE), MAIN(없으면 첫 사용) 레시피와 READ TIME(분)</summary>
    public record RecipeTime(string Oper, string Code, string Description, int? Minutes, decimal? Min, decimal? Max, int Count);

    /// <summary>
    /// 세정코드(MAT_ID)로 제품 셋업의 레시피 시간을 찾는다 — LOT 상세에서 실제 세정·건조 시간과 비교하는 기준.
    /// 제품 셋업에 없는 품목이거나 MES 모듈을 못 읽으면 빈 목록(화면은 '레시피 미등록').
    /// </summary>
    private async Task<List<RecipeTime>> RecipeTimesAsync(string matId, CancellationToken ct)
    {
        var sp = HttpContext.RequestServices;
        var products = sp.GetService<IProductService>();
        var refData = sp.GetService<IProductReferenceDataService>();
        var cache = sp.GetService<IMemoryCache>();
        if (products is null || refData is null || matId.Length == 0) return new();
        try
        {
            // 제품 목록은 5분 캐시 — LOT 상세를 열 때마다 전부 읽지 않게
            var ids = cache is null ? null : cache.Get<Dictionary<string, int>>("mesreport:productIds");
            if (ids is null)
            {
                ids = (await products.GetAllAsync(ct)).GroupBy(p => p.CleaningCode.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().ProductId, StringComparer.OrdinalIgnoreCase);
                cache?.Set("mesreport:productIds", ids, TimeSpan.FromMinutes(5));
            }
            if (!ids.TryGetValue(matId.Trim(), out var productId)) return new();
            return (await refData.GetProductRecipesAsync(productId, ct)).Where(r => r.IsActive).GroupBy(r => r.OperCode)
                .Select(g => { var m = g.FirstOrDefault(r => r.IsMain) ?? g.First(); return new RecipeTime(g.Key.ToString(CultureInfo.InvariantCulture), m.RecipeCode, m.RecipeDescription, m.ReadTimeMinutes, m.MinValue, m.MaxValue, g.Count()); })
                .OrderBy(r => r.Oper, StringComparer.Ordinal).ToList();
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return new(); }   // 레시피는 덤 — 못 읽어도 LOT 상세는 연다
    }

    public record RecipeDef(string Code, string Description, string Oper, int? Minutes);

    /// <summary>레시피 코드 → 정의(READ TIME) — 제품 셋업의 '레시피 정의'. 못 읽으면 빈 목록(화면은 코드만, DRYnn 은 nn 시간으로 읽음).</summary>
    private async Task<List<RecipeDef>> RecipeDefsAsync(IReadOnlySet<string> codes, CancellationToken ct)
    {
        var refData = HttpContext.RequestServices.GetService<IProductReferenceDataService>();
        if (refData is null || codes.Count == 0) return new();
        try
        {
            return (await refData.GetRecipesAsync(ct)).Where(r => codes.Contains(r.Code.Trim()))
                .Select(r => new RecipeDef(r.Code.Trim(), r.Description, r.OperCode.ToString(CultureInfo.InvariantCulture), r.ReadTimeMinutes)).ToList();
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return new(); }
    }

    /// <summary>출하실적 구분 — 삼성은 MEMORY·FOUNDRY(설정 순서), 나머지는 고객사 이름(뒤로)</summary>
    /// <summary>케미칼(SD-1 소분) 출고만 따로 보는 구분 키 — 표에서도 고객사 구분과 섞지 않고 맨 아래 따로 묶는다</summary>
    public const string ChemicalGroup = "chem";

    private (string Key, string Name, int Order) ShipGroupOf(LegacyLot l)
    {
        if (l.Kind == LegacyLot.KindChemical) return (ChemicalGroup, "케미칼(SD-1)", 1000);
        var g = _db.GroupOf(l.Customer);
        if (g != LegacyMesDb.OtherGroup)
        {
            var i = _db.CustomerGroups.Select(x => x.Name).Distinct().ToList().IndexOf(g);
            return (g, g, i < 0 ? 50 : i);
        }
        return ("c:" + l.Customer, _db.CustomerName(l.Customer), 100);
    }

    /// <summary>
    /// 구분 고르기 — "" 전체, "MEMORY"·"FOUNDRY" 같은 구분 이름, "기타세정" 은 삼성 밖 고객사 전부(케미칼 빼고), "chem" 은 케미칼(SD-1) 출고만
    /// </summary>
    private bool InGroupFilter(LegacyLot l, string? group)
    {
        if (string.IsNullOrWhiteSpace(group)) return true;
        if (group == ChemicalGroup) return l.Kind == LegacyLot.KindChemical;
        return l.Kind != LegacyLot.KindChemical && group == _db.GroupOf(l.Customer);
    }

    private static bool TypeOk(LegacyLot l, string? type)
        => string.IsNullOrWhiteSpace(type) || l.MatType.Contains(type.Trim(), StringComparison.OrdinalIgnoreCase)
           || l.MatDesc.Contains(type.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 출하실적 자료 — 출하 LOT(+ 케미칼 SD-1 출고) · 지금 창고 재고. 기본은 기존 MES 와 같게: 무상세정 빼고(free=false), 케미칼 넣고(chem=true).
    /// </summary>
    private async Task<(List<LegacyLot> Ship, List<LegacyLot> Stock, bool Truncated)> ShipDataAsync(int year, string? group, string? type,
        bool free, bool chem, bool skip, CancellationToken ct)
    {
        var shipped = await _db.ShippedAsync(year, ct);
        // 삼성(MEMORY·FOUNDRY) LOT 중 세정을 SKIP 한 것은 기존 MES 처럼 기본으로 뺀다. 기타세정 고객사는 SKIP 이어도 넣는다(무상만 뺌).
        var skipLots = skip ? new HashSet<string>() : await _db.ShippedSkipAsync(year, ct);
        bool SkippedSamsung(LegacyLot l) => skipLots.Contains(l.LotId) && _db.GroupOf(l.Customer) != LegacyMesDb.OtherGroup;
        // 케미칼만 고르면 [케미칼 포함] 체크와 상관없이 읽는다
        var chemical = chem || group == ChemicalGroup ? await _db.ChemicalOutAsync(year, ct) : new List<LegacyLot>();
        var stock = await _db.WipAsync(ct);
        bool Keep(LegacyLot l) => InGroupFilter(l, group) && TypeOk(l, type) && (free || !LegacyMesReports.IsFree(l));
        return (shipped.Where(l => !SkippedSamsung(l)).Concat(chemical).Where(Keep).ToList(), stock.Where(l => LegacyMesReports.StockSlot(l) >= 0 && Keep(l)).ToList(),
            shipped.Count >= LegacyMesDb.ShipMaxRows);
    }

    /// <summary>출하실적 — 연도·구분(MEMORY·FOUNDRY·기타세정)·제품 TYPE·무상 포함·케미칼 포함·삼성 세정 SKIP 포함. 월 = 실제 출하 시각, 옆에 지금 창고 재고.</summary>
    [HttpGet("shipments")]
    public Task<ActionResult> Shipments([FromQuery] int year, [FromQuery] string? group, [FromQuery] string? type,
        [FromQuery] bool free = false, [FromQuery] bool chem = true, [FromQuery] bool skip = false, CancellationToken ct = default) => Run(async () =>
    {
        if (year < 2000 || year > 2100) year = DateTime.Now.Year;
        var (ship, stock, truncated) = await ShipDataAsync(year, group, type, free, chem, skip, ct);
        var prices = await _db.PriceBookAsync(ct);
        return Ok(LegacyMesReports.Shipments(ship, stock, year, ShipGroupOf, truncated, prices));
    });

    public record ShipDetailRow(string LotId, string? InDay, string? OutDay, string Line, string InLine, string PmEq, string Circle,
        string ProcName, string MatType, string MatDesc, string MatId, string Sn, decimal Qty, decimal Amount, string Customer, decimal UnitPrice, string Paid);

    /// <summary>
    /// 출하실적 숫자를 누른 목록 — 표와 같은 자료(5분 캐시)를 같은 조건으로 거른다.
    /// key = 표의 구분 키(없으면 전체), line = 라인(없으면 그 구분 전체), month = 1~12(0 = 그 해 전체), stock = dongtan·cheonan(창고 재고 칸).
    /// </summary>
    [HttpGet("shipments/detail")]
    public Task<ActionResult> ShipmentDetail([FromQuery] int year, [FromQuery] string? group, [FromQuery] string? type,
        [FromQuery] string? key, [FromQuery] string? line, [FromQuery] int month, [FromQuery] string? stock,
        [FromQuery] bool free = false, [FromQuery] bool chem = true, [FromQuery] bool skip = false, CancellationToken ct = default) => Run(async () =>
    {
        if (year < 2000 || year > 2100) year = DateTime.Now.Year;
        var (ship, wip, _) = await ShipDataAsync(year, group, type, free, chem, skip, ct);
        bool Same(LegacyLot l) => (string.IsNullOrEmpty(key) || ShipGroupOf(l).Key == key) && (string.IsNullOrEmpty(line) || LegacyMesReports.LineOf(l) == line);
        IEnumerable<LegacyLot> lots = stock switch
        {
            "dongtan" => wip.Where(l => LegacyMesReports.StockSlot(l) == 0 && Same(l)),
            "cheonan" => wip.Where(l => LegacyMesReports.StockSlot(l) == 1 && Same(l)),
            _ => ship.Where(l => Same(l) && LegacyMesDb.ParseDay(l.ShipDay) is { } d && d.Year == year && (month is < 1 or > 12 || d.Month == month)),
        };
        var prices = await _db.PriceBookAsync(ct);
        var isStock = stock is "dongtan" or "cheonan";
        var rows = lots.OrderBy(l => l.ShipDay).ThenBy(l => l.LotId).Select(l =>
        {
            var unit = LegacyMesReports.UnitPrice(l, prices, LegacyMesReports.PriceDay(l));   // 표와 같은 단가(입고일 기준)
            var q = LegacyMesReports.QtyOf(l);
            return new ShipDetailRow(l.LotId, Day(l.CreateTime), isStock ? null : Day(l.ShipDay), l.Line, l.InLine.Length > 0 ? l.InLine : l.Line,
                l.PmEq, l.Circle, l.ProcName, l.MatType, l.MatDesc, l.MatId, l.OutSn, q, unit * q, _db.CustomerName(l.Customer), unit, l.Paid);
        }).ToList();
        return Ok(rows);
    });

    /// <summary>
    /// 매출 트렌드 — 출하 기준(출하실적 기본값과 같은 규칙: 무상세정·삼성 세정 SKIP 빼고 케미칼 넣음).
    /// kind = month(month=yyyy-MM) · quarter(year, q=1~4) · range(from, to=yyyy-MM-dd). 없으면 이번 달.
    /// 끝이 오늘보다 뒤면 오늘까지만 세고 기간 예상을 낸다. 비교(작년 같은 기간·직전 기간)에 필요한 해까지 읽는다.
    /// </summary>
    [HttpGet("trend")]
    public Task<ActionResult> Trend([FromQuery] string? kind, [FromQuery] string? month, [FromQuery] int? year, [FromQuery] int? q,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string[]? customer, [FromQuery] string[]? line,
        CancellationToken ct = default) => Run(async () =>
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        SalesTrend.Period? period;
        switch (kind)
        {
            case "quarter":
                var qy = year ?? today.Year; var qn = Math.Clamp(q ?? ((today.Month - 1) / 3 + 1), 1, 4);
                if (qy < 2001 || qy > 2100) return BadRequest(new { error = "연도가 올바르지 않습니다." });
                var qs = new DateOnly(qy, (qn - 1) * 3 + 1, 1);
                period = SalesTrend.Period.Make(SalesTrend.PeriodKind.Quarter, qs, qs.AddMonths(3).AddDays(-1), today);
                break;
            case "range":
                if (from is null || to is null) return BadRequest(new { error = "시작일과 끝일을 골라 주세요." });
                if (to.Value.DayNumber - from.Value.DayNumber > 366) return BadRequest(new { error = "기간은 1년(366일)까지 볼 수 있습니다." });
                period = SalesTrend.Period.Make(SalesTrend.PeriodKind.Range, from.Value, to.Value, today);
                break;
            default:
                var ms = DateOnly.TryParseExact(month ?? "", "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)
                    ? m : new DateOnly(today.Year, today.Month, 1);
                if (ms.Year < 2001 || ms.Year > 2100) return BadRequest(new { error = "월이 올바르지 않습니다." });
                period = SalesTrend.Period.Make(SalesTrend.PeriodKind.Month, ms, ms.AddMonths(1).AddDays(-1), today);
                break;
        }
        if (period is null) return BadRequest(new { error = "아직 오지 않은 기간이거나 시작일이 끝일보다 뒤입니다." });

        // 작년 같은 기간·직전 기간·12주 추이·올해/작년 누계에 필요한 해 = 시작 해 - 1 ~ 끝 해
        var prices = await _db.PriceBookAsync(ct);
        var sales = new List<SalesTrend.Sale>();
        for (var yr = period.From.Year - 1; yr <= period.To.Year; yr++)
        {
            var (ship, _, _) = await ShipDataAsync(yr, null, null, false, true, false, ct);
            foreach (var l in ship)
            {
                if (LegacyMesDb.ParseDay(l.ShipDay) is not { } d || d.Year != yr) continue;
                var qty = LegacyMesReports.QtyOf(l);
                sales.Add(new SalesTrend.Sale(d, LegacyMesReports.UnitPrice(l, prices, LegacyMesReports.PriceDay(l)) * qty, qty,
                    SalesTrend.GroupOf(_db.SalesGroups, l.Customer), _db.CustomerName(l.Customer), LegacyMesReports.LineOf(l),
                    l.MatId, l.MatDesc, l.PmEq, l.MatType, l.Customer));
            }
        }
        // 업체·라인 필터(여러 개 고를 수 있음) — 고르기 목록은 필터 전 자료로
        var custSet = (customer ?? Array.Empty<string>()).Select(c => c.Trim()).Where(c => c.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lineSet = (line ?? Array.Empty<string>()).Select(c => c.Trim()).Where(c => c.Length > 0).ToHashSet(StringComparer.Ordinal);
        var (filtered, options) = SalesTrend.ApplyFilter(sales, period, custSet, lineSet);
        return Ok(SalesTrend.Build(filtered, period, _db.SalesGroups) with { Options = options, Filtered = custSet.Count + lineSet.Count > 0 });
    });

    public record MaterialRow(string MatId, string MatDesc, string MatType, string Customer, decimal? Price);

    /// <summary>제품(품목)과 단가.</summary>
    [HttpGet("materials")]
    public Task<ActionResult> Materials([FromQuery] string? q, CancellationToken ct) => Run(async () =>
    {
        var t = (q ?? "").Trim();
        var rows = (await _db.MaterialsAsync(ct))
            .Where(m => t.Length == 0 || m.MatId.Contains(t, StringComparison.OrdinalIgnoreCase) || m.MatDesc.Contains(t, StringComparison.OrdinalIgnoreCase)
                        || m.MatType.Contains(t, StringComparison.OrdinalIgnoreCase))
            .Select(m => new MaterialRow(m.MatId, m.MatDesc, m.MatType, _db.CustomerName(m.Customer), m.Price)).ToList();
        return Ok(rows);
    });
}
