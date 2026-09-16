namespace CleanPotal.Core;

/// <summary>
/// 직급(호칭) 목록과 서열. 직위(JobTitle = QA팀장·세정팀장 등 맡은 일)와는 다른 개념이다.
///
/// 화면의 선택 목록도 같은 순서를 쓴다(client/src/pages/Users.tsx 의 RANKS).
/// 목록에 없는 값이 들어와도 저장은 막지 않는다 — 직급 체계가 바뀌었을 때
/// 배포 없이 기존 값을 그대로 보여줄 수 있어야 한다. 서열만 '알 수 없음'이 된다.
/// </summary>
public static class JobRank
{
    /// <summary>아래(사원)에서 위(사장) 순서.</summary>
    public static readonly string[] All =
    {
        "사원", "주임", "대리", "과장", "차장", "부장", "상무", "전무", "부사장", "사장",
    };

    /// <summary>서열. 위일수록 작은 값(정렬용). 비었거나 목록에 없으면 <see cref="int.MaxValue"/>.</summary>
    public static int Order(string? rank)
    {
        var r = (rank ?? "").Trim();
        if (r.Length == 0) return int.MaxValue;
        var i = Array.IndexOf(All, r);
        return i < 0 ? int.MaxValue : All.Length - i;   // 사장=1 … 사원=10
    }
}
