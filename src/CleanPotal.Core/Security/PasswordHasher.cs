namespace CleanPotal.Core.Security;

/// <summary>
/// 비밀번호 해시 (BCrypt). WPF에서 넘어온 구형 해시(SHA-256/SHA-1/MD5, 대·소문자 hex)도
/// 검증을 호환 지원하여 임포트된 기존 계정이 원래 비밀번호로 로그인되게 한다.
/// 구형 해시로 로그인에 성공하면 AuthService가 즉시 BCrypt로 재해시해 저장한다.
/// </summary>
public static class PasswordHasher
{
    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    public static bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        // 구버전(WPF) 해시 호환 — 대/소문자 hex 모두 허용
        if (IsLegacyHex(hash))
            return LegacyScheme(password, hash) is not null;
        try { return BCrypt.Net.BCrypt.Verify(password, hash); }
        catch { return false; }
    }

    /// <summary>로그인 성공 시 구형 해시면 BCrypt로 재해시할지 판단.</summary>
    public static bool NeedsRehash(string hash) => !string.IsNullOrEmpty(hash) && IsLegacyHex(hash);

    /// <summary>
    /// 구형 해시 후보 — WPF 구현에 따라 길이/알고리즘/대소문자가 제각각이라 모두 허용한다.
    /// (레거시 계정이 로그인에 성공하면 AuthService 가 곧바로 BCrypt 로 재해시해 저장하므로
    ///  약한 해시가 DB에 남지 않는다.)
    /// </summary>
    private static bool IsLegacyHex(string h) => (h.Length is 64 or 40 or 32) && IsHex(h);

    /// <summary>일치하는 구형 방식 이름을 돌려준다(진단용). 일치하지 않으면 null.</summary>
    public static string? LegacyScheme(string password, string hash)
    {
        if (string.IsNullOrEmpty(hash) || !IsLegacyHex(hash)) return null;
        bool Eq(string a) => string.Equals(a, hash, StringComparison.OrdinalIgnoreCase);
        return hash.Length switch
        {
            64 when Eq(Hex(System.Security.Cryptography.SHA256.HashData(Utf8(password)))) => "SHA-256",
            40 when Eq(Hex(System.Security.Cryptography.SHA1.HashData(Utf8(password)))) => "SHA-1",
            32 when Eq(Hex(System.Security.Cryptography.MD5.HashData(Utf8(password)))) => "MD5",
            _ => null,
        };
    }

    /// <summary>저장된 해시의 형식만 사람이 읽을 수 있게 분류한다(값은 노출하지 않음).</summary>
    public static string Describe(string? hash)
    {
        if (string.IsNullOrEmpty(hash)) return "비어 있음";
        if (hash.StartsWith("$2")) return $"BCrypt (길이 {hash.Length})";
        if (IsLegacyHex(hash))
        {
            var algo = hash.Length switch { 64 => "SHA-256 추정", 40 => "SHA-1 추정", _ => "MD5 추정" };
            var caseKind = hash.Any(char.IsUpper) ? "대문자" : "소문자";
            return $"{algo} hex {caseKind} (길이 {hash.Length})";
        }
        return $"알 수 없는 형식 (길이 {hash.Length})";
    }

    private static byte[] Utf8(string s) => System.Text.Encoding.UTF8.GetBytes(s);
    private static string Hex(byte[] b) => Convert.ToHexString(b);

    /// <summary>
    /// WPF 등에서 넘어온 비밀번호 원본 값을 임포트할 때 쓴다.
    /// 이미 구버전 해시(SHA-256/SHA-1/MD5 hex) 형태면 그대로 보존해야 Verify()의 레거시
    /// 호환 경로가 실제 비밀번호로 로그인시킬 수 있다 — 여기서 다시 BCrypt로 해시해
    /// 버리면 "해시의 해시"가 되어 어떤 비밀번호를 넣어도 영원히 로그인이 안 된다.
    /// 그 외(평문)는 새로 BCrypt 해시한다.
    /// </summary>
    public static string ImportHash(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return Hash("1234");
        if (IsLegacyHex(raw)) return raw;   // 이미 구형 해시 → 그대로 보존(재해시 금지)
        return Hash(raw);                    // 평문으로 간주 → BCrypt
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

}
