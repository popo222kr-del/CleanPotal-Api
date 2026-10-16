using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 엑셀로 바로 열기용 짧은 주소 — 웹은 로그인(Authorization 헤더)이 필요한데 엑셀 프로그램은 그 헤더를 못 싣는다.
/// 로그인한 사람이 누른 순간 파일 하나만 가리키는 추측할 수 없는 주소를 만들고, 몇 분 뒤 또는 정해진 횟수를 쓰면 사라진다.
/// (엑셀은 여는 동안 같은 주소를 여러 번 부르므로 '딱 한 번'이 아니라 '짧게·몇 번'.) 주소를 만든 PC(IP)에서만 열린다 — 주소가 새어도 다른 PC 에선 안 된다.
/// 서버 메모리에만 둔다 — 서버를 다시 켜면 모두 무효.
/// </summary>
public sealed class OpenLinks
{
    public static readonly TimeSpan Life = TimeSpan.FromMinutes(3);
    public const int MaxUses = 30;

    private readonly IMemoryCache _cache;
    public OpenLinks(IMemoryCache cache) => _cache = cache;

    private sealed class Entry
    {
        public required string Path { get; init; }
        public required string Name { get; init; }
        public required string By { get; init; }
        public string? Ip { get; init; }
        public int Uses;
    }

    /// <summary>파일 하나를 가리키는 주소 토큰</summary>
    public string Create(string path, string name, string by, string? ip)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        _cache.Set("open:" + token, new Entry { Path = path, Name = name, By = by, Ip = ip }, Life);
        return token;
    }

    /// <summary>토큰 → (경로, 이름). 없거나 끝났으면 null</summary>
    public (string Path, string Name, string By)? Use(string token, string? ip)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 48 || !token.All(Uri.IsHexDigit)) return null;
        if (!_cache.TryGetValue("open:" + token, out Entry? e) || e is null) return null;
        if (e.Ip is not null && ip is not null && e.Ip != ip) return null;
        if (Interlocked.Increment(ref e.Uses) > MaxUses) { _cache.Remove("open:" + token); return null; }
        return (e.Path, e.Name, e.By);
    }
}
