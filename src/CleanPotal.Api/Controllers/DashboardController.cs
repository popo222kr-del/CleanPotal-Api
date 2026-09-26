using CleanPotal.Api.Infrastructure;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 대시보드 요약 — "지금 문제 있는 것"(이상 알림)과 현장 숫자를 한 번에 준다.
/// 카드: 체크시트 · 기타세정 · 주간세정 · 생산팀 요청 · 오늘 배차 · BROKEN.
/// (기타세정과 주간세정은 같은 표를 업체 마스터로 나눈 두 메뉴다 — 한쪽만 세지 않게 주의.)
/// 온·습도·MES 재공·ICP-MS·재고·작성 현황은 써 보고 뺐다(2026-09-26) — 필요하면 같은 방식으로 다시 붙인다.
///
/// 로그인만 요구하고, 카드마다 그 메뉴의 조회 권한과 숨긴 메뉴를 여기서 따진다 — 권한이 없으면 그 카드는 null.
/// 모든 사용자에게 같은 숫자(요청 미확인 수 빼고)는 30초 동안 한 번만 계산한다(대시보드는 자주 열리고 1분마다 새로 고친다).
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    private const string CacheKey = "dashboard:shared";

    private readonly CleanPotalDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ICheckSheetService _checks;
    private readonly IHandoverService _handover;
    private readonly IProdReqService _prodReq;
    private readonly IDispatchService _dispatch;

    public DashboardController(CleanPotalDbContext db, IMemoryCache cache, ICheckSheetService checks,
        IHandoverService handover, IProdReqService prodReq, IDispatchService dispatch)
    {
        _db = db;
        _cache = cache;
        _checks = checks;
        _handover = handover;
        _prodReq = prodReq;
        _dispatch = dispatch;
    }

    private sealed record Shared(DashChecklistDto? Checklist, DashHandoverDto? Handover, DashHandoverDto? Weekly,
        (int Open, int Overdue)? ProdReq, DashDispatchDto? Dispatch, DashBrokenDto? Broken);

    [HttpGet("summary")]
    public async Task<ActionResult<PortalDashboardDto>> Summary(CancellationToken ct)
    {
        var u = HttpContext.Items["auth_user"] as User;
        if (u is null) return Unauthorized();

        bool Can(int access, string route) => u.IsAdmin || (access >= 1 && !MenuGateFilter.IsHidden(u.HiddenMenus, route));
        var canChecklist = Can(u.AccessField, "/checklist");
        var canHandover = Can(u.AccessHandover, "/handover");
        var canWeekly = Can(u.AccessHandover, "/weekly");
        var canProdReq = Can(u.AccessHandover, "/prodreq");
        var canDispatch = Can(u.AccessHandover, "/handover");   // 배차는 기타세정 현황 화면의 버튼으로 들어간다
        var canBroken = Can(u.AccessOffice, "/broken");

        var shared = await _cache.GetOrCreateAsync(CacheKey, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheFor;
            return new Shared(await ChecklistAsync(), await HandoverAsync(weekly: false), await HandoverAsync(weekly: true),
                await ProdReqAsync(ct), await DispatchAsync(), await BrokenAsync(ct));
        }) ?? new Shared(null, null, null, null, null, null);

        var checklist = canChecklist ? shared.Checklist : null;
        var handover = canHandover ? shared.Handover : null;
        var weekly = canWeekly ? shared.Weekly : null;
        DashProdReqDto? prodReq = null;
        if (canProdReq && shared.ProdReq is { } pr)
        {
            var unread = 0;
            try { unread = await _prodReq.GetUnreadCountAsync(u.Username); } catch (Exception) { /* 미확인 수는 곁다리 */ }
            prodReq = new DashProdReqDto(pr.Open, pr.Overdue, unread);
        }

        return Ok(new PortalDashboardDto(Alerts(checklist, handover, prodReq, weekly),
            checklist, handover, weekly, prodReq, canDispatch ? shared.Dispatch : null, canBroken ? shared.Broken : null, DateTime.Now));
    }

    /// <summary>맨 위 이상 알림 — 정상이면 비어 있다. 급한 것(bad)을 앞에.</summary>
    public static IReadOnlyList<DashAlertDto> Alerts(DashChecklistDto? c, DashHandoverDto? h, DashProdReqDto? p, DashHandoverDto? w = null)
    {
        var list = new List<DashAlertDto>();
        if (h is { Overdue: > 0 }) list.Add(new("bad", $"기타세정 출고일 지남 {h.Overdue}건", "/handover"));
        if (w is { Overdue: > 0 }) list.Add(new("bad", $"주간세정 출고일 지남 {w.Overdue}건", "/weekly"));
        if (c is { OpenNg: > 0 }) list.Add(new("warn", $"체크시트 미조치 NG {c.OpenNg}건", "/checklist?tab=ng"));
        if (c is { WeeklyOverdue: > 0 }) list.Add(new("warn", $"체크시트 주 1회 점검 밀림 {c.WeeklyOverdue}건", "/checklist"));
        if (p is { Overdue: > 0 }) list.Add(new("warn", $"생산팀 요청 마감 지남 {p.Overdue}건", "/prodreq"));
        return list.OrderBy(a => a.Level == "bad" ? 0 : 1).ToList();
    }

    // ── 카드별 계산 — 하나가 실패해도 나머지 카드는 보이게 각자 잡는다 ──

    private async Task<DashChecklistDto?> ChecklistAsync()
    {
        try
        {
            var st = await _checks.GetStatusAsync(null);
            var zones = st.Lines.SelectMany(l => l.Zones).ToList();
            var cur = zones.Select(z => st.CurrentShift == "야간" ? z.Night : z.Day).Where(x => x.State != "na").ToList();
            return new DashChecklistDto(st.CurrentWorkDate, st.CurrentShift,
                cur.Count(x => x.State == "submitted"), cur.Count(x => x.State == "progress"), cur.Count,
                st.OpenNg, zones.Sum(z => z.WeeklyOverdue), zones.Sum(z => z.WeeklyDue));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[dashboard] 체크시트 요약 실패: {ex.Message}");
            return null;
        }
    }

    /// <param name="weekly">false = 기타세정 현황, true = 주간세정 현황(업체 마스터의 주간세정 표시로 나뉜다)</param>
    private async Task<DashHandoverDto?> HandoverAsync(bool weekly)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var open = await _handover.GetAllAsync(null, null, null, weekly);   // 진행·포장(완료 제외)
            return new DashHandoverDto(open.Count,
                open.Count(h => h.OutDate == today), open.Count(h => h.OutDate == today.AddDays(1)),
                open.Count(h => h.OutDate < today));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[dashboard] {(weekly ? "주간세정" : "기타세정")} 요약 실패: {ex.Message}");
            return null;
        }
    }

    private async Task<DashDispatchDto?> DispatchAsync()
    {
        try
        {
            var rows = await _dispatch.GetByDateAsync(DateOnly.FromDateTime(DateTime.Now));
            return new DashDispatchDto(rows.Count,
                rows.Select(d => d.VendorName.Trim()).Where(v => v.Length > 0).Distinct().Take(6).ToList());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[dashboard] 배차 요약 실패: {ex.Message}");
            return null;
        }
    }

    public sealed record BrokenRow(DateOnly? OccurDate, DateTime CreatedAt, bool IsOfficial);

    /// <summary>BROKEN 집계 — 발생일(없으면 등록일) 기준 이번 달·올해 건수, 올해 공식 건수.</summary>
    public static DashBrokenDto BrokenSummary(IReadOnlyList<BrokenRow> rows, DateOnly today)
    {
        var yearStart = new DateOnly(today.Year, 1, 1);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var year = rows.Select(b => (Row: b, Date: b.OccurDate ?? DateOnly.FromDateTime(b.CreatedAt)))
            .Where(x => x.Date >= yearStart && x.Date <= today).ToList();
        return new DashBrokenDto(year.Count(x => x.Date >= monthStart), year.Count, year.Count(x => x.Row.IsOfficial));
    }

    /// <summary>BROKEN — 발생일(없으면 등록일) 기준 이번 달·올해 건수, 올해 공식 건수.</summary>
    private async Task<DashBrokenDto?> BrokenAsync(CancellationToken ct)
    {
        try
        {
            var rows = await _db.BrokenRecords.AsNoTracking()
                .Select(b => new BrokenRow(b.OccurDate, b.CreatedAt, b.IsOfficial))
                .ToListAsync(ct);
            return BrokenSummary(rows, DateOnly.FromDateTime(DateTime.Now));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[dashboard] BROKEN 요약 실패: {ex.Message}");
            return null;
        }
    }

    private async Task<(int Open, int Overdue)?> ProdReqAsync(CancellationToken ct)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var open = await _db.ProdReqs.AsNoTracking().Where(p => p.Status == "진행")
                .Select(p => p.DueDate).ToListAsync(ct);
            return (open.Count, open.Count(d => d < today));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[dashboard] 생산팀 요청 요약 실패: {ex.Message}");
            return null;
        }
    }
}
