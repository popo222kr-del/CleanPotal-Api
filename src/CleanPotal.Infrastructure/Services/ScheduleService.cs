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
    public ScheduleService(CleanPotalDbContext db) => _db = db;

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

        var days = new List<RosterDayHeaderDto>();
        for (int d = 1; d <= numDays; d++)
        {
            var dt = new DateOnly(year, month, d);
            int dow = (int)dt.DayOfWeek;
            bool weekend = dow == 0 || dow == 6;
            days.Add(new RosterDayHeaderDto(d, DayNamesKr[dow], weekend, false));
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
