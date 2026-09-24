using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 공휴일 표. 근태 등록이 공휴일을 연차일에서 빼므로 날짜가 틀리면 연차가 잘못 차감된다.
/// 예전 표는 2027 추석을 10/13~15 로 적어 두었고, 2026 대체공휴일·지방선거가 빠져 있었다.
/// </summary>
public class HolidayServiceTests
{
    private static readonly HolidayService Svc = new();

    [Theory]
    [InlineData(2026, 5, 25)]   // 부처님오신날(일) 대체
    [InlineData(2026, 6, 3)]    // 지방선거
    [InlineData(2026, 8, 17)]   // 광복절(토) 대체
    [InlineData(2026, 10, 5)]   // 개천절(토) 대체
    [InlineData(2027, 2, 9)]    // 설 연휴 일요일 대체
    [InlineData(2027, 9, 14)]   // 추석
    [InlineData(2027, 9, 15)]
    [InlineData(2027, 9, 16)]
    [InlineData(2027, 10, 11)]  // 한글날(토) 대체
    [InlineData(2027, 12, 27)]  // 성탄절(토) 대체
    [InlineData(2025, 10, 8)]   // 추석 연휴 일요일 대체
    public void 쉬는_날이다(int y, int m, int d)
        => Assert.True(Svc.IsHoliday(new DateOnly(y, m, d)));

    [Theory]
    [InlineData(2027, 10, 13)]  // 예전 표의 잘못된 추석
    [InlineData(2027, 10, 14)]
    [InlineData(2027, 10, 15)]
    public void 쉬는_날이_아니다(int y, int m, int d)
        => Assert.False(Svc.IsHoliday(new DateOnly(y, m, d)));

    [Theory]
    [InlineData(2025)]
    [InlineData(2026)]
    [InlineData(2027)]
    public void 대체공휴일은_모두_평일이다(int year)
    {
        foreach (var h in Svc.GetByYear(year).Where(h => h.Name == "대체공휴일"))
            Assert.True(h.Date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday, $"{h.Date}");
    }
}
