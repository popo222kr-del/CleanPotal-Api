using System.Globalization;
using System.Text;

namespace CleanPotal.Core;

/// <summary>
/// 업체 관리의 업체를 MES 업체로 한 번에 올릴 때 쓰는 코드 만들기.
///
/// MES 업체는 <b>업체 코드</b>와 <b>반출번호 약어</b>가 반드시 있어야 하고 둘 다 겹치면 안 된다.
/// 62개를 손으로 채우는 것은 무리라 여기서 초안을 만들어 주고, 화면에서 한 줄씩 고칠 수 있게 한다
/// (반출번호는 서류에 찍히는 값이라 사람이 마지막에 손보는 편이 낫다).
///
/// - 업체 코드: 가나다 → ABC 순으로 001, 002, 003 …
/// - 반출번호 약어: 한글 초성을 로마자로 (금강쿼츠 → KKKC). 회사에서 쓰던 약어와 다르면 그 줄만 고친다.
/// </summary>
public static class VendorMesCodes
{
    private const int HangulBase = 0xAC00;
    private const int HangulLast = 0xD7A3;

    /// <summary>초성 19자의 로마자. 된소리는 예사소리와 같은 글자로 줄인다(약어라 짧을수록 낫다).</summary>
    private static readonly string[] Choseong =
        ["K", "K", "N", "D", "D", "R", "M", "B", "B", "S", "S", "", "J", "J", "C", "K", "T", "P", "H"];

    /// <summary>ㅇ 으로 시작하면 소리가 없어 중성으로 대신한다(이엔지 → IEJ).</summary>
    private static readonly string[] Jungseong =
        ["A", "A", "Y", "Y", "E", "E", "Y", "Y", "O", "W", "W", "O", "Y", "U", "W", "W", "W", "Y", "E", "E", "I"];

    /// <summary>제안할 약어 길이. 이보다 긴 이름은 앞부분만 쓴다(MES 의 약어 한도는 10자다).</summary>
    private const int SuggestLength = 4;

    /// <summary>업체명에서 반출번호 약어 초안을 만든다. 비면 "V".</summary>
    public static string SuggestPrefix(string? vendorName)
    {
        var name = (vendorName ?? "").Trim();
        var sb = new StringBuilder();
        var wordStart = true;

        foreach (var ch in name)
        {
            if (sb.Length >= SuggestLength) break;

            if (ch >= HangulBase && ch <= HangulLast)
            {
                var index = ch - HangulBase;
                var cho = index / (21 * 28);
                var letter = Choseong[cho];
                sb.Append(letter.Length > 0 ? letter : Jungseong[index / 28 % 21]);
                wordStart = false;
                continue;
            }

            if (char.IsLetter(ch))
            {
                // 영문은 낱말 첫 글자만 — "ABC Corp" → "AC"
                if (wordStart) sb.Append(char.ToUpperInvariant(ch));
                wordStart = false;
                continue;
            }

            // 숫자·기호·공백은 낱말 경계로만 본다
            wordStart = true;
        }

        var prefix = sb.ToString();
        return prefix.Length == 0 ? "V" : prefix;
    }

    /// <summary>이미 쓰는 약어와 겹치면 뒤에 숫자를 붙여 비켜 간다(SS → SS2 → SS3 …).</summary>
    public static string UniquePrefix(string? vendorName, ISet<string> taken)
    {
        // SuggestLength(4) + 숫자 세 자리라도 MES 의 10자 한도 안에 들어온다.
        var baseText = SuggestPrefix(vendorName);
        if (taken.Add(baseText)) return baseText;

        for (var n = 2; n < 1000; n++)
        {
            var candidate = baseText + n.ToString(CultureInfo.InvariantCulture);
            if (taken.Add(candidate)) return candidate;
        }

        // 여기까지 올 일은 없다 — 와도 저장 단계에서 중복으로 막힌다.
        return baseText;
    }

    /// <summary>001, 002 … 세 자리를 넘어가면 자릿수를 늘린다.</summary>
    public static string SequenceCode(int number)
        => number.ToString("000", CultureInfo.InvariantCulture);

    /// <summary>이미 쓰는 업체 코드를 건너뛰며 다음 번호를 찾는다.</summary>
    public static string NextCode(ref int number, ISet<string> taken)
    {
        while (true)
        {
            var code = SequenceCode(++number);
            if (taken.Add(code)) return code;
        }
    }

    /// <summary>가나다 먼저, 그다음 ABC. 번호를 매기는 순서다.</summary>
    public static IComparer<string> NameOrder { get; } = Comparer<string>.Create((a, b) =>
    {
        var ga = Group(a);
        var gb = Group(b);
        return ga != gb ? ga - gb : string.Compare(a, b, StringComparison.InvariantCultureIgnoreCase);
    });

    private static int Group(string? name)
    {
        var text = (name ?? "").TrimStart();
        if (text.Length == 0) return 2;
        return text[0] >= HangulBase && text[0] <= HangulLast ? 0 : 1;
    }
}
