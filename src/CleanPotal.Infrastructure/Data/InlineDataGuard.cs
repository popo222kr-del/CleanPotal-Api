using System.Text.RegularExpressions;
using CleanPotal.Core;

namespace CleanPotal.Infrastructure.Data;

/// <summary>
/// 첨부 칸에 사진을 통째로(base64 data: 주소) 넣는 것을 막는다.
///
/// 지금 화면은 사진을 첨부 보관소에 파일로 올리고 칸에는 "att:번호|이름|종류" 만 담는다.
/// 옛 화면이 열린 채 남은 탭이나 API 를 직접 부르면 여전히 수 MB 짜리 base64 가 DB 칸에 들어가
/// (SQL Server Express 10GB) 목록 응답까지 무거워졌다.
///
/// 이미 저장돼 있던 base64(아직 migrate-attachments 전의 옛 기록)는 그대로 둔다 —
/// 사진을 건드리지 않고 다른 칸만 고쳐 저장해도 막히지 않게, "새로 들어온" 것만 거절한다.
/// </summary>
public static partial class InlineDataGuard
{
    [GeneratedRegex(@"data:[\w.+-]+/[\w.+-]+;base64,[A-Za-z0-9+/=]{16,}")]
    private static partial Regex DataUri();

    public static void EnsureNoNewInline(string? newValue, string? oldValue, string field)
    {
        if (string.IsNullOrEmpty(newValue) || !newValue.Contains(";base64,", StringComparison.Ordinal)) return;
        var old = oldValue ?? "";
        foreach (Match m in DataUri().Matches(newValue))
        {
            if (old.Contains(m.Value, StringComparison.Ordinal)) continue;
            throw new BusinessRuleException(
                $"{field}: 사진을 첨부 파일로 올려 주세요. 화면이 옛 버전이면 새로고침(F5) 후 다시 저장하세요.");
        }
    }
}
