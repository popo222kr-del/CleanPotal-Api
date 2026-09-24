using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IHolidayService
{
    /// <summary>해당 연도 공휴일 목록.</summary>
    IReadOnlyList<HolidayDto> GetByYear(int year);

    /// <summary>해당 연도 공휴일 맵 (날짜 → 이름). 빠른 조회용.</summary>
    IReadOnlyDictionary<DateOnly, string> GetMap(int year);

    bool IsHoliday(DateOnly date);

    /// <summary>코드에 적힌 기본 목록(관리자 수정분을 뺀 것).</summary>
    IReadOnlyDictionary<DateOnly, string> GetBuiltInMap(int year);

    /// <summary>관리자가 공휴일을 고친 뒤 부른다 — 다음 조회부터 새 값을 읽는다.</summary>
    void InvalidateOverrides();
}
