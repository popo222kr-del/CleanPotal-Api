namespace CleanPotal.Core;

/// <summary>
/// 업체명 표기 통일(별칭 → 대표명).
/// WPF 시절 자유입력으로 생긴 표기 흔들림(국제 / 국제엘레트릭코리아 / 국제엘렉트릭코리아 …)을
/// 하나의 대표명으로 모은다. 데이터 임포트(refresh-from-wpf) 시 자동 적용되며,
/// 필요하면 신규 저장 시에도 재사용할 수 있도록 Core 에 둔다.
/// 새 별칭이 발견되면 아래 사전에 "별칭" = "대표명" 한 줄만 추가하면 된다.
/// </summary>
public static class VendorNames
{
    // 키(별칭, 대소문자·앞뒤공백 무시) → 값(대표명)
    private static readonly Dictionary<string, string> Alias = new(StringComparer.OrdinalIgnoreCase)
    {
        // 국제엘렉트릭코리아
        ["국제"] = "국제엘렉트릭코리아",
        ["국제엘레트릭코리아"] = "국제엘렉트릭코리아",
        ["국제엘레트릭 코리아"] = "국제엘렉트릭코리아",
        ["국제엘렉트릭 코리아"] = "국제엘렉트릭코리아",
        // 영신쿼츠
        ["영신"] = "영신쿼츠",
        // 동부하이텍
        ["DB HiTEK"] = "동부하이텍",
        ["DB하이텍"] = "동부하이텍",
        ["DB 하이텍"] = "동부하이텍",
        // 세메스
        ["SEMES"] = "세메스",
        // ACM리서치
        ["ACM리서치코리아"] = "ACM리서치",
        // 진성큐엔에스
        ["진성"] = "진성큐엔에스",
        // TEL (도쿄일렉트론코리아)
        ["도쿄일렉트론코리아(TEL)"] = "TEL (도쿄일렉트론코리아)",
        ["도쿄일렉트론코리아"] = "TEL (도쿄일렉트론코리아)",
        ["TEL"] = "TEL (도쿄일렉트론코리아)",
        ["TEL KOR"] = "TEL (도쿄일렉트론코리아)",
        ["TEL (QTZ)"] = "TEL (도쿄일렉트론코리아)",
    };

    /// <summary>업체명 하나를 대표명으로 변환. 별칭이 아니면 앞뒤 공백만 정리해 그대로 반환.</summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name ?? "";
        var key = name.Trim();
        return Alias.TryGetValue(key, out var canon) ? canon : key;
    }
}
