using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

/// <summary>근무 일정 비즈니스 로직 구현 (도장 근무표 + 교대 예측).</summary>
public class ScheduleService : IScheduleService
{
    private readonly CleanPotalDbContext _db;
    private readonly IHolidayService _holidays;
    public ScheduleService(CleanPotalDbContext db, IHolidayService holidays)
    {
        _db = db;
        _holidays = holidays;
    }

    private static readonly string[] ProductionTeams = { "김팀", "장팀" };
    private static readonly string[] DayNamesKr = { "일", "월", "화", "수", "목", "금", "토" };

    private static int JobTitleOrder(string jt) => jt switch
    {
        _ when jt.Contains("세정팀장") => 1,
        _ when jt.Contains("세정") => 2,
        _ when jt.Contains("QA팀장") => 3,
        _ when jt.Contains("QA") => 4,
        _ when jt.Contains("조장") => 5,
        _ => 99,
    };

    private static bool IsWorkDay(string shiftType)
    {
        var t = shiftType.Replace("예상:", "");
        if (string.IsNullOrEmpty(t) || t == "비우기") return false;
        return !(t.Contains("휴무") || t.Contains("연차") || t.Contains("반차") || t.Contains("교육"));
    }

    public async Task<RosterMonthDto> GetRosterAsync(int year, int month, string teamFilter, bool predict)
    {
        int numDays = DateTime.DaysInMonth(year, month);
        var first = new DateOnly(year, month, 1);
        var last = new DateOnly(year, month, numDays);

        var holidayMap = _holidays.GetMap(year);
        var days = new List<RosterDayHeaderDto>();
        for (int d = 1; d <= numDays; d++)
        {
            var dt = new DateOnly(year, month, d);
            int dow = (int)dt.DayOfWeek;
            bool weekend = dow == 0 || dow == 6;
            bool isHoliday = holidayMap.ContainsKey(dt);
            days.Add(new RosterDayHeaderDto(d, DayNamesKr[dow], weekend, isHoliday));
        }

        var targetTeams = teamFilter == "전체" ? ProductionTeams : new[] { teamFilter };

        var users = await _db.Users
            .Where(u => !u.IsResigned && targetTeams.Contains(u.TeamName))
            .ToListAsync();
        users = users
            .OrderBy(u => u.TeamName)
            .ThenBy(u => JobTitleOrder(u.JobTitle))
            .ThenBy(u => u.RealName)
            .ToList();

        var shifts = await _db.ShiftSchedules
            .Where(s => s.TargetDate >= first && s.TargetDate <= last)
            .ToListAsync();
        var shiftMap = shifts.ToDictionary(s => (s.MemberName, s.TargetDate), s => s.ShiftType);

        var teams = new List<RosterTeamDto>();
        foreach (var team in targetTeams)
        {
            var teamUsers = users.Where(u => u.TeamName == team).ToList();
            if (teamUsers.Count == 0) continue;

            var dailyCounts = new int[numDays];
            int grandTotal = 0;
            var members = new List<RosterMemberDto>();

            foreach (var u in teamUsers)
            {
                var cells = new List<RosterCellDto>();
                int rowTotal = 0;
                for (int i = 0; i < numDays; i++)
                {
                    var date = new DateOnly(year, month, i + 1);
                    shiftMap.TryGetValue((u.RealName, date), out var st);
                    st ??= "";
                    bool predicted = false;
                    if (string.IsNullOrEmpty(st) && predict)
                    {
                        st = "예상:" + ShiftPredictor.Predict(team, date);
                        predicted = true;
                    }
                    if (IsWorkDay(st)) { rowTotal++; dailyCounts[i]++; }
                    var shown = st == "비우기" ? "" : st;
                    cells.Add(new RosterCellDto(date, shown, predicted));
                }
                grandTotal += rowTotal;
                members.Add(new RosterMemberDto(u.RealName, u.JobTitle, cells, rowTotal));
            }
            teams.Add(new RosterTeamDto(team, members, dailyCounts, grandTotal));
        }

        return new RosterMonthDto(year, month, days, teams);
    }

