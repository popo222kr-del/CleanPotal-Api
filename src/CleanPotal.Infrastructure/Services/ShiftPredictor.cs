using System.Globalization;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// 2주 단위 주간/야간 자동 교대 예측 (기존 WPF predict_shift 로직).
/// 1조와 2조는 항상 반대 근무다.
/// </summary>
public static class ShiftPredictor
{
    /// <param name="shiftGroup">
    /// 교대 조(1 또는 2). 예전에는 팀 이름("장팀")으로 판단해서 팀 이름을 바꾸면
    /// 두 팀이 같은 조로 예측되는 문제가 있었다.
    /// </param>
    public static string Predict(int shiftGroup, DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        int week = ISOWeek.GetWeekOfYear(dt);
        bool isDay = (week / 2) % 2 == 0;
        if (shiftGroup == 2) isDay = !isDay;
        return isDay ? "주간" : "야간";
    }
}
