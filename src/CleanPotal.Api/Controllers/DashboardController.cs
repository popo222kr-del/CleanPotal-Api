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
/// 1차: 체크시트·기타세정·주간세정·생산팀 요청 / 2차: MES 재공·오늘 배차·ICP-MS·인수인계·주간보고 작성 여부.
/// (기타세정과 주간세정은 같은 표를 업체 마스터로 나눈 두 메뉴다 — 한쪽만 세지 않게 주의.)
/// (온·습도는 뺐다 — 필요하면 IotController.Latest 와 같은 방식으로 카드를 다시 붙인다.)
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
    private readonly IIcpmsService _icpms;
    private readonly IInventoryService _inventory;
    private readonly IServiceProvider _services;

    public DashboardController(CleanPotalDbContext db, IMemoryCache cache, ICheckSheetService checks,
        IHandoverService handover, IProdReqService prodReq, IDispatchService dispatch, IIcpmsService icpms, IInventoryService inventory, IServiceProvider services)
    {
        _inventory = inventory;
        _dispatch = dispatch;
        _icpms = icpms;
        _services = services;
        _db = db;
        _cache = cache;
        _checks = checks;
        _handover = handover;
        _prodReq = prodReq;
    }

    private sealed record Shared(DashChecklistDto? Checklist, DashHandoverDto? Handover, DashHandoverDto? Weekly, (int Open, int Overdue)? ProdReq,
        DashMesDto? Mes, DashDispatchDto? Dispatch, DashIcpmsDto? Icpms, DashReportsDto? Reports, DashInventoryDto? Inventory);

    [HttpGet("summary")]
    public async Task<ActionResult<PortalDashboardDto>> Summary(CancellationToken ct)
    {
        var u = HttpContext.Items["auth_user"] as User;
        if (u is null) return Unauthorized();

        bool Can(int access, string route) => u.IsAdmin || (access >= 1 && !MenuGateFilter.IsHidden(u.HiddenMenus, route));
        var canChecklist = Can(u.AccessField, "/checklist");
        var canHandover = Can(u.AccessHandover, "/handover");
        var canWeeklyClean = Can(u.AccessHandover, "/weekly");
        var canProdReq = Can(u.AccessHandover, "/prodreq");
        var canMes = Can(u.AccessMes, "/mes");
        var canDispatch = Can(u.AccessHandover, "/handover");   // 배차는 기타세정 현황 화면의 버튼으로 들어간다
        var canIcpms = Can(u.AccessField, "/icpms");
        var canInventory = Can(u.AccessField, "/inventory");
        var canMeeting = Can(u.AccessHandover, "/meeting");
        var canWeekly = Can(u.AccessOffice, "/weekly-report");

        var shared = await _cache.GetOrCreateAsync(CacheKey, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheFor;
            return new Shared(await ChecklistAsync(), await HandoverAsync(weekly: false), await HandoverAsync(weekly: true), await ProdReqAsync(ct),
                await MesAsync(ct), await DispatchAsync(), await IcpmsAsync(), await ReportsAsync(ct), await InventoryAsync());
        }) ?? new Shared(null, null, null, null, null, null, null, null, null);

        var checklist = canChecklist ? shared.Checklist : null;
        var handover = canHandover ? shared.Handover : null;
        DashProdReqDto? prodReq = null;
        if (canProdReq && shared.ProdReq is { } pr)
        {
            var unread = 0;
            try { unread = await _prodReq.GetUnreadCountAsync(u.Username); } catch (Exception) { /* 미확인 수는 곁다리 */ }
            prodReq = new DashProdReqDto(pr.Open, pr.Overdue, unread);
        }

        var mes = canMes ? shared.Mes : null;
        var reports = (canMeeting || canWeekly) && shared.Reports is { } r
            ? r with { MeetingVisible = canMeeting, WeeklyVisible = canWeekly }
            : null;
        var weekly = canWeeklyClean ? shared.Weekly : null;
        var inventory = canInventory ? shared.Inventory : null;
        return Ok(new PortalDashboardDto(Alerts(checklist, handover, prodReq, mes, weekly, inventory), checklist, handover, prodReq, DateTime.Now,
            mes, canDispatch ? shared.Dispatch : null, canIcpms ? shared.Icpms : null, reports, weekly, inventory));
    }

    /// <summary>맨 위 이상 알림 — 정상이면 비어 있다. 급한 것(bad)을 앞에.</summary>
    public static IReadOnlyList<DashAlertDto> Alerts(DashChecklistDto? c, DashHandoverDto? h, DashProdReqDto? p, DashMesDto? m = null, DashHandoverDto? w = null, DashInventoryDto? i = null)
    {
        var list = new List<DashAlertDto>();
        if (h is { Overdue: > 0 }) list.Add(new("bad", $"기타세정 출고일 지남 {h.Overdue}건", "/handover"));
        if (w is { Overdue: > 0 }) list.Add(new("bad", $"주간세정 출고일 지남 {w.Overdue}건", "/weekly"));
        if (c is { OpenNg: > 0 }) list.Add(new("warn", $"체크시트 미조치 NG {c.OpenNg}건", "/checklist?tab=ng"));
        if (c is { WeeklyOverdue: > 0 }) list.Add(new("warn", $"체크시트 주 1회 점검 밀림 {c.WeeklyOverdue}건", "/checklist"));
        if (p is { Overdue: > 0 }) list.Add(new("warn", $"생산팀 요청 마감 지남 {p.Overdue}건", "/prodreq"));
        if (i is { LowNotOrdered: > 0 }) list.Add(new("warn", $"재고 부족(발주 전) {i.LowNotOrdered}품목", "/inventory"));
        if (m is { LongWait: > 0 }) list.Add(new("warn", $"MES 장기 대기 {m.LongWait} LOT", "/mes"));
        if (m is { Hold: > 0 }) list.Add(new("warn", $"MES 보류 {m.Hold} LOT", "/mes"));
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

    // MES 는 모듈을 끈 서버에서도 대시보드가 떠야 하므로 필요할 때만 꺼내 쓴다(없으면 카드 없음).
    // 공정 카드 규칙은 MES Dash Board(MesDashboardController)와 같다 — 전산등록(1000)은 공정이 아니라 뺀다.
    private async Task<DashMesDto?> MesAsync(CancellationToken ct)
    {
        try
        {
            var lots = _services.GetService<ProductionManagement.Application.Interfaces.ILotService>();
            if (lots is null) return null;
            var sum = await lots.GetDashboardSummaryAsync(ct);
            var wip = await lots.GetProcessWipCountsAsync(ct);
            return new DashMesDto(sum.InProgress, sum.TodayReceived, sum.TodayShipped, sum.Hold, sum.Rework, sum.ShippingWaiting, sum.LongWait,
                wip.Where(w => w.OperCode != 1000).Select(w => new DashMesStageDto(w.ProcessName, w.Count, w.IsBottleneck)).ToList());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[dashboard] MES 요약 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>재고관리 화면과 같은 판정(InventoryService — 현재 재고 ≤ 안전재고, 숫자로 읽히는 품목만).</summary>
    private async Task<DashInventoryDto?> InventoryAsync()
    {
        try
        {
            var items = (await _inventory.GetByZoneAsync(null)).SelectMany(z => z.Items).Where(x => x.IsLow).ToList();
            var notOrdered = items.Where(x => !x.IsOrdered).ToList();
            return new DashInventoryDto(items.Count, notOrdered.Count, items.Count - notOrdered.Count,
                notOrdered.Select(x => x.ItemName.Trim()).Where(n => n.Length > 0).Distinct().Take(4).ToList());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[dashboard] 재고 요약 실패: {ex.Message}");
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

    private async Task<DashIcpmsDto?> IcpmsAsync()
    {
        try
        {
            var s = await _icpms.GetSummaryAsync(null, null);   // 가장 최근 측정일 기준
            return new DashIcpmsDto(s.LatestDate, s.MeasuredEquip, s.TotalEquip, Math.Round(s.MaxValue, 2), s.MaxEqId, s.MaxElement, s.Unit);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[dashboard] ICP-MS 요약 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>오늘 만든 생산미팅(생산팀 인수인계)과 이번 주(월~일)에 만든 주간보고 — 가장 최근 것의 작성자·시각.</summary>
    private async Task<DashReportsDto?> ReportsAsync(CancellationToken ct)
    {
        try
        {
            var today = DateTime.Today;
            var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            var meeting = await _db.Reports.AsNoTracking()
                .Where(r => r.ReportType == "meeting" && r.CreatedAt >= today)
                .OrderByDescending(r => r.CreatedAt).Select(r => new { r.CreatorName, r.CreatedAt }).FirstOrDefaultAsync(ct);
            var weekly = await _db.Reports.AsNoTracking()
                .Where(r => r.ReportType == "weekly" && r.CreatedAt >= monday)
                .OrderByDescending(r => r.CreatedAt).Select(r => new { r.CreatorName, r.CreatedAt }).FirstOrDefaultAsync(ct);
            return new DashReportsDto(true, meeting is not null, meeting?.CreatorName ?? "", meeting?.CreatedAt,
                true, weekly is not null, weekly?.CreatorName ?? "", weekly?.CreatedAt);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[dashboard] 작성 여부 요약 실패: {ex.Message}");
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