    public async Task<IReadOnlyList<StampedCellDto>> StampAsync(StampShiftRequest req, string actorName)
    {
        var result = new List<StampedCellDto>();
        int repeat = req.Clear ? 1 : Math.Max(1, req.Days);

        foreach (var name in req.Members)
        {
            var team = await _db.Users
                .Where(u => u.RealName == name)
                .Select(u => u.TeamName)
                .FirstOrDefaultAsync() ?? "";

            for (int i = 0; i < repeat; i++)
            {
                var date = req.StartDate.AddDays(i);
                var existing = await _db.ShiftSchedules
                    .FirstOrDefaultAsync(s => s.MemberName == name && s.TargetDate == date);

                string st = req.Clear ? "비우기" : req.ShiftType;

                if (existing is null)
                {
                    _db.ShiftSchedules.Add(new ShiftSchedule
                    {
                        MemberName = name,
                        TargetDate = date,
                        ShiftType = st,
                        TeamGroup = team,
                        CreatorName = actorName,
                        CreateDate = DateTime.Now,
                    });
                }
                else
                {
                    existing.ShiftType = st;
                    existing.TeamGroup = team;
                }
                result.Add(new StampedCellDto(name, date, req.Clear ? "" : st));
            }
        }
        await _db.SaveChangesAsync();
        return result;
    }

    public async Task<CalendarMonthDto> GetCalendarAsync(int year, int month, bool predict)
    {
        int numDays = DateTime.DaysInMonth(year, month);
        var first = new DateOnly(year, month, 1);
        var last = new DateOnly(year, month, numDays);
        var holidayMap = _holidays.GetMap(year);

        var members = await _db.Users
            .Where(u => !u.IsResigned && u.TeamName != "")
            .Select(u => new { u.RealName, u.TeamName })
            .ToListAsync();

        var shifts = await _db.ShiftSchedules
            .Where(s => s.TargetDate >= first && s.TargetDate <= last)
            .ToListAsync();
        var manual = shifts.ToDictionary(s => (s.TargetDate, s.MemberName), s => s.ShiftType);

        var events = await _db.TeamEvents
            .Where(e => e.StartDate <= last && e.EndDate >= first)
            .OrderBy(e => e.StartDate).ToListAsync();

        var days = new List<CalendarDayDto>();
        for (int d = 1; d <= numDays; d++)
        {
            var date = new DateOnly(year, month, d);
            int dow = (int)date.DayOfWeek;
            var dayShift = new List<string>();
            var nightShift = new List<string>();
            var offShift = new List<string>();
            var eduNames = new List<string>();
            var dayOff = new List<(string name, string type)>();
            var nightOff = new List<(string name, string type)>();
            var genOff = new List<(string name, string type)>();

            // 휴무자의 기준 근무(주/야) 예측 — 뱃지 분할용
            string BaseShift(string team) =>
                team is "김팀" or "장팀" ? ShiftPredictor.Predict(team, date)
                : team is "주간팀" or "Office" ? "주간" : "";

            foreach (var m in members)
            {
                string st;
                if (manual.TryGetValue((date, m.RealName), out var ms))
                {
                    if (ms == "비우기") continue;
                    st = ms;
                }
                else if (predict && (m.TeamName == "김팀" || m.TeamName == "장팀"))
                    st = ShiftPredictor.Predict(m.TeamName, date);
                else if (predict && (m.TeamName == "주간팀" || m.TeamName == "Office"))
                    st = dow is >= 1 and <= 5 ? "주간" : "";
                else
                    continue;

                if (st == "주간") dayShift.Add(m.RealName);
                else if (st == "야간") nightShift.Add(m.RealName);
                else if (st.Contains("교육")) { eduNames.Add(m.RealName); offShift.Add($"{m.RealName}({st})"); }
                else if (st.Contains("휴무") || st.Contains("연차") || st.Contains("반차"))
                {
                    offShift.Add($"{m.RealName}({st})");
                    var bs = BaseShift(m.TeamName);
                    if (bs == "주간") dayOff.Add((m.RealName, st));
                    else if (bs == "야간") nightOff.Add((m.RealName, st));
                    else genOff.Add((m.RealName, st));
                }
            }

            static string OffTitle(IEnumerable<string> types)
            {
                var ts = types.ToList();
                bool leave = ts.Any(t => t == "연차"), half = ts.Any(t => t.Contains("반차")), off = ts.Any(t => t == "휴무");
                if (leave && !half && !off) return "연차";
                if (!leave && half && !off) return "반차";
                if (!leave && !half && off) return "휴무";
                return "휴무/연차";
            }

            var badges = new List<CalendarBadgeDto>();
            if (dayShift.Count > 0) badges.Add(new($"주간: {dayShift.Count}", "day", dayShift));
            if (nightShift.Count > 0) badges.Add(new($"야간: {nightShift.Count}", "night", nightShift));
            if (dayOff.Count > 0) badges.Add(new($"주간 {OffTitle(dayOff.Select(x => x.type))}: {dayOff.Count}", "dayoff", dayOff.Select(x => $"{x.name}({x.type})").ToList()));
            if (nightOff.Count > 0) badges.Add(new($"야간 {OffTitle(nightOff.Select(x => x.type))}: {nightOff.Count}", "nightoff", nightOff.Select(x => $"{x.name}({x.type})").ToList()));
            if (genOff.Count > 0) badges.Add(new($"{OffTitle(genOff.Select(x => x.type))}: {genOff.Count}", "off", genOff.Select(x => $"{x.name}({x.type})").ToList()));
            if (eduNames.Count > 0) badges.Add(new($"교육: {eduNames.Count}", "edu", eduNames));

            var dayEvents = events
                .Where(e => e.StartDate <= date && e.EndDate >= date)
                .Select(EventDto).ToList();

            days.Add(new CalendarDayDto(
                date, d, DayNamesKr[dow], dow == 0 || dow == 6,
                holidayMap.TryGetValue(date, out var hn) ? hn : "",
                dayShift, nightShift, offShift, badges, dayEvents));
        }
        return new CalendarMonthDto(year, month, days);
    }

