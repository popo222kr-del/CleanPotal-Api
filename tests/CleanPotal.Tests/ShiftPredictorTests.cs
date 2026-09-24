using System.Globalization;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 2주 교대 예측. 예전 공식(ISO 주 번호 ÷ 2)은 53주가 있는 해의 연말에 같은 근무를 3주 이어 주고
/// 다음 해 전체를 1주씩 밀었다.
/// </summary>
public class ShiftPredictorTests
{
    private static string Old(int group, DateOnly d)
    {
        var week = ISOWeek.GetWeekOfYear(d.ToDateTime(TimeOnly.MinValue));
        var isDay = (week / 2) % 2 == 0;
        if (group == 2) isDay = !isDay;
        return isDay ? "주간" : "야간";
    }

    [Fact]
    public void 올해_2026_예측은_예전과_같다()
    {
        for (var d = new DateOnly(2026, 1, 1); d.Year == 2026; d = d.AddDays(1))
        {
            if (ISOWeek.GetYear(d.ToDateTime(TimeOnly.MinValue)) != 2026) continue;   // 1월 1일 등 전년도 ISO 주는 제외
            Assert.Equal(Old(1, d), ShiftPredictor.Predict(1, d));
        }
    }

    [Fact]
    public void 해가_바뀌어도_2주마다_번갈아_든다()
    {
        // 2026-11-30(월)부터 12주 동안, 월요일마다 본 근무가 정확히 2주씩 번갈아 나와야 한다.
        var monday = new DateOnly(2026, 11, 30);
        var runs = new List<int>();
        string? prev = null;
        for (var i = 0; i < 12; i++)
        {
            var shift = ShiftPredictor.Predict(1, monday.AddDays(7 * i));
            if (shift == prev) runs[^1]++; else runs.Add(1);
            prev = shift;
        }
        Assert.All(runs.Skip(1).SkipLast(1), r => Assert.Equal(2, r));
        Assert.True(runs.Max() <= 2, "같은 근무가 3주 이상 이어지면 안 된다.");
    }

    [Fact]
    public void 조가_다르면_늘_반대다()
    {
        for (var d = new DateOnly(2025, 6, 1); d < new DateOnly(2028, 6, 1); d = d.AddDays(3))
            Assert.NotEqual(ShiftPredictor.Predict(1, d), ShiftPredictor.Predict(2, d));
    }
}
