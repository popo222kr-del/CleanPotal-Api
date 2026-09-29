using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// 데일리 업무보고 — 각 메뉴에 이미 적힌 기록을 하루 단위로 모은다. 새로 입력받는 것은 없다.
///
/// 하루 = 그날 주간 + 그날 야간(다음 날 아침까지). 체크시트·KOH·BAKE·약액은 이미 (날짜, 주/야)로 적혀 있고,
/// 인수인계·요청사항·BROKEN·폐기품도 날짜 칸(입고일·요청일·발생일 등)으로 적으니 그 날짜로 고른다.
/// 스케줄 보드는 07:00 ~ 다음 날 07:00 한 장이라 그대로 맞는다.
///
/// "출고일 지남"·"마감 지남"은 기록이 아니라 지금 상태라, 지난 날짜를 봐도 지금 기준으로 센다.
/// 섹션 하나가 실패해도 나머지는 보이게 섹션마다 따로 잡는다(대시보드와 같은 방식).
/// </summary>
public class DailyReportService
{
    private const int BoardStartHour = 7;   // 스케줄 보드는 07:00 부터 24시간

    private readonly CleanPotalDbContext _db;
    private readonly IScheduleService _schedule;
    private readonly ICheckSheetService _checks;
    private readonly IHandoverService _handover;
    private readonly IScheduleBoardService _board;
    private readonly IIcpmsService _icpms;
    private readonly IDispatchService _dispatch;
    private readonly WorkLogService _work;

    public DailyReportService(CleanPotalDbContext db, IScheduleService schedule, ICheckSheetService checks,
        IHandoverService handover, IScheduleBoardService board, IIcpmsService icpms, IDispatchService dispatch, WorkLogService work)
    {
        _db = db;
        _schedule = schedule;
        _checks = checks;
        _handover = handover;
        _board = board;
        _icpms = icpms;
        _dispatch = dispatch;
        _work = work;
    }

    private static async Task<T?> Safe<T>(string name, Func<Task<T>> f) where T : class
    {
        try { return await f(); }
        catch (Exception ex)
        {
            Console.WriteLine($"[daily-report] {name} 실패: {ex.Message}");
            return null;
        }
    }

    public async Task<DailyReportDto> GetAsync(DateOnly date, DailyReportAccess can)
    {
        // DbContext 는 동시에 쓰면 안 되므로 차례로 부른다.
        var shiftTeams = await Safe("근무 조", () => _schedule.GetShiftTeamsAsync(date));
        var crew = await Safe("근무", () => CrewAsync(date));
        var checklist = can.Checklist ? await Safe("체크시트", () => ChecklistAsync(date)) : null;
        var meetings = can.Meeting ? await Safe("생산팀 인수인계", () => MeetingsAsync(date)) : null;
        var handover = can.Handover ? await Safe("기타세정", () => HandoverAsync(date, weekly: false)) : null;
        var weekly = can.Weekly ? await Safe("주간세정", () => HandoverAsync(date, weekly: true)) : null;
        var prodReq = can.ProdReq ? await Safe("요청사항", () => ProdReqAsync(date)) : null;
        var board = can.Board ? await Safe("스케줄 보드", () => BoardAsync(date)) : null;
        var chemical = await Safe("약액 교체", () => _work.GetReportAsync(date));
        var waste = can.Waste ? await Safe("KOH·폐액", () => _work.GetWasteRangeAsync(date.AddDays(-1), date)) : null;
        var bake = can.Bake ? chemical?.Bake : null;
        if (chemical is not null) chemical = chemical with { Bake = null };   // BAKE 는 따로 싣는다
        var broken = can.Broken ? await Safe("BROKEN", () => BrokenAsync(date)) : null;
        var scrap = can.Scrap ? await Safe("폐기품", () => ScrapAsync(date)) : null;
        var icpms = can.Icpms ? await Safe("ICP-MS", () => IcpmsAsync(date)) : null;
        var dispatch = can.Dispatch ? await Safe("배차", () => DispatchAsync(date)) : null;
        var tomorrow = await Safe("다음 날", () => TomorrowAsync(date.AddDays(1)));

        return new DailyReportDto(date, shiftTeams?.DayTeams, shiftTeams?.NightTeams, crew, checklist, meetings,
            handover, weekly, prodReq, board, chemical, waste, bake, broken, scrap, icpms, dispatch, tomorrow);
    }

