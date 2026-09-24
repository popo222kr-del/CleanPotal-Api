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
        // 기준 주(2025-12-22 월요일)부터 이어서 센 주 번호로 2주씩 자른다.
        //
        // 예전에는 ISO 주 번호(해마다 1부터 다시)를 2로 나눴다. 2026년처럼 53주가 있는 해는 연말(W52·W53)과
        // 다음 해 W01 이 같은 근무로 3주 연속 예측되고, 그 뒤 한 해 전체가 1주씩 어긋났다.
        // 기준 주를 2026년 ISO 1주 직전 월요일로 잡아, 2026년 한 해의 예측은 예전과 똑같이 나온다.
        var weeks = FloorDiv(date.DayNumber - Anchor.DayNumber, 7);
        bool isDay = Mod(FloorDiv(weeks, 2), 2) == 0;
        if (shiftGroup == 2) isDay = !isDay;
        return isDay ? "주간" : "야간";
    }

    /// <summary>ISO 2026년 0주차 월요일. 2026년 안에서는 (여기서 센 주 번호) = (ISO 주 번호).</summary>
    private static readonly DateOnly Anchor = new(2025, 12, 22);

    private static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);
    private static int Mod(int a, int b) => ((a % b) + b) % b;
}
