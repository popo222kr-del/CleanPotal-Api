using System.Globalization;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// 2주 단위 주간/야간 자동 교대 예측 (기존 WPF predict_shift 로직).
/// 김팀/장팀은 항상 반대 조.
/// </summary>
public static class ShiftPredictor
{
    public static string Predict(string team, DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        int week = ISOWeek.GetWeekOfYear(dt);
        bool isDay = (week / 2) % 2 == 0;
        if (team == "장팀") isDay = !isDay;
        return isDay ? "주간" : "야간";
    }
}
