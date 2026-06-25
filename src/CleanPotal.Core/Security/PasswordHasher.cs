using System.Security.Cryptography;
using System.Text;

namespace CleanPotal.Core.Security;

/// <summary>
/// 비밀번호 해시 (현재 SHA-256). 운영 전환 시 bcrypt/PBKDF2로 교체 예정 — TODO.
/// 시드/인증 양쪽이 동일 알고리즘을 쓰도록 한 곳에 둔다.
/// </summary>
public static class PasswordHasher
{
    public static string Hash(string password)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();

    public static bool Verify(string password, string hash)
        => Hash(password) == hash;
}
