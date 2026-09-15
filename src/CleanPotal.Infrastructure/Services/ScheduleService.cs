using CleanPotal.Core;
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

    /// <summary>교대 생산팀 목록·조 번호. 조직도에서 읽으므로 팀 이름을 바꿔도 따라온다.</summary>
    private Task<ProductionTeams> LoadTeamsAsync() => ProductionTeams.LoadAsync(_db);
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
        // 검증 없이 DateTime.DaysInMonth 를 부르면 ArgumentOutOfRangeException → 500 이 나간다.
        // 잘못된 연/월은 사용자가 고칠 수 있는 입력 오류이므로 400 으로 돌려준다.
        if (year is < 2000 or > 2100)
            throw new BusinessRuleException("연도는 2000~2100 사이여야 합니다.");
        if (month is < 1 or > 12)
            throw new BusinessRuleException("월은 1~12 사이여야 합니다.");

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

        var pt = await LoadTeamsAsync();
        var targetTeams = teamFilter == "전체" ? pt.Names.ToArray() : new[] { teamFilter };

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
                        st = "예상:" + pt.PredictShift(team, date);
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

    /// <summary>근무표에 찍을 수 있는 도장 종류 — 화면(STAMP_TYPES)과 같은 목록.</summary>
    private static readonly HashSet<string> AllowedShiftTypes =
        new(StringComparer.Ordinal) { "주간", "야간", "반차", "반반차", "휴무", "연차", "특근", "교육" };

    private const int MaxStampMembers = 200;   // 한 번에 처리할 대상자 상한
    private const int MaxStampDays = 31;       // 한 번에 찍을 수 있는 최대 일수

    public async Task<IReadOnlyList<StampedCellDto>> StampAsync(StampShiftRequest req, string actorName)
    {
        // ── 입력 검증 (잘못된 입력은 400 으로) ───────────────────────────────
        var names = (req.Members ?? Array.Empty<string>())
            .Select(n => (n ?? "").Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.Ordinal)          // 중복 대상자 제거
            .ToList();
        if (names.Count == 0)
            throw new BusinessRuleException("대상자를 선택하세요.");
        if (names.Count > MaxStampMembers)
            throw new BusinessRuleException($"한 번에 처리할 수 있는 대상자는 {MaxStampMembers}명까지입니다.");

        int repeat = req.Clear ? 1 : req.Days;
        if (repeat < 1 || repeat > MaxStampDays)
            throw new BusinessRuleException($"일수는 1~{MaxStampDays} 사이여야 합니다.");

        string st = req.Clear ? "비우기" : (req.ShiftType ?? "").Trim();
        if (!req.Clear && !AllowedShiftTypes.Contains(st))
            throw new BusinessRuleException($"사용할 수 없는 근무 표시입니다: {st}");

        if (req.StartDate.Year is < 2000 or > 2100)
            throw new BusinessRuleException("날짜 범위가 올바르지 않습니다.");

        // ── 필요한 사용자/기존 근무표를 각각 한 번씩만 조회 (기존 N+1 제거) ──
        var teamByName = await _db.Users
            .Where(u => names.Contains(u.RealName))
            .GroupBy(u => u.RealName)
            .Select(g => new { Name = g.Key, Team = g.Select(x => x.TeamName).First() })
            .ToDictionaryAsync(x => x.Name, x => x.Team ?? "");

        var unknown = names.Where(n => !teamByName.ContainsKey(n)).ToList();
        if (unknown.Count > 0)
            throw new BusinessRuleException($"직원 목록에 없는 대상자입니다: {string.Join(", ", unknown)}");

        var dates = Enumerable.Range(0, repeat).Select(i => req.StartDate.AddDays(i)).ToList();
        var lastDate = dates[^1];

        var existingRows = await _db.ShiftSchedules
            .Where(s => names.Contains(s.MemberName)
                        && s.TargetDate >= req.StartDate && s.TargetDate <= lastDate)
            .ToListAsync();
        var existingMap = existingRows
            .GroupBy(s => (s.MemberName, s.TargetDate))
            .ToDictionary(g => g.Key, g => g.First());

        // ── 메모리에서 일괄 반영 후 한 번에 저장 ─────────────────────────────
        var result = new List<StampedCellDto>(names.Count * repeat);
        foreach (var name in names)
        {
            var team = teamByName[name];
            foreach (var date in dates)
            {
                if (existingMap.TryGetValue((name, date), out var existing))
                {
                    existing.ShiftType = st;
                    existing.TeamGroup = team;
                }
                else
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
                result.Add(new StampedCellDto(name, date, req.Clear ? "" : st));
            }
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // (MemberName, TargetDate) 고유 인덱스 충돌 — 다른 요청이 동시에 같은 칸을 찍은 경우.
            // SaveChanges 는 트랜잭션이라 이번 요청의 추가분이 전부 롤백된 상태이므로,
            // 현재 DB 상태를 다시 읽어 "있으면 갱신 / 없으면 추가"를 다시 계산한다.
            foreach (var entry in _db.ChangeTracker.Entries<ShiftSchedule>().ToList())
                entry.State = EntityState.Detached;

            var reloaded = await _db.ShiftSchedules
                .Where(s => names.Contains(s.MemberName)
                            && s.TargetDate >= req.StartDate && s.TargetDate <= lastDate)
                .ToListAsync();
            var nowMap = reloaded
                .GroupBy(s => (s.MemberName, s.TargetDate))
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var name in names)
            {
                var team = teamByName[name];
                foreach (var date in dates)
                {
                    if (nowMap.TryGetValue((name, date), out var row))
                    {
                        row.ShiftType = st;
                        row.TeamGroup = team;
                    }
                    else
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
                }
            }
            await _db.SaveChangesAsync();   // 재시도도 실패하면 그대로 예외를 올린다(500)
        }
        return result;
    }

    public async Task<int> RegisterAttendanceAsync(AttendanceRequest req, string actorName)
    {
        var name = (req.MemberName ?? "").Trim();
        if (name.Length == 0 || string.IsNullOrWhiteSpace(req.ShiftType) || req.StartDate > req.EndDate) return 0;

        var team = await _db.Users
            .Where(u => u.RealName == name && !u.IsResigned)
            .Select(u => u.TeamName)
            .FirstOrDefaultAsync();
        if (team is null) return 0;   // 직원 목록에 없는 이름 차단 (WPF와 동일)

        var holidays = new HashSet<DateOnly>();
        for (int y = req.StartDate.Year; y <= req.EndDate.Year; y++)
            foreach (var d in _holidays.GetMap(y).Keys) holidays.Add(d);

        int count = 0;
        for (var dt = req.StartDate; dt <= req.EndDate; dt = dt.AddDays(1))
        {
            if (dt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || holidays.Contains(dt)) continue;
            var existing = await _db.ShiftSchedules
                .FirstOrDefaultAsync(s => s.MemberName == name && s.TargetDate == dt);
            if (existing is null)
            {
                _db.ShiftSchedules.Add(new ShiftSchedule
                {
                    MemberName = name,
                    TargetDate = dt,
                    ShiftType = req.ShiftType,
                    TeamGroup = team,
                    CreatorName = actorName,
                    CreateDate = DateTime.Now,
                });
            }
            else
            {
                existing.ShiftType = req.ShiftType;
                existing.TeamGroup = team;
            }
            count++;
        }
        if (count > 0) await _db.SaveChangesAsync();
        return count;
    }

    // 근태 등록 인원 목록은 '비교대 팀 먼저, 생산팀 나중' 순서다.
    // 예전에는 { Office, 주간팀, 장팀, 김팀 } 처럼 이름을 박아 두어 팀 이름을 바꾸면 정렬이 무너졌다.

    public async Task<IReadOnlyList<ScheduleMemberDto>> GetMembersAsync()
    {
        var users = await _db.Users
            .Where(u => !u.IsResigned && u.RealName != "")
            .Select(u => new { u.RealName, u.TeamName })
            .ToListAsync();
        var pt = await LoadTeamsAsync();
        return users
            .OrderBy(u => pt.IsProduction(u.TeamName) ? 1 : 0)    // 비교대 팀 먼저
            .ThenBy(u => pt.GroupOf(u.TeamName))                  // 생산팀은 1조 → 2조
            .ThenBy(u => u.TeamName, StringComparer.Ordinal)
            .ThenBy(u => u.RealName, StringComparer.Ordinal)
            .Select(u => new ScheduleMemberDto(u.RealName, u.TeamName))
            .ToList();
    }

    /// <summary>교대 생산팀 이름(1조 → 2조).</summary>
    public async Task<IReadOnlyList<string>> GetProductionTeamsAsync()
        => (await LoadTeamsAsync()).Names;

    public IReadOnlyList<string> GetHolidays(int year)
        => _holidays.GetMap(year).Keys.OrderBy(d => d).Select(d => d.ToString("yyyy-MM-dd")).ToList();

    public async Task<CalendarMonthDto> GetCalendarAsync(int year, int month, bool predict)
    {
        int numDays = DateTime.DaysInMonth(year, month);
        var first = new DateOnly(year, month, 1);
        var last = new DateOnly(year, month, numDays);
        var holidayMap = _holidays.GetMap(year);

        // 달력 인원 집계는 교대 생산팀만 — 주간팀/Office는 제외
        var pt = await LoadTeamsAsync();
        var productionTeams = pt.Names.ToList();
        var members = await _db.Users
            .Where(u => !u.IsResigned && productionTeams.Contains(u.TeamName))
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

            foreach (var m in members)
            {
                string st;
                if (manual.TryGetValue((date, m.RealName), out var ms))
                {
                    if (ms == "비우기") continue;
                    st = ms;
                }
                else if (predict)
                    st = pt.PredictShift(m.TeamName, date);
                else
                    continue;

                if (st == "주간") dayShift.Add(m.RealName);
                else if (st == "야간") nightShift.Add(m.RealName);
                else if (st.Contains("교육")) { eduNames.Add(m.RealName); offShift.Add($"{m.RealName}({st})"); }
                else if (st.Contains("휴무") || st.Contains("연차") || st.Contains("반차"))
                {
                    offShift.Add($"{m.RealName}({st})");
                    var bs = pt.PredictShift(m.TeamName, date);
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
            // 상단 교대조 바: 색은 팀 고정(team0/team1) + 인원수 병기 (주간(장팀) 7). 아래는 휴무/연차만.
            {
                string dayTeam = "", nightTeam = "";
                foreach (var team in pt.Names)
                {
                    var ts = pt.PredictShift(team, date);
                    if (ts == "주간") dayTeam = team;
                    else if (ts == "야간") nightTeam = team;
                }
                // 색은 교대 기준: 주간=주황, 야간=파랑. 팀명은 글씨로 표기.
                if (dayTeam != "" || dayShift.Count > 0)
                    badges.Add(new(dayTeam != "" ? $"주간({dayTeam}) {dayShift.Count}" : $"주간 {dayShift.Count}", "sday", dayShift));
                if (nightTeam != "" || nightShift.Count > 0)
                    badges.Add(new(nightTeam != "" ? $"야간({nightTeam}) {nightShift.Count}" : $"야간 {nightShift.Count}", "snight", nightShift));

                // 휴무/연차 뱃지 — ■ 색을 교대 색과 동일하게
                if (dayOff.Count > 0) badges.Add(new($"주간 {OffTitle(dayOff.Select(x => x.type))}: {dayOff.Count}", "offday", dayOff.Select(x => $"{x.name}({x.type})").ToList()));
                if (nightOff.Count > 0) badges.Add(new($"야간 {OffTitle(nightOff.Select(x => x.type))}: {nightOff.Count}", "offnight", nightOff.Select(x => $"{x.name}({x.type})").ToList()));
            }
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

    // ── 오늘의 세정팀 현황 (인수인계 대시보드) ──

    public async Task<TodayStatusDto> GetTodayStatusAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var members = await _db.Users
            .Where(u => !u.IsResigned && u.TeamName != "")
            .Select(u => new { u.RealName, u.TeamName })
            .ToListAsync();

        var shifts = await _db.ShiftSchedules
            .Where(s => s.TargetDate == today)
            .ToListAsync();
        var manual = shifts.ToDictionary(s => s.MemberName, s => s.ShiftType);

        // 표시 순서: 교대 생산팀 먼저, 그다음 나머지 팀(이름순).
        // 예전에는 { 김팀, 장팀, 주간팀, Office } 로 박아 두어 팀 이름을 바꾸면 화면에서 사라졌다.
        var pt = await LoadTeamsAsync();
        var teamNames = pt.Names
            .Concat(members.Select(m => m.TeamName).Distinct().Where(t => !pt.IsProduction(t)).OrderBy(t => t, StringComparer.Ordinal))
            .ToList();

        var teams = new List<TeamTodayDto>();
        foreach (var team in teamNames)
        {
            var day = new List<string>();
            var night = new List<string>();
            var off = new List<string>();
            var edu = new List<string>();

            foreach (var m in members.Where(m => m.TeamName == team))
            {
                string st;
                if (manual.TryGetValue(m.RealName, out var ms))
                {
                    if (ms == "비우기") continue;
                    st = ms;
                }
                // 교대 생산팀은 예측(주/야 로테이션), 그 외 팀은 실제 도장만 표시
                // (WPF '오늘의 세정팀 현황'과 동일 — 근무표 달력에서 찍은 데이터를 그대로 공유)
                else if (pt.IsProduction(team)) st = pt.PredictShift(team, today);
                else continue;

                if (st == "주간") day.Add(m.RealName);
                else if (st == "야간") night.Add(m.RealName);
                else if (st.Contains("교육")) edu.Add($"{m.RealName}");
                else if (st.Contains("휴무") || st.Contains("연차") || st.Contains("반차"))
                    off.Add(st == "휴무" ? m.RealName : $"{m.RealName}({st})");
            }

            var badges = new List<CalendarBadgeDto>();
            if (day.Count > 0) badges.Add(new($"주간 {day.Count}", "day", day));
            if (night.Count > 0) badges.Add(new($"야간 {night.Count}", "night", night));
            if (off.Count > 0) badges.Add(new($"휴무 {off.Count}", "off", off));
            if (edu.Count > 0) badges.Add(new($"교육 {edu.Count}", "edu", edu));
            teams.Add(new TeamTodayDto(team, badges));
        }

        var upEvents = await _db.TeamEvents
            .Where(e => e.EndDate >= today)
            .OrderBy(e => e.StartDate)
            .Take(8)
            .ToListAsync();

        var limit = today.AddDays(7);
        var upEdu = await _db.EducationPlans
            .Where(e => e.StartDate != null && e.StartDate >= today && e.StartDate <= limit
                        && e.Status != "완료" && e.Status != "취소")
            .OrderBy(e => e.StartDate)
            .ToListAsync();

        return new TodayStatusDto(
            today,
            teams,
            upEvents.Select(EventDto).ToList(),
            upEdu.Select(e => new UpcomingEduDto(e.MemberName, e.CourseName, e.StartDate, e.EndDate, e.EduMethod)).ToList());
    }

    /// <summary>특정 날짜의 주간/야간 근무 팀 (WPF UpdateShiftTeamLabels).
    /// 도장이 없으면 교대 예측으로 판단.</summary>
    public async Task<ShiftTeamsDto> GetShiftTeamsAsync(DateOnly date)
    {
        var shifts = await _db.ShiftSchedules
            .Where(s => s.TargetDate == date && s.TeamGroup != "")
            .Select(s => new { s.TeamGroup, s.ShiftType })
            .ToListAsync();

        var day = shifts.Where(s => s.ShiftType == "주간").Select(s => s.TeamGroup).Distinct().ToList();
        var night = shifts.Where(s => s.ShiftType == "야간").Select(s => s.TeamGroup).Distinct().ToList();

        // 도장 데이터가 없으면 교대 예측으로 채움
        if (day.Count == 0 && night.Count == 0)
        {
            var pt = await LoadTeamsAsync();
            foreach (var team in pt.Names)
            {
                var st = pt.PredictShift(team, date);
                if (st == "주간") day.Add(team);
                else if (st == "야간") night.Add(team);
            }
        }
        return new ShiftTeamsDto(day, night);
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