    private async Task<IReadOnlyList<DailyCrewTeamDto>> CrewAsync(DateOnly date)
    {
        var st = await _schedule.GetTodayStatusAsync(date);
        IReadOnlyList<string> Names(TeamTodayDto t, string kind) =>
            t.Badges.FirstOrDefault(b => b.Kind == kind)?.Names ?? (IReadOnlyList<string>)Array.Empty<string>();
        return st.Teams.Select(t => new DailyCrewTeamDto(t.Team, t.Dept, t.Production,
            Names(t, "day"), Names(t, "night"), Names(t, "off"), Names(t, "edu"))).ToList();
    }

    private async Task<DailyChecklistDto> ChecklistAsync(DateOnly date)
    {
        var st = await _checks.GetStatusAsync(date);
        var zones = st.Lines.SelectMany(l => l.Zones.Select(z => new DailyCheckZoneDto(l.Line, z.Code, z.Name,
                z.Day.State, z.Day.Ng, z.Day.SubmittedByName, z.Night.State, z.Night.Ng, z.Night.SubmittedByName, z.WeeklyOverdue)))
            .Where(z => z.DayState != "na" || z.NightState != "na")
            .ToList();
        var ngs = (await _checks.GetNgsAsync(false, null, date, date))
            .OrderBy(n => n.Shift == "야간" ? 1 : 0).ThenBy(n => n.ZoneName)
            .Select(n => new DailyCheckNgDto(n.ZoneName, n.Shift, n.ItemText, n.Memo, n.CheckedByName, n.NgStatus, n.NgCloseNote))
            .ToList();
        return new DailyChecklistDto(zones, ngs, st.OpenNg);
    }

    private async Task<IReadOnlyList<DailyMeetingDto>> MeetingsAsync(DateOnly date)
        => await _db.ProductionMeetings.AsNoTracking().Where(m => m.MeetingDate == date)
            .OrderBy(m => m.Id)
            .Select(m => new DailyMeetingDto(m.Title, m.DayContent, m.NightContent, m.OfficeMemo, m.CreatorName))
            .ToListAsync();

    private async Task<DailyHandoverDto> HandoverAsync(DateOnly date, bool weekly)
    {
        var open = await _handover.GetAllAsync(null, null, null, weekly);
        var done = await _handover.GetAllAsync("완료", null, null, weekly);
        var all = open.Concat(done).ToList();
        static DailyHandoverItemDto Item(HandoverDto h) => new(h.Vendor, h.Content, h.Owner, h.InDate, h.OutDate, h.Status);
        var today = DateOnly.FromDateTime(DateTime.Today);
        return new DailyHandoverDto(
            all.Where(h => h.InDate == date).OrderBy(h => h.Vendor).Select(Item).ToList(),
            all.Where(h => h.OutDate == date).OrderBy(h => h.Vendor).Select(Item).ToList(),
            open.Where(h => h.OutDate < today).OrderBy(h => h.OutDate).Select(Item).ToList(),
            open.Where(h => h.OutDate == date.AddDays(1)).OrderBy(h => h.Vendor).Select(Item).ToList(),
            open.Count);
    }

