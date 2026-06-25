namespace CleanPotal.Core.Security;

/// <summary>
/// 비밀번호 해시 (BCrypt). 기존 SHA-256 해시(64자 hex)도 검증은 호환 지원하여
/// 임포트/구버전 계정이 로그인 시 자동 동작하게 한다.
/// </summary>
public static class PasswordHasher
{
    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    public static bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        // 구버전 SHA-256(64자 hex) 호환
        if (hash.Length == 64 && IsHex(hash))
            return LegacySha256(password) == hash;
        try { return BCrypt.Net.BCrypt.Verify(password, hash); }
        catch { return false; }
    }

    /// <summary>로그인 성공 시 구형 해시면 BCrypt로 재해시할지 판단.</summary>
    public static bool NeedsRehash(string hash) => hash.Length == 64 && IsHex(hash);

    private static bool IsHex(string s)
    {
        foreach (var c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    private static string LegacySha256(string password)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
