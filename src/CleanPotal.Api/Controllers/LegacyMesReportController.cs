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

    public record StatusDto(bool Configured, List<string> Customers, Dictionary<string, string> CustomerNames);

    /// <summary>연결 여부와 고객사 목록(품목에 쓰인 코드 전체, 또는 설정에 적은 코드).</summary>
    [HttpGet("status")]
    public async Task<ActionResult<StatusDto>> Status(CancellationToken ct)
    {
        var codes = _db.AllCustomers ? new List<string>() : _db.Customers.ToList();
        await _db.LoadNamesAsync(ct);
        if (_db.Configured)
            try { codes = (await _db.CustomerCodesAsync(ct)).Select(c => c.Code).ToList(); }
            catch (LegacyMesUnavailableException) { /* 목록 없이도 화면은 연다 */ }
        return Ok(new StatusDto(_db.Configured, codes, codes.ToDictionary(c => c, _db.CustomerName)));
    }

    private static bool Match(LegacyLot l, string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return true;
        var t = q.Trim();
        return l.MatId.Contains(t, StringComparison.OrdinalIgnoreCase) || l.MatDesc.Contains(t, StringComparison.OrdinalIgnoreCase)
            || l.LotId.Contains(t, StringComparison.OrdinalIgnoreCase) || l.MatType.Contains(t, StringComparison.OrdinalIgnoreCase);
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
        string? AetsInDay, string Status, string? AetsOutDay, string? SecOutDay);

    private LotRow Row(LegacyLot l) => new(l.LotId, l.OutNo.Length > 0 ? $"{l.OutNo}-{l.OutSeq}" : "", l.MatDesc, l.OutSn, l.InSn, l.MatId,
        _db.CustomerName(l.Customer), l.Price, l.Rework, l.Paid, l.Line, l.PmEq, l.Circle, l.ProcName, l.MatType, l.Life, l.UseCount,
        Day(l.CreateTime), LegacyMesReports.OperName(l.Oper, l.Factory), Day(l.AetsOutDay), Day(l.SecOutDay));

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
            .Take(5000).Select(Row).ToList();
        return Ok(rows);
    });

    /// <summary>LOT 조회 — AETS 입고일 기간(최대 1년). 보내 준 조회 쿼리와 같은 칸.</summary>
    [HttpGet("lots")]
    public Task<ActionResult> Lots([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] string? q, CancellationToken ct) => Run(async () =>
    {
        if (to < from) return BadRequest(new { error = "기간을 확인하세요." });
        if (to.DayNumber - from.DayNumber > 366) return BadRequest(new { error = "기간은 1년까지 조회할 수 있습니다." });
        var lots = await _db.LotsByCreateAsync(from, to, ct);
        return Ok(new { truncated = lots.Count >= LegacyMesDb.MaxRows, rows = lots.Where(l => Match(l, q)).Select(Row).ToList() });
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
