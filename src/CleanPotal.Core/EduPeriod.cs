using System.Text.RegularExpressions;

namespace CleanPotal.Core;

/// <summary>
/// 교육 일자 표기 — 시작일·종료일을 WPF 와 같은 한 줄로 합친다.
///
/// WPF 의 "기본 교육 기록"은 교육내용·교육일자 2칸뿐이고, 교육일자는
/// <c>StartDate</c>/<c>EndDate</c> 를 합쳐 만든 문자열이다. 같은 달 안에서 끝나면
/// 뒤쪽은 '일'만 적는다(예: <c>2018-06-04~07</c>).
///
/// 날짜는 DB 에 문자열로 들어 있고 형식이 섞여 있을 수 있어, 파싱에 실패해도
/// 값을 잃지 않도록 원문을 그대로 이어 붙인다.
/// </summary>
public static class EduPeriod
{
    private static readonly Regex Ymd = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    /// <param name="legacy">
    /// 예전 단일 컬럼(EduDate). WPF 원본에는 없는 컬럼이라 임포트 데이터는 항상 비어 있지만,
    /// 웹에서 직접 입력한 기록이 있을 수 있어 시작·종료가 모두 없을 때만 대신 쓴다.
    /// </param>
    public static string Format(string? start, string? end, string? legacy = null)
    {
        var s = (start ?? "").Trim();
        var e = (end ?? "").Trim();

        if (s.Length == 0 && e.Length == 0) return (legacy ?? "").Trim();
        if (s.Length == 0) return e;
        if (e.Length == 0 || string.Equals(s, e, StringComparison.Ordinal)) return s;

        // yyyy-MM-dd 형식일 때만 줄여 쓴다. 그 외에는 원문을 그대로 붙인다.
        if (Ymd.IsMatch(s) && Ymd.IsMatch(e))
        {
            if (s[..8] == e[..8]) return $"{s}~{e[8..]}";      // 같은 연·월 → 일만  (2018-06-04~07)
            if (s[..5] == e[..5]) return $"{s}~{e[5..]}";      // 같은 연   → 월-일  (2018-06-28~07-02)
        }
        return $"{s}~{e}";
    }
}
