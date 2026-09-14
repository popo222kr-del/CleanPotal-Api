using System.Security.Cryptography;
using System.Text;
using CleanPotal.Core.Security;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 비밀번호 해시 — WPF 구형 해시 호환이 깨지면 임포트된 계정이 전부 로그인 불가가 되므로
/// 가장 먼저 회귀를 잡아야 하는 지점이다.
/// </summary>
public class PasswordHasherTests
{
    private static string Sha256Hex(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    private static string Sha1Hex(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)));
    private static string Md5Hex(string s) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s)));

    [Fact]
    public void BCrypt_해시는_같은_비밀번호로_검증된다()
    {
        var hash = PasswordHasher.Hash("pw1234");
        Assert.True(PasswordHasher.Verify("pw1234", hash));
        Assert.False(PasswordHasher.Verify("pw12345", hash));
        Assert.False(PasswordHasher.NeedsRehash(hash));
    }

    [Theory]
    [InlineData("SHA256")]
    [InlineData("SHA1")]
    [InlineData("MD5")]
    public void WPF_구형_해시도_대소문자_상관없이_검증된다(string algo)
    {
        const string pw = "1234";
        var hex = algo switch
        {
            "SHA256" => Sha256Hex(pw),
            "SHA1" => Sha1Hex(pw),
            _ => Md5Hex(pw),
        };

        Assert.True(PasswordHasher.Verify(pw, hex));                   // 대문자 hex
        Assert.True(PasswordHasher.Verify(pw, hex.ToLowerInvariant())); // 소문자 hex
        Assert.False(PasswordHasher.Verify("wrong", hex));
        Assert.True(PasswordHasher.NeedsRehash(hex));                   // 로그인 후 BCrypt 로 재해시 대상
    }

    [Fact]
    public void 빈_해시는_어떤_비밀번호로도_통과하지_않는다()
    {
        Assert.False(PasswordHasher.Verify("", ""));
        Assert.False(PasswordHasher.Verify("1234", ""));
        Assert.False(PasswordHasher.NeedsRehash(""));
    }

    [Fact]
    public void ImportHash_는_구형_해시를_다시_해시하지_않는다()
    {
        // 이중 해시되면 원래 비밀번호로 로그인할 수 없게 된다(실제로 났던 장애).
        var legacy = Sha256Hex("1234");
        Assert.Equal(legacy, PasswordHasher.ImportHash(legacy));
        Assert.True(PasswordHasher.Verify("1234", PasswordHasher.ImportHash(legacy)));
    }

    [Fact]
    public void ImportHash_는_평문을_BCrypt_로_바꾼다()
    {
        var hash = PasswordHasher.ImportHash("plain-pw");
        Assert.StartsWith("$2", hash);
        Assert.True(PasswordHasher.Verify("plain-pw", hash));
    }

    [Fact]
    public void Fingerprint_는_해시가_바뀌면_달라지고_같으면_유지된다()
    {
        var a = PasswordHasher.Hash("old");
        var b = PasswordHasher.Hash("new");

        Assert.Equal(PasswordHasher.Fingerprint(a), PasswordHasher.Fingerprint(a));
        Assert.NotEqual(PasswordHasher.Fingerprint(a), PasswordHasher.Fingerprint(b));
        // 해시 원문이 지문에 그대로 드러나면 안 된다.
        Assert.DoesNotContain(PasswordHasher.Fingerprint(a), a);
        Assert.Equal(8, PasswordHasher.Fingerprint(a).Length);
    }

    [Fact]
    public void Describe_는_비밀번호_값을_노출하지_않는다()
    {
        var hash = PasswordHasher.Hash("secret-value");
        var text = PasswordHasher.Describe(hash);
        Assert.DoesNotContain("secret-value", text);
        Assert.DoesNotContain(hash, text);
        Assert.Contains("BCrypt", text);
    }
}
