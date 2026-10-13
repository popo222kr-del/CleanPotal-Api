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
        string Comment, Dictionary<string, string?>? Hist);

    private LotRow Row(LegacyLot l)
    {
        var h = l.Hist is null ? null : HistFields(l.Hist, _db.UserName);
        var shipped = l.Hist is not null && l.Hist.TryGetValue("ship", out var sp) ? Day(sp.Split('|')[0]) : null;
        var cmt = l.Hist is not null && l.Hist.TryGetValue("cmt", out var c) && c.Split('|', 2) is { Length: 2 } cp ? cp[1].Trim() : l.Comment;
        return new(l.LotId, l.OutNo.Length > 0 ? $"{l.OutNo}-{l.OutSeq}" : "", l.MatDesc, l.OutSn, l.InSn, l.MatId,
            _db.CustomerName(l.Customer), l.Price, l.Rework, l.Paid, l.Line, l.PmEq, l.Circle, l.ProcName, l.MatType, l.Life, l.UseCount,
            Day(l.CreateTime), LegacyMesReports.OperName(l.Oper, l.Factory), shipped ?? Day(l.AetsOutDay), Day(l.SecOutDay),
            _db.GroupOf(l.Customer), l.MatGroup, l.Oper, cmt, h);
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
        return Ok(new { lot = Row(lot), history = hist, edc });
    });

    /// <summary>출하실적 — 연도·고객사·제품 TYPE.</summary>
    [HttpGet("shipments")]
    public Task<ActionResult> Shipments([FromQuery] int year, [FromQuery] string? customer, [FromQuery] string? type, CancellationToken ct) => Run(async () =>
    {
        if (year < 2000 || year > 2100) year = DateTime.Now.Year;
        var lots = (await _db.ShippedAsync(year, ct)).Where(l =>
            (string.IsNullOrWhiteSpace(customer) || l.Customer == customer)
            && (string.IsNullOrWhiteSpace(type) || l.MatType.Contains(type.Trim(), StringComparison.OrdinalIgnoreCase)));
        return Ok(LegacyMesReports.Shipments(lots, year, _db.CustomerName));
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