    // ── 팀 일정 ──

    private static TeamEventDto EventDto(TeamEvent e) =>
        new(e.Id, e.RegisteredBy, e.StartDate, e.EndDate, e.Content, e.Detail, e.CreateDate);

    public async Task<IReadOnlyList<TeamEventDto>> GetTeamEventsAsync(int year, int month)
    {
        int numDays = DateTime.DaysInMonth(year, month);
        var first = new DateOnly(year, month, 1);
        var last = new DateOnly(year, month, numDays);
        // 해당 월과 기간이 겹치는 일정 (시작<=말일 AND 끝>=초일)
        var events = await _db.TeamEvents
            .Where(e => e.StartDate <= last && e.EndDate >= first)
            .OrderBy(e => e.StartDate)
            .ToListAsync();
        return events.Select(EventDto).ToList();
    }

    public async Task<TeamEventDto> AddTeamEventAsync(TeamEventRequest req, string actor)
    {
        var e = new TeamEvent
        {
            RegisteredBy = actor,
            StartDate = req.StartDate,
            EndDate = req.EndDate,
            Content = req.Content,
            Detail = req.Detail,
            CreateDate = DateTime.Now,
        };
        _db.TeamEvents.Add(e);
        await _db.SaveChangesAsync();
        return EventDto(e);
    }

    public async Task<TeamEventDto?> UpdateTeamEventAsync(int id, TeamEventRequest req)
    {
        var e = await _db.TeamEvents.FindAsync(id);
        if (e is null) return null;
        e.StartDate = req.StartDate;
        e.EndDate = req.EndDate;
        e.Content = req.Content;
        e.Detail = req.Detail;
        await _db.SaveChangesAsync();
        return EventDto(e);
    }

    public async Task<bool> DeleteTeamEventAsync(int id)
    {
        var e = await _db.TeamEvents.FindAsync(id);
        if (e is null) return false;
        _db.TeamEvents.Remove(e);
        await _db.SaveChangesAsync();
        return true;
    }
}
