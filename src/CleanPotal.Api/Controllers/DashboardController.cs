using CleanPotal.Api.Infrastructure;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Core.Iot;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 대시보드 요약 — "지금 문제 있는 것"(이상 알림)과 현장 숫자(체크시트·온·습도·기타세정·생산팀 요청)를 한 번에 준다.
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
    private readonly ZigbeeSensorStore _store;
    private readonly ZigbeeOptions _zigbee;

    public DashboardController(CleanPotalDbContext db, IMemoryCache cache, ICheckSheetService checks,
        IHandoverService handover, IProdReqService prodReq, ZigbeeSensorStore store, IOptions<ZigbeeOptions> zigbee)
    {
        _db = db;
        _cache = cache;
        _checks = checks;
        _handover = handover;
        _prodReq = prodReq;
        _store = store;
        _zigbee = zigbee.Value;
    }

    private sealed record Shared(DashChecklistDto? Checklist, DashSensorsDto? Sensors, DashHandoverDto? Handover, (int Open, int Overdue)? ProdReq);

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> Summary(CancellationToken ct)
    {
        var u = HttpContext.Items["auth_user"] as User;
        if (u is null) return Unauthorized();

        bool Can(int access, string route) => u.IsAdmin || (access >= 1 && !MenuGateFilter.IsHidden(u.HiddenMenus, route));
        var canChecklist = Can(u.AccessField, "/checklist");
        var canSensors = Can(u.AccessField, "/temp-humidity");
        var canHandover = Can(u.AccessHandover, "/handover");
        var canProdReq = Can(u.AccessHandover, "/prodreq");

        var shared = await _cache.GetOrCreateAsync(CacheKey, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheFor;
            return new Shared(await ChecklistAsync(), await SensorsAsync(ct), await HandoverAsync(), await ProdReqAsync(ct));
        }) ?? new Shared(null, null, null, null);

        var checklist = canChecklist ? shared.Checklist : null;
        var sensors = canSensors ? shared.Sensors : null;
        var handover = canHandover ? shared.Handover : null;
        DashProdReqDto? prodReq = null;
        if (canProdReq && shared.ProdReq is { } pr)
        {
            var unread = 0;
            try { unread = await _prodReq.GetUnreadCountAsync(u.Username); } catch (Exception) { /* 미확인 수는 곁다리 */ }
            prodReq = new DashProdReqDto(pr.Open, pr.Overdue, unread);
        }

        return Ok(new DashboardSummaryDto(Alerts(checklist, sensors, handover, prodReq), checklist, sensors, handover, prodReq, DateTime.Now));
    }

    /// <summary>맨 위 이상 알림 — 정상이면 비어 있다. 급한 것(bad)을 앞에.</summary>
    public static IReadOnlyList<DashAlertDto> Alerts(DashChecklistDto? c, DashSensorsDto? s, DashHandoverDto? h, DashProdReqDto? p)
    {
        var list = new List<DashAlertDto>();
        if (s is not null)
        {
            if (!s.Collecting) list.Add(new("bad", "온·습도 수집이 끊겼습니다", "/temp-humidity"));
            else if (s.Total > 0 && s.Online < s.Total) list.Add(new("bad", $"온·습도 센서 {s.Total - s.Online}곳 수신 없음", "/temp-humidity"));
            var alert = s.Sensors.Where(x => x.Status == "alert").Select(x => x.Name).ToList();
            var warn = s.Sensors.Where(x => x.Status == "warn").Select(x => x.Name).ToList();
            if (alert.Count > 0) list.Add(new("bad", $"온·습도 기준 초과: {string.Join(", ", alert)}", "/temp-humidity"));
            if (warn.Count > 0) list.Add(new("warn", $"온·습도 주의: {string.Join(", ", warn)}", "/temp-humidity"));
        }
        if (h is { Overdue: > 0 }) list.Add(new("bad", $"기타세정 출고일 지남 {h.Overdue}건", "/handover"));
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

    private async Task<DashSensorsDto?> SensorsAsync(CancellationToken ct)
    {
        try
        {
            var now = DateTime.Now;
            var sensors = await _db.ZigbeeSensors.AsNoTracking().Where(s => s.IsEnabled)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.DeviceId).ToListAsync(ct);
            var rows = (await _db.ZigbeeThresholds.AsNoTracking().ToListAsync(ct)).Select(IotController.ToRow).ToList();
            var list = sensors.Select(s => ZigbeeMapping.ToDto(s.DeviceId, s.DisplayName, s.Site, _store.Get(s.DeviceId), now,
                ZigbeeLimitResolver.Resolve(s.DeviceId, s.Site, rows, _zigbee))).ToList();
            // 구독을 꺼 둔 서버(테스트 서버)는 수집 끊김으로 알리지 않는다.
            var collecting = !_zigbee.Mqtt.Enabled
                || (_store.MqttConnected && _store.Zigbee2MqttAlive(now, _zigbee.Zigbee2MqttSilentMinutes) != false);
            return new DashSensorsDto(collecting, list.Count(s => s.Status != "offline"), list.Count,
                list.Max(s => s.ReceivedAt),
                list.Select(s => new DashSensorDto(s.DeviceName, s.Temperature, s.Humidity, s.Status, s.StatusReason)).ToList());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[dashboard] 온·습도 요약 실패: {ex.Message}");
            return null;
        }
    }

    private async Task<DashHandoverDto?> HandoverAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var open = await _handover.GetAllAsync(null, null, null, weekly: false);   // 진행·포장(완료 제외)
            return new DashHandoverDto(open.Count,
                open.Count(h => h.OutDate == today), open.Count(h => h.OutDate == today.AddDays(1)),
                open.Count(h => h.OutDate < today));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[dashboard] 기타세정 요약 실패: {ex.Message}");
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
