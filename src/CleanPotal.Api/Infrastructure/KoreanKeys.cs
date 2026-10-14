using System.Text;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 한글 입력기가 켜진 채로 바코드 스캐너를 찍으면 영문 키가 한글로 바뀌어 들어온다("1SN261014004P" → "1누261014004ㅔ").
/// 두벌식 자판 기준으로 눌린 영문 키로 되돌린다. 대소문자는 알 수 없어 대문자로(LOT·S/N·반출번호는 대문자).
/// 한글이 없으면 그대로 돌려준다(공백만 다듬음).
/// </summary>
public static class KoreanKeys
{
    private static readonly string[] Cho = ["r", "R", "s", "e", "E", "f", "a", "q", "Q", "t", "T", "d", "w", "W", "c", "z", "x", "v", "g"];
    private static readonly string[] Jung = ["k", "o", "i", "O", "j", "p", "u", "P", "h", "hk", "ho", "hl", "y", "n", "nj", "np", "nl", "b", "m", "ml", "l"];
    private static readonly string[] Jong = ["", "r", "R", "rt", "s", "sw", "sg", "e", "f", "fr", "fa", "fq", "ft", "fx", "fv", "fg", "a", "q", "qt", "t", "T", "d", "w", "c", "z", "x", "v", "g"];

    /// <summary>호환 자모(ㄱ U+3131 ~ ㅣ U+3163) → 키</summary>
    private static readonly Dictionary<char, string> Jamo = new()
    {
        ['ㄱ'] = "r", ['ㄲ'] = "R", ['ㄳ'] = "rt", ['ㄴ'] = "s", ['ㄵ'] = "sw", ['ㄶ'] = "sg", ['ㄷ'] = "e", ['ㄸ'] = "E", ['ㄹ'] = "f",
        ['ㄺ'] = "fr", ['ㄻ'] = "fa", ['ㄼ'] = "fq", ['ㄽ'] = "ft", ['ㄾ'] = "fx", ['ㄿ'] = "fv", ['ㅀ'] = "fg", ['ㅁ'] = "a", ['ㅂ'] = "q",
        ['ㅃ'] = "Q", ['ㅄ'] = "qt", ['ㅅ'] = "t", ['ㅆ'] = "T", ['ㅇ'] = "d", ['ㅈ'] = "w", ['ㅉ'] = "W", ['ㅊ'] = "c", ['ㅋ'] = "z",
        ['ㅌ'] = "x", ['ㅍ'] = "v", ['ㅎ'] = "g", ['ㅏ'] = "k", ['ㅐ'] = "o", ['ㅑ'] = "i", ['ㅒ'] = "O", ['ㅓ'] = "j", ['ㅔ'] = "p",
        ['ㅕ'] = "u", ['ㅖ'] = "P", ['ㅗ'] = "h", ['ㅘ'] = "hk", ['ㅙ'] = "ho", ['ㅚ'] = "hl", ['ㅛ'] = "y", ['ㅜ'] = "n", ['ㅝ'] = "nj",
        ['ㅞ'] = "np", ['ㅟ'] = "nl", ['ㅠ'] = "b", ['ㅡ'] = "m", ['ㅢ'] = "ml", ['ㅣ'] = "l",
    };

    public static bool HasHangul(string s) => s.Any(c => c is >= '가' and <= '힣' || Jamo.ContainsKey(c));

    public static string ToKeys(string? input)
    {
        var s = (input ?? "").Trim();
        if (!HasHangul(s)) return s;
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (c is >= '가' and <= '힣')
            {
                var i = c - 0xAC00;
                sb.Append(Cho[i / (21 * 28)]).Append(Jung[i % (21 * 28) / 28]).Append(Jong[i % 28]);
            }
            else if (Jamo.TryGetValue(c, out var k)) sb.Append(k);
            else sb.Append(c);
        }
        return sb.ToString().ToUpperInvariant();
    }
}
