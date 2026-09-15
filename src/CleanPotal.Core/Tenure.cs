using System.Globalization;

namespace CleanPotal.Core;

/// <summary>
/// 입사일 → 경력 표기 ("8년 3개월").
///
/// 입사일은 DB 에 문자열로 들어 있고 WPF 시절 표기가 섞여 있어(2018-06-01 / 2018.06.01 …)
/// 몇 가지 형식을 함께 받아들인다. 해석하지 못하면 빈 문자열을 돌려주고, 화면은 "-" 로 둔다
/// (아무 숫자나 만들어 보여주면 틀린 경력이 사실처럼 보인다).
/// </summary>
public static class Tenure
{
    private static readonly string[] Formats =
    {
        "yyyy-MM-dd", "yyyy.MM.dd", "yyyy/MM/dd", "yyyyMMdd",
        "yyyy-M-d", "yyyy.M.d", "yyyy/M/d",
    };

    /// <summary>입사일 문자열을 날짜로. 해석 불가면 null.</summary>
    public static DateOnly? ParseHireDate(string? hireDate)
    {
        var s = (hireDate ?? "").Trim();
        if (s.Length == 0) return null;
        return DateOnly.TryParseExact(s, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var loose) ? loose : null;
    }

    /// <param name="asOf">기준일. 테스트에서 고정하려고 받는다. 비우면 오늘.</param>
    public static string Format(string? hireDate, DateOnly? asOf = null)
    {
        var hire = ParseHireDate(hireDate);
        if (hire is null) return "";

        var today = asOf ?? DateOnly.FromDateTime(DateTime.Today);
        if (hire > today) return "입사 예정";

        // 월 단위로 센 뒤 12로 나눈다. 아직 그 달의 '일'에 도달하지 않았으면 한 달 빼준다.
        int months = (today.Year - hire.Value.Year) * 12 + (today.Month - hire.Value.Month);
        if (today.Day < hire.Value.Day) months--;
        if (months < 0) months = 0;

        int years = months / 12, restMonths = months % 12;
        if (years == 0 && restMonths == 0) return "1개월 미만";
        if (years == 0) return $"{restMonths}개월";
        if (restMonths == 0) return $"{years}년";
        return $"{years}년 {restMonths}개월";
    }
}
