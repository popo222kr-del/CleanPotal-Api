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

    /// <summary>
    /// WPF 등에서 넘어온 비밀번호 원본 값을 임포트할 때 쓴다.
    /// 이미 구버전 SHA-256 해시(64자 hex) 형태면 그대로 보존해야 Verify()의 레거시
    /// 호환 경로가 실제 비밀번호로 로그인시킬 수 있다 — 여기서 다시 BCrypt로 해시해
    /// 버리면 "해시의 해시"가 되어 어떤 비밀번호를 넣어도 영원히 로그인이 안 된다.
    /// 그 외(평문이거나 비어있음)는 새로 BCrypt 해시한다.
    /// </summary>
    public static string ImportHash(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return Hash("1234");
        if (raw.Length == 64 && IsHex(raw)) return raw;   // 이미 레거시 해시 → 보존
        return Hash(raw);                                  // 평문으로 간주 → BCrypt
    }

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
