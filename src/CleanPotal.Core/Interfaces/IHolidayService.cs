using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IHolidayService
{
    /// <summary>해당 연도 공휴일 목록.</summary>
    IReadOnlyList<HolidayDto> GetByYear(int year);

    /// <summary>해당 연도 공휴일 맵 (날짜 → 이름). 빠른 조회용.</summary>
    IReadOnlyDictionary<DateOnly, string> GetMap(int year);

    bool IsHoliday(DateOnly date);
}
