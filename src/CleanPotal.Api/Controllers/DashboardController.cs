using CleanPotal.Api.Infrastructure;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using CleanPotal.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 대시보드 요약 — "지금 문제 있는 것"(이상 알림)과 현장 숫자를 한 번에 준다.
/// 카드: 체크시트(현장) · 기타세정 · 주간세정 · 생산팀 요청 · 오늘 배차 · BROKEN
///      · 체크시트(설비) · 스케줄 보드 · 약액 교체 · KOH·폐액 · BAKE 진행 현황(2026-09-30 추가).
/// 설비·공정 기록은 근무일(07시 전은 전날) 기준이다 — 야간에 열어도 그 근무의 기록을 센다.
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
    private readonly EqCheckService _eq;
    private readonly WorkLogService _work;
    private readonly IScheduleBoardService _board;

    public DashboardController(CleanPotalDbContext db, IMemoryCache cache, ICheckSheetService checks,
        IHandoverService handover, IProdReqService prodReq, IDispatchService dispatch,
        EqCheckService eq, WorkLogService work, IScheduleBoardService board)
    {
        _eq = eq;
        _work = work;
        _board = board;
        _db = db;
        _cache = cache;
        _checks = checks;
        _handover = handover;
        _prodReq = prodReq;
        _dispatch = dispatch;
    }

    private sealed record Shared(DashChecklistDto? Checklist, DashHandoverDto? Handover, DashHandoverDto? Weekly,
        (int Open, int Overdue)? ProdReq, DashDispatchDto? Dispatch, DashBrokenDto? Broken,
        DashEqCheckDto? EqCheck, DashBoardDto? Board, DashChemicalDto? Chemical, DashWasteDto? Waste, DashBakeDto? Bake);

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
        var canEqCheck = Can(u.AccessField, "/eq-check");
        var canBoard = Can(u.AccessField, "/schedule-board");
        var canChemical = Can(u.AccessField, "/work/chemical");
        var canWaste = Can(u.AccessField, "/work/waste");
        var canBake = Can(u.AccessField, "/work/bake");

        // 체크시트(구역 부서)·세정 현황(부서 업체 기준)은 부서마다 다르다 — 부서별로 따로 담아 둔다.
        var shared = await _cache.GetOrCreateAsync($"{CacheKey}:{(u.IsAdmin ? "admin" : "user")}:{(u.Department ?? "").Trim()}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheFor;
            // 모두가 같이 쓰는 캐시다 — 처음 부른 사람이 탭을 닫아 그 요청이 취소돼도 카드가 비어 30초 동안
            // 모두에게 빈 카드가 가지 않게, 요청의 취소 신호를 넘기지 않는다.
            var workDate = WorkDate(DateTime.Now);
            var report = await Safe("약액 교체", () => _work.GetReportAsync(workDate));
            return new Shared(await ChecklistAsync(), await HandoverAsync(weekly: false), await HandoverAsync(weekly: true),
                await ProdReqAsync(CancellationToken.None), await DispatchAsync(), await BrokenAsync(CancellationToken.None),
                await Safe("체크시트(설비)", EqCheckAsync), await Safe("스케줄 보드", () => BoardAsync(workDate)),
                report is null ? null : ChemicalSummary(report), await Safe("KOH·폐액", () => WasteAsync(workDate)),
                report is null ? null : BakeSummary(report));
        }) ?? new Shared(null, null, null, null, null, null, null, null, null, null, null);

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

        var eqCheck = canEqCheck ? shared.EqCheck : null;
        return Ok(new PortalDashboardDto(Alerts(checklist, handover, prodReq, weekly, eqCheck),
            checklist, handover, weekly, prodReq, canDispatch ? shared.Dispatch : null, canBroken ? shared.Broken : null, DateTime.Now,
            eqCheck, canBoard ? shared.Board : null, canChemical ? shared.Chemical : null,
            canWaste ? shared.Waste : null, canBake ? shared.Bake : null));
    }

    /// <summary>근무일 — 교대 시작 07시 전이면 전날 근무다(스케줄 보드·약액·KOH·BAKE 기록이 이 날짜로 적힌다).</summary>
    public static DateOnly WorkDate(DateTime now) => DateOnly.FromDateTime(now.AddHours(-7));

    /// <summary>맨 위 이상 알림 — 정상이면 비어 있다. 급한 것(bad)을 앞에.</summary>
    public static IReadOnlyList<DashAlertDto> Alerts(DashChecklistDto? c, DashHandoverDto? h, DashProdReqDto? p, DashHandoverDto? w = null,
        DashEqCheckDto? e = null)
    {
        var list = new List<DashAlertDto>();
        if (h is { Overdue: > 0 }) list.Add(new("bad", $"기타세정 출고일 지남 {h.Overdue}건", "/handover"));
        if (w is { Overdue: > 0 }) list.Add(new("bad", $"주간세정 출고일 지남 {w.Overdue}건", "/weekly"));
        if (c is { OpenNg: > 0 }) list.Add(new("warn", $"체크시트(현장) 미조치 NG {c.OpenNg}건", "/checklist?tab=ng"));
        if (c is { WeeklyOverdue: > 0 }) list.Add(new("warn", $"체크시트(현장) 주 1회 점검 밀림 {c.WeeklyOverdue}건", "/checklist"));
        if (e is { OpenNg: > 0 }) list.Add(new("warn", $"체크시트(설비) 미조치 NG {e.OpenNg}건", "/eq-check?tab=ng"));
        if (e is { WeeklyLate: > 0 }) list.Add(new("warn", $"체크시트(설비) 주간 점검 밀림 {e.WeeklyLate}대", "/eq-check"));
        if (p is { Overdue: > 0 }) list.Add(new("warn", $"생산팀 요청 마감 지남 {p.Overdue}건", "/prodreq"));
        return list.OrderBy(a => a.Level == "bad" ? 0 : 1).ToList();
    }

    // ── 카드별 계산 — 하나가 실패해도 나머지 카드는 보이게 각자 잡는다 ──

    private static async Task<T?> Safe<T>(string name, Func<Task<T>> f) where T : class
    {
        try { return await f(); }
        catch (Exception ex)
        {
            Console.WriteLine($"[dashboard] {name} 요약 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>체크시트(설비) — 점검 항목이 있는 설비만 센다(그 주기 항목이 없으면 빼고).</summary>
    public static DashEqCheckDto EqCheckSummary(EqCheckStatusDto st)
    {
        var rows = st.Rows;
        int Units(Func<EqCheckStatusRowDto, EqCheckCellDto> cell) => rows.Count(r => cell(r).State != "none");
        int Done(Func<EqCheckStatusRowDto, EqCheckCellDto> cell) => rows.Count(r => cell(r).State == "done");
        return new DashEqCheckDto(st.Date,
            Done(r => r.Daily), Units(r => r.Daily),
            Done(r => r.Weekly), Units(r => r.Weekly), rows.Count(r => r.Weekly.State == "late"),
            Done(r => r.Monthly), Units(r => r.Monthly),
            st.WeekDue, st.MonthDue, rows.Sum(r => r.OpenNg));
    }

    private async Task<DashEqCheckDto> EqCheckAsync() => EqCheckSummary(await _eq.GetStatusAsync(null));

    private async Task<DashBoardDto> BoardAsync(DateOnly date)
    {
        var eqs = await _board.GetEquipmentsAsync();
        var blocks = await _board.GetDayAsync(date.ToString("yyyy-MM-dd"));
        var busy = blocks.Select(b => b.EquipmentIndex).ToHashSet();
        return new DashBoardDto(date, eqs.Count(e => busy.Contains(e.Index)), eqs.Count, eqs.Count(e => e.IsIdle), blocks.Count);
    }

    /// <summary>약액 교체 — 세정 설비 중 그날 교체 내용이 적힌 설비.</summary>
    public static DashChemicalDto ChemicalSummary(WorkReportDto r)
    {
        var codes = r.Rows.Where(x => x.Kind != EquipKinds.Bake && !string.IsNullOrWhiteSpace(x.Content)).Select(x => x.Code).ToList();
        return new DashChemicalDto(r.Date, codes.Count, codes.Take(8).ToList());
    }

    /// <summary>BAKE — 가동(상태 빈 값) 기록이 있는 오븐 수와 그을음·Q'TZ 이상 건수.</summary>
    public static DashBakeDto BakeSummary(WorkReportDto r)
    {
        var logs = r.Bake ?? Array.Empty<BakeLogDto>();
        var ovens = r.Rows.Where(x => x.Kind == EquipKinds.Bake).Select(x => x.Code)
            .Concat(logs.Select(l => l.EqCode)).Distinct().Count();
        var running = logs.Where(l => string.IsNullOrWhiteSpace(l.Status)).Select(l => l.EqCode).Distinct().Count();
        return new DashBakeDto(r.Date, running, ovens, logs.Count(l => l.HasSoot), logs.Count(l => l.HasQuartz));
    }

    /// <summary>KOH·폐액 — 그날과 전날, 주·야 합. 한 교대도 적지 않은 값은 null.</summary>
    public static DashWasteDto WasteSummary(IReadOnlyList<WasteLogDto> rows, DateOnly date)
    {
        static decimal? Sum(IEnumerable<decimal?> v) { var l = v.Where(x => x.HasValue).ToList(); return l.Count == 0 ? null : l.Sum(); }
        var today = rows.Where(r => r.Date == date).ToList();
        var prev = rows.Where(r => r.Date == date.AddDays(-1)).ToList();
        return new DashWasteDto(date, Sum(today.Select(r => r.CausticUsed)), Sum(today.Select(r => r.WasteIncrease)),
            Sum(prev.Select(r => r.CausticUsed)), Sum(prev.Select(r => r.WasteIncrease)),
            today.Count(r => r.CausticUsed.HasValue || r.WasteIncrease.HasValue));
    }

    private async Task<DashWasteDto> WasteAsync(DateOnly date)
        => WasteSummary((await _work.GetWasteRangeAsync(date.AddDays(-1), date)).Rows, date);

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
