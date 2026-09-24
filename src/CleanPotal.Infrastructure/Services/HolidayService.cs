using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// 한국 공휴일 (2025~2027 하드코딩).
///
/// 대체공휴일 규칙: 설날·추석은 연휴에 일요일이 끼면, 어린이날은 토·일이면 다음 평일을 쉰다.
/// 2021년부터 삼일절·광복절·개천절·한글날, 2023년부터 부처님오신날·성탄절도 토·일이면 대체공휴일이다.
/// 신정·현충일은 대체공휴일이 없다. 공직선거 선거일도 공휴일이다.
///
/// 2028년 이후는 아직 비어 있다 — 매년 6월 무렵 발표되는 다음 해 월력요항(한국천문연구원)을 보고 추가한다.
/// 연도가 비어 있으면 그해 공휴일이 하나도 없는 것으로 계산되므로(근태 연차 차감에 영향) 해가 바뀌기 전에 채운다.
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
