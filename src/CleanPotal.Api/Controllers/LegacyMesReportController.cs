using System.Globalization;
using CleanPotal.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    public record LotRow(string LotId, string OutNo, string MatDesc, string OutSn, string InSn, string MatId, string Customer, decimal? Price,
        string Rework, string Paid, string Line, string PmEq, string Circle, string ProcName, string MatType, string Life, string UseCount,
        string? AetsInDay, string Status, string? AetsOutDay, string? SecOutDay, string Group, string MatGroup, string Oper,
        string Comment, Dictionary<string, string?>? Hist, string InLine, string ShipTo);

    private LotRow Row(LegacyLot l)
    {
        var h = l.Hist is null ? null : HistFields(l.Hist, _db.UserName);
        var shipped = l.Hist is not null && l.Hist.TryGetValue("ship", out var sp) ? Day(sp.Split('|')[0]) : null;
        var cmt = l.Hist is not null && l.Hist.TryGetValue("cmt", out var c) && c.Split('|', 2) is { Length: 2 } cp ? cp[1].Trim() : l.Comment;
        return new(l.LotId, l.OutNo.Length > 0 ? $"{l.OutNo}-{l.OutSeq}" : "", l.MatDesc, l.OutSn, l.InSn, l.MatId,
            _db.CustomerName(l.Customer), l.Price, l.Rework, l.Paid, l.Line, l.PmEq, l.Circle, l.ProcName, l.MatType, l.Life, l.UseCount,
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
        string Res, string User, string UserName, string Comment);
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
        var hist = (await _db.LotHistoryAsync(lotId, ct)).Select(h => new HistLine(h.Seq, Sec(h.Time), h.Code, h.Oper, OpName(h.Oper),
            h.OldOper, OpName(h.OldOper), h.Res, h.User, _db.UserName(h.User), h.Comment)).ToList();
        var edc = (await _db.LotEdcAsync(lotId, ct)).Select(e => new EdcLine(e.Seq, e.Param, e.Oper, OpName(e.Oper),
            e.Values.Where(v => v.Length > 0).ToList(), e.User, _db.UserName(e.User), Sec(e.Time))).ToList();
        var spec = await _db.SpecAsync(lot.MatId, ct);
        return Ok(new { lot = Row(lot), history = hist, edc, spec });
    });

    /// <summary>출하실적 구분 — 삼성은 MEMORY·FOUNDRY(설정 순서), 나머지는 고객사 이름(뒤로)</summary>
    private (string Key, string Name, int Order) ShipGroupOf(LegacyLot l)
    {
        var g = _db.GroupOf(l.Customer);
        if (g != LegacyMesDb.OtherGroup)
        {
            var i = _db.CustomerGroups.Select(x => x.Name).Distinct().ToList().IndexOf(g);
            return (g, g, i < 0 ? 50 : i);
        }
        return ("c:" + l.Customer, _db.CustomerName(l.Customer), 100);
    }

    /// <summary>구분 고르기 — "" 전체, "MEMORY"·"FOUNDRY" 같은 구분 이름, "기타세정" 은 삼성 밖 고객사 전부</summary>
    private bool InGroupFilter(LegacyLot l, string? group)
        => string.IsNullOrWhiteSpace(group) || group == (_db.GroupOf(l.Customer) == LegacyMesDb.OtherGroup ? LegacyMesDb.OtherGroup : _db.GroupOf(l.Customer));

    private static bool TypeOk(LegacyLot l, string? type)
        => string.IsNullOrWhiteSpace(type) || l.MatType.Contains(type.Trim(), StringComparison.OrdinalIgnoreCase)
           || l.MatDesc.Contains(type.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 출하실적 자료 — 출하 LOT(+ 케미칼 SD-1 출고) · 지금 창고 재고. 기본은 기존 MES 와 같게: 무상세정 빼고(free=false), 케미칼 넣고(chem=true).
    /// </summary>
    private async Task<(List<LegacyLot> Ship, List<LegacyLot> Stock, bool Truncated)> ShipDataAsync(int year, string? group, string? type,
        bool free, bool chem, CancellationToken ct)
    {
        var shipped = await _db.ShippedAsync(year, ct);
        var chemical = chem ? await _db.ChemicalOutAsync(year, ct) : new List<LegacyLot>();
        var stock = await _db.WipAsync(ct);
        bool Keep(LegacyLot l) => InGroupFilter(l, group) && TypeOk(l, type) && (free || !LegacyMesReports.IsFree(l));
        return (shipped.Concat(chemical).Where(Keep).ToList(), stock.Where(l => LegacyMesReports.StockSlot(l) >= 0 && Keep(l)).ToList(),
            shipped.Count >= LegacyMesDb.ShipMaxRows);
    }

    /// <summary>출하실적 — 연도·구분(MEMORY·FOUNDRY·기타세정)·제품 TYPE·무상 포함·케미칼 포함. 월 = 실제 출하 시각, 옆에 지금 창고 재고.</summary>
    [HttpGet("shipments")]
    public Task<ActionResult> Shipments([FromQuery] int year, [FromQuery] string? group, [FromQuery] string? type,
        [FromQuery] bool free = false, [FromQuery] bool chem = true, CancellationToken ct = default) => Run(async () =>
    {
        if (year < 2000 || year > 2100) year = DateTime.Now.Year;
        var (ship, stock, truncated) = await ShipDataAsync(year, group, type, free, chem, ct);
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
        [FromQuery] bool free = false, [FromQuery] bool chem = true, CancellationToken ct = default) => Run(async () =>
    {
        if (year < 2000 || year > 2100) year = DateTime.Now.Year;
        var (ship, wip, _) = await ShipDataAsync(year, group, type, free, chem, ct);
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
