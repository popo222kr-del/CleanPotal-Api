using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// 한국 공휴일 (2025~2027 하드코딩, WPF/Flask 데이터 이식).
/// 추후 공공데이터포털 API 연동으로 교체 가능.
/// </summary>
public class HolidayService : IHolidayService
{
    private static readonly Dictionary<int, Dictionary<DateOnly, string>> Data = Build();

    public IReadOnlyList<HolidayDto> GetByYear(int year)
        => GetMap(year).Select(kv => new HolidayDto(kv.Key, kv.Value))
                       .OrderBy(h => h.Date).ToList();

    public IReadOnlyDictionary<DateOnly, string> GetMap(int year)
        => Data.TryGetValue(year, out var m) ? m : new Dictionary<DateOnly, string>();

    public bool IsHoliday(DateOnly date)
        => Data.TryGetValue(date.Year, out var m) && m.ContainsKey(date);

    private static Dictionary<int, Dictionary<DateOnly, string>> Build()
    {
        DateOnly D(int y, int m, int d) => new(y, m, d);
        var result = new Dictionary<int, Dictionary<DateOnly, string>>();

        result[2025] = new()
        {
            [D(2025,1,1)]="신정", [D(2025,1,28)]="설날", [D(2025,1,29)]="설날", [D(2025,1,30)]="설날",
            [D(2025,3,1)]="삼일절", [D(2025,5,5)]="어린이날", [D(2025,5,6)]="대체공휴일",
            [D(2025,6,6)]="현충일", [D(2025,8,15)]="광복절", [D(2025,10,3)]="개천절",
            [D(2025,10,5)]="추석", [D(2025,10,6)]="추석", [D(2025,10,7)]="추석",
            [D(2025,10,9)]="한글날", [D(2025,12,25)]="크리스마스",
        };
        result[2026] = new()
        {
            [D(2026,1,1)]="신정", [D(2026,2,16)]="설날", [D(2026,2,17)]="설날", [D(2026,2,18)]="설날",
            [D(2026,3,1)]="삼일절", [D(2026,3,2)]="대체공휴일",
            [D(2026,5,5)]="어린이날", [D(2026,5,24)]="부처님오신날",
            [D(2026,6,6)]="현충일", [D(2026,8,15)]="광복절", [D(2026,9,24)]="추석",
            [D(2026,9,25)]="추석", [D(2026,9,26)]="추석", [D(2026,10,3)]="개천절",
            [D(2026,10,9)]="한글날", [D(2026,12,25)]="크리스마스",
        };
        result[2027] = new()
        {
            [D(2027,1,1)]="신정", [D(2027,2,6)]="설날", [D(2027,2,7)]="설날", [D(2027,2,8)]="설날",
            [D(2027,3,1)]="삼일절", [D(2027,5,5)]="어린이날", [D(2027,5,13)]="부처님오신날",
            [D(2027,6,6)]="현충일", [D(2027,8,15)]="광복절", [D(2027,10,3)]="개천절",
            [D(2027,10,9)]="한글날", [D(2027,10,13)]="추석", [D(2027,10,14)]="추석",
            [D(2027,10,15)]="추석", [D(2027,12,25)]="크리스마스",
        };
        return result;
    }
}