    private async Task<DailyProdReqDto> ProdReqAsync(DateOnly date)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var rows = await _db.ProdReqs.AsNoTracking()
            .Where(p => p.RequestDate == date || p.ActionDate == date || (p.Status == "진행" && p.DueDate < today))
            .ToListAsync();
        DailyProdReqItemDto Item(Core.Entities.ProdReq p) => new(p.Category, p.Location, p.RequestDetail, p.Requester,
            p.DueDate, p.Status, p.ActionDetail, p.Assignee);
        return new DailyProdReqDto(
            rows.Where(p => p.RequestDate == date).OrderBy(p => p.Id).Select(Item).ToList(),
            rows.Where(p => p.ActionDate == date && p.Status == "완료").OrderBy(p => p.Id).Select(Item).ToList(),
            rows.Where(p => p.Status == "진행" && p.DueDate < today).OrderBy(p => p.DueDate).Select(Item).ToList());
    }

    /// <summary>분(07:00 기준 오프셋) → "HH:mm". 하루를 넘으면 다음 날로 본다.</summary>
    private static (string Time, bool NextDay) BoardTime(int offset)
    {
        var abs = BoardStartHour * 60 + offset;
        return ($"{abs / 60 % 24:00}:{abs % 60:00}", abs >= 24 * 60);
    }

    private async Task<DailyBoardDto> BoardAsync(DateOnly date)
    {
        var eqs = await _board.GetEquipmentsAsync();
        var blocks = await _board.GetDayAsync(date.ToString("yyyy-MM-dd"));
        var byEq = blocks.GroupBy(b => b.EquipmentIndex).ToDictionary(g => g.Key, g => g.OrderBy(b => b.StartMinute).ToList());
        var rows = eqs.Where(e => byEq.ContainsKey(e.Index)).Select(e => new DailyBoardEqDto(e.DisplayName, e.GroupName, e.Process,
            byEq[e.Index].Select(b =>
            {
                var total = b.S2Minutes + b.HFMinutes + b.DIMinutes;
                var (start, _) = BoardTime(b.StartMinute);
                var (end, next) = BoardTime(b.StartMinute + total);
                var name = b.RecipeText.Split('@')[0].Trim();
                if (b.S2Temperature is { } t) name += $" (S2 {t}℃ {b.S2Minutes}분)";
                return new DailyBoardBlockDto(start, end, next, name, total);
            }).ToList())).ToList();
        return new DailyBoardDto(rows, eqs.Count, eqs.Count(e => e.IsIdle));
    }

    private async Task<IReadOnlyList<DailyBrokenDto>> BrokenAsync(DateOnly date)
        => await _db.BrokenRecords.AsNoTracking().Where(b => b.OccurDate == date).OrderBy(b => b.Id)
            .Select(b => new DailyBrokenDto(b.Line, b.ProductName, b.SN, b.Team, b.Causer, b.OccurStage, b.Description,
                b.Status, b.IsOfficial))
            .ToListAsync();

    private async Task<IReadOnlyList<DailyScrapDto>> ScrapAsync(DateOnly date)
    {
        var batches = await _db.ScrapBatches.AsNoTracking().Include(b => b.Items).Where(b => b.Date == date)
            .OrderBy(b => b.Id).ToListAsync();
        return batches.Select(b => new DailyScrapDto(b.Title, b.Items.Count, b.Items.Count(i => i.Loaded), b.IsClosed,
            b.Items.Select(i => i.Line.Trim()).Where(l => l.Length > 0).Distinct().OrderBy(l => l).ToList())).ToList();
    }

    private async Task<IReadOnlyList<DailyIcpmsDto>> IcpmsAsync(DateOnly date)
        => (await _icpms.GetCheckNotesAsync(date.ToString("yyyy-MM-dd")))
            .Where(n => n.Measured || n.Note.Trim().Length > 0)
            .Select(n => new DailyIcpmsDto(n.EqId, n.Process, n.Measured, n.TopElement, n.TopValue, n.Note))
            .ToList();

    private async Task<IReadOnlyList<DailyDispatchDto>> DispatchAsync(DateOnly date)
        => (await _dispatch.GetByDateAsync(date))
            .Select(d => new DailyDispatchDto(d.VendorName, d.OutgoingDetails, d.IncomingDetails)).ToList();

    private async Task<DailyTomorrowDto> TomorrowAsync(DateOnly day)
    {
        var edu = (await _db.EducationPlans.AsNoTracking()
                .Where(e => e.StartDate != null && e.StartDate <= day && e.Status != "취소")
                .Where(e => (e.EndDate ?? e.StartDate) >= day)
                .OrderBy(e => e.MemberName)
                .Select(e => new { e.MemberName, e.CourseName })
                .ToListAsync())
            .Select(e => $"{e.MemberName} · {e.CourseName}").ToList();
        var events = await _db.TeamEvents.AsNoTracking()
            .Where(e => e.StartDate <= day && e.EndDate >= day)
            .OrderBy(e => e.StartDate).Select(e => e.Content).ToListAsync();
        return new DailyTomorrowDto(edu, events);
    }
}
