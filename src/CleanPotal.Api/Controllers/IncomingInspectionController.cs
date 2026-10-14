using CleanPotal.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 세정 작업 › 입고검사 이력 — 입고검사 대기 LOT 과, 고른 부품(같은 반출 S/N)의 이번 + 이전 5회 측정·판정·메모.
/// 기존 MES DB 를 읽기만 한다. 단가·금액은 싣지 않아 세정 작업 조회 권한으로 본다.
/// </summary>
[ApiController]
[Route("api/incoming-insp")]
[Authorize(Policy = "ViewHandover")]
[MenuGate("/incoming-insp")]
public class IncomingInspectionController : ControllerBase
{
    private readonly LegacyMesDb _db;
    private readonly IMemoryCache _cache;
    public IncomingInspectionController(LegacyMesDb db, IMemoryCache cache) { _db = db; _cache = cache; }

    /// <summary>이전 회차를 몇 번까지 보이나</summary>
    public const int PreviousCycles = 5;

    private async Task<ActionResult> Run(Func<Task<ActionResult>> f)
    {
        if (!_db.Configured) return Ok(new { configured = false });
        try { await _db.LoadNamesAsync(); return await f(); }
        catch (LegacyMesUnavailableException ex) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message }); }
    }

    public record WaitRow(string LotId, string Sn, string MatDesc, string MatId, string Customer, string Group, string Line, string UseCount,
        string Oper, string OperName, string? InTime, string? PrevUse, List<string> Flags);

    /// <summary>입고검사 대기 — 지금 입고검사(2100) 공정의 삼성 LOT, 직전 회차 경고 배지. 1분 캐시.</summary>
    [HttpGet("wait")]
    public Task<ActionResult> Wait(CancellationToken ct) => Run(async () =>
    {
        if (_cache.TryGetValue("incoming:wait", out List<WaitRow>? hit) && hit is not null) return Ok(new { configured = true, rows = hit });
        // 입고검사(2100) 공정 · 삼성(MEMORY·FOUNDRY 등 구분에 든 고객사)만 — 기타세정 고객사는 빼고
        var lots = (await _db.WipAsync(ct))
            .Where(l => l.Oper == "2100" && _db.GroupOf(l.Customer) != LegacyMesDb.OtherGroup)
            .OrderByDescending(l => l.CreateTime).ToList();
        var prev = await _db.LastShippedBySnAsync(lots.Select(l => l.OutSn), ct);
        var edc = (await _db.EdcForLotsAsync(prev.Values.Select(p => p.LotId), ct)).ToLookup(x => x.LotId, x => x.Edc);
        var rows = lots.Select(l =>
        {
            var p = prev.TryGetValue(l.OutSn, out var pv) ? pv : default;
            var flags = p.LotId is null ? new List<string> { "첫 입고" } : IncomingInspection.Flags(edc[p.LotId]);
            return new WaitRow(l.LotId, l.OutSn, l.MatDesc, l.MatId, _db.CustomerName(l.Customer), _db.GroupOf(l.Customer), l.Line, l.UseCount,
                l.Oper, LegacyMesReports.OperName(l.Oper, l.Factory), LegacyMesDb.ParseMinute(l.CreateTime)?.ToString("MM-dd HH:mm"),
                p.UseCount, flags);
        }).ToList();
        _cache.Set("incoming:wait", rows, TimeSpan.FromSeconds(60));
        return Ok(new { configured = true, rows });
    });

    /// <summary>
    /// 부품 이력 — LOT ID · S/N · 반출번호 아무거나(바코드). 그 LOT(없으면 가장 최근 LOT)을 '이번'으로, 같은 반출 S/N 의 이전 5회를 붙인다.
    /// </summary>
    [HttpGet("history")]
    public Task<ActionResult> History([FromQuery] string? q, CancellationToken ct) => Run(async () =>
    {
        var t = KoreanKeys.ToKeys(q);   // 한글 입력기로 찍힌 바코드("1누261014004ㅔ")도 영문 키로
        if (t.Length < 3) return BadRequest(new { error = "LOT ID · S/N · 반출번호를 3글자 이상 넣으세요." });
        var cur = await ResolveAsync(t, ct);
        if (cur is null) return NotFound(new { error = $"'{t}' 에 해당하는 LOT 를 찾지 못했습니다." });

        var lots = cur.OutSn.Length > 0 ? await _db.SnLotsAsync(cur.OutSn, 20, ct) : new List<LegacyLot>();
        var current = lots.FirstOrDefault(l => l.LotId == cur.LotId) ?? (await _db.LotAsync(cur.LotId, ct)) ?? cur;
        var previous = lots.Where(l => l.LotId != current.LotId && string.CompareOrdinal(l.CreateTime, current.CreateTime) < 0)
            .OrderByDescending(l => l.CreateTime).Take(PreviousCycles).ToList();

        var all = previous.Append(current).ToList();
        var edc = (await _db.EdcForLotsAsync(all.Select(l => l.LotId), ct)).ToLookup(x => x.LotId, x => x.Edc);
        var spec = (await _db.SpecAsync(current.MatId, ct))
            .Select(s => new IncomingInspection.SpecLine(s.Param, s.Oper, IncomingInspection.Names.GetValueOrDefault(s.Param, s.Param), s.Min, s.Max))
            .ToList();
        var prevCycles = previous.Select(l => IncomingInspection.BuildCycle(l, edc[l.LotId], spec, false)).ToList();   // 최근 것부터
        var curCycle = IncomingInspection.BuildCycle(current, edc[current.LotId], spec, true);

        return Ok(new
        {
            configured = true,
            lotId = current.LotId, sn = current.OutSn, matDesc = current.MatDesc, matId = current.MatId,
            customer = _db.CustomerName(current.Customer), line = current.Line, inLine = current.InLine.Length > 0 ? current.InLine : current.Line,
            useCount = current.UseCount, status = LegacyMesReports.OperName(current.Oper, current.Factory),
            inTime = LegacyMesDb.ParseMinute(current.CreateTime)?.ToString("yyyy-MM-dd HH:mm"),
            // 스펙 — 숫자 기준이 있는 것만(외관 항목은 기준 없이 Y/N)
            spec = spec.Where(s => s.Min.Length > 0 || s.Max.Length > 0)
                .Select(s => new { s.Param, s.Oper, s.Name, side = IncomingInspection.IsInSide(s.Oper) ? "입고" : "출하", text = IncomingInspection.SpecText(s) }),
            cycles = prevCycles.AsEnumerable().Reverse().Append(curCycle),   // 오래된 것 → 이번
            warnings = IncomingInspection.Warnings(prevCycles),
        });
    });

    /// <summary>LOT ID(정확히) → S/N(반출·반입) → 반출번호 순으로 찾고, 여러 건이면 가장 최근 입고.</summary>
    private async Task<LegacyLot?> ResolveAsync(string t, CancellationToken ct)
    {
        var byLot = await _db.FindLotsAsync(t, null, null, ct);
        var hit = byLot.FirstOrDefault(l => l.LotId.Equals(t, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return hit;
        var bySn = await _db.FindLotsAsync(null, t, null, ct);
        hit = bySn.FirstOrDefault(l => l.OutSn.Equals(t, StringComparison.OrdinalIgnoreCase) || l.InSn.Equals(t, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return hit;
        var byOut = await _db.FindLotsAsync(null, null, t, ct);
        return byOut.FirstOrDefault() ?? byLot.FirstOrDefault() ?? bySn.FirstOrDefault();
    }
}
