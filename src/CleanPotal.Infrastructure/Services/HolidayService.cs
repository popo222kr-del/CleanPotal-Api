using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// 한국 공휴일. 코드에 적힌 기본 목록(2025~2027) 위에 관리자가 화면에서 고친 날(HolidayOverrides 표)을 덮어쓴다.
///
/// 대체공휴일 규칙: 설날·추석은 연휴에 일요일이 끼면, 어린이날은 토·일이면 다음 평일을 쉰다.
/// 2021년부터 삼일절·광복절·개천절·한글날, 2023년부터 부처님오신날·성탄절도 토·일이면 대체공휴일이다.
/// 신정·현충일은 대체공휴일이 없다. 공직선거 선거일도 공휴일이다.
///
/// 2028년 이후 기본 목록은 비어 있다 — 매년 6월 무렵 발표되는 다음 해 월력요항(한국천문연구원)을 보고
/// 관리자 화면(공휴일 관리)에서 넣는다. 임시공휴일도 같은 화면에서 넣는다.
/// 연도가 비어 있으면 그해 공휴일이 하나도 없는 것으로 계산되므로(근태 연차 차감에 영향) 해가 바뀌기 전에 채운다.
/// </summary>
public class HolidayService : IHolidayService
{
    private static readonly Dictionary<int, Dictionary<DateOnly, string>> Data = Build();
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory? _scopes;
    private readonly object _gate = new();
    private IReadOnlyList<HolidayOverride>? _overrides;
    private DateTime _loadedAtUtc;

    /// <summary>기본 목록만 쓴다(테스트·DB 없는 곳).</summary>
    public HolidayService() { }

    /// <summary>기본 목록 + DB 에 저장된 관리자 수정분.</summary>
    public HolidayService(IServiceScopeFactory scopes) => _scopes = scopes;

    public IReadOnlyList<HolidayDto> GetByYear(int year)
        => GetMap(year).Select(kv => new HolidayDto(kv.Key, kv.Value))
                       .OrderBy(h => h.Date).ToList();

    public IReadOnlyDictionary<DateOnly, string> GetMap(int year)
    {
        var map = Data.TryGetValue(year, out var m)
            ? new Dictionary<DateOnly, string>(m)
            : new Dictionary<DateOnly, string>();
        foreach (var o in Overrides())
        {
            if (o.Date.Year != year) continue;
            if (o.IsOff) map[o.Date] = o.Name;
            else map.Remove(o.Date);
        }
        return map;
    }

    public bool IsHoliday(DateOnly date) => GetMap(date.Year).ContainsKey(date);

    public IReadOnlyDictionary<DateOnly, string> GetBuiltInMap(int year)
        => Data.TryGetValue(year, out var m) ? m : new Dictionary<DateOnly, string>();

    public void InvalidateOverrides()
    {
        _loadedAtUtc = DateTime.MinValue;   // 다음 조회 때 다시 읽는다(그 전까지는 직전 값을 쓴다)
    }

    /// <summary>관리자 수정분. 5분 동안 기억하고, 화면에서 고치면 바로 지운다(InvalidateOverrides).</summary>
    private IReadOnlyList<HolidayOverride> Overrides()
    {
        if (_scopes is null) return Array.Empty<HolidayOverride>();
        var cached = _overrides;
        if (cached is not null && DateTime.UtcNow - _loadedAtUtc < CacheFor) return cached;

        // 한 스레드만 다시 읽는다. 그동안 다른 요청은 기다리지 않고 직전 값을 쓴다 —
        // DB 가 느리거나 안 닿을 때 휴일을 보는 요청이 전부 줄 서지 않게.
        if (!Monitor.TryEnter(_gate))
            return cached ?? Array.Empty<HolidayOverride>();
        try
        {
            if (_overrides is not null && DateTime.UtcNow - _loadedAtUtc < CacheFor) return _overrides;
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CleanPotalDbContext>();
                _overrides = db.HolidayOverrides.AsNoTracking().ToList();
            }
            catch (Exception ex)
            {
                // 표가 아직 없거나 DB 가 잠깐 안 닿아도 기본 목록으로는 계속 돌아가야 한다. 1분 뒤 다시 읽는다.
                Console.WriteLine($"[holiday][경고] 공휴일 수정분을 읽지 못해 기본 목록만 씁니다: {ex.Message}");
                _overrides = Array.Empty<HolidayOverride>();
                _loadedAtUtc = DateTime.UtcNow - CacheFor + TimeSpan.FromMinutes(1);
                return _overrides;
            }
            _loadedAtUtc = DateTime.UtcNow;
            return _overrides;
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private static Dictionary<int, Dictionary<DateOnly, string>> Build()
    {
        DateOnly D(int y, int m, int d) => new(y, m, d);
        var result = new Dictionary<int, Dictionary<DateOnly, string>>();

        result[2025] = new()
        {
            [D(2025,1,1)]="신정", [D(2025,1,27)]="임시공휴일",
            [D(2025,1,28)]="설날", [D(2025,1,29)]="설날", [D(2025,1,30)]="설날",
            [D(2025,3,1)]="삼일절", [D(2025,3,3)]="대체공휴일",
            [D(2025,5,5)]="어린이날·부처님오신날", [D(2025,5,6)]="대체공휴일",
            [D(2025,6,3)]="대통령선거", [D(2025,6,6)]="현충일", [D(2025,8,15)]="광복절",
            [D(2025,10,3)]="개천절", [D(2025,10,5)]="추석", [D(2025,10,6)]="추석", [D(2025,10,7)]="추석",
            [D(2025,10,8)]="대체공휴일", [D(2025,10,9)]="한글날", [D(2025,12,25)]="크리스마스",
        };
        result[2026] = new()
        {
            [D(2026,1,1)]="신정", [D(2026,2,16)]="설날", [D(2026,2,17)]="설날", [D(2026,2,18)]="설날",
            [D(2026,3,1)]="삼일절", [D(2026,3,2)]="대체공휴일",
            [D(2026,5,5)]="어린이날", [D(2026,5,24)]="부처님오신날", [D(2026,5,25)]="대체공휴일",
            [D(2026,6,3)]="지방선거", [D(2026,6,6)]="현충일",
            [D(2026,8,15)]="광복절", [D(2026,8,17)]="대체공휴일",
            [D(2026,9,24)]="추석", [D(2026,9,25)]="추석", [D(2026,9,26)]="추석",
            [D(2026,10,3)]="개천절", [D(2026,10,5)]="대체공휴일",
            [D(2026,10,9)]="한글날", [D(2026,12,25)]="크리스마스",
        };
        result[2027] = new()
        {
            [D(2027,1,1)]="신정", [D(2027,2,6)]="설날", [D(2027,2,7)]="설날", [D(2027,2,8)]="설날",
            [D(2027,2,9)]="대체공휴일",
            [D(2027,3,1)]="삼일절", [D(2027,5,5)]="어린이날", [D(2027,5,13)]="부처님오신날",
            [D(2027,6,6)]="현충일", [D(2027,8,15)]="광복절", [D(2027,8,16)]="대체공휴일",
            [D(2027,9,14)]="추석", [D(2027,9,15)]="추석", [D(2027,9,16)]="추석",
            [D(2027,10,3)]="개천절", [D(2027,10,4)]="대체공휴일",
            [D(2027,10,9)]="한글날", [D(2027,10,11)]="대체공휴일",
            [D(2027,12,25)]="크리스마스", [D(2027,12,27)]="대체공휴일",
        };
        return result;
    }
}
