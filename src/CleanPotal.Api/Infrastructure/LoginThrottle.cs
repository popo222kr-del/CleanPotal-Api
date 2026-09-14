using Microsoft.Extensions.Caching.Memory;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 로그인 실패 횟수 제한(무차별 대입 완화).
/// 키는 <b>아이디 + 접속 IP</b> 조합이다. 아이디만으로 잠그면 남의 아이디를 일부러 틀려
/// 그 사람을 못 들어오게 만드는(잠금 악용) 문제가 생기므로 IP를 함께 본다.
/// 상태는 메모리에만 두므로 서버를 재시작하면 초기화된다(내부망 업무 시스템 기준으로 충분).
/// </summary>
public sealed class LoginThrottle
{
    /// <summary>이 횟수만큼 연속 실패하면 잠금.</summary>
    private const int MaxFailures = 5;
    /// <summary>실패 횟수를 누적해서 세는 기간.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    /// <summary>잠금 유지 시간.</summary>
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(10);

    private sealed class Attempts
    {
        public int Failures;
        public DateTimeOffset? LockedUntil;
    }

    private readonly IMemoryCache _cache;
    public LoginThrottle(IMemoryCache cache) => _cache = cache;

    private static string Key(string? username, string? ip)
        => $"login-fail:{(username ?? "").Trim().ToLowerInvariant()}|{ip ?? "-"}";

    /// <summary>잠겨 있으면 남은 시간, 아니면 null.</summary>
    public TimeSpan? RetryAfter(string? username, string? ip)
    {
        if (_cache.TryGetValue(Key(username, ip), out Attempts? a) && a?.LockedUntil is { } until)
        {
            var remain = until - DateTimeOffset.UtcNow;
            if (remain > TimeSpan.Zero) return remain;
        }
        return null;
    }

    /// <summary>로그인 실패 기록. 한도를 넘으면 잠금으로 전환한다.</summary>
    public void RecordFailure(string? username, string? ip)
    {
        var key = Key(username, ip);
        var a = _cache.TryGetValue(key, out Attempts? existing) && existing is not null ? existing : new Attempts();
        a.Failures++;
        if (a.Failures >= MaxFailures)
        {
            a.LockedUntil = DateTimeOffset.UtcNow.Add(LockDuration);
            _cache.Set(key, a, LockDuration);
        }
        else
        {
            _cache.Set(key, a, Window);
        }
    }

    /// <summary>로그인 성공 — 누적 실패 기록을 지운다.</summary>
    public void RecordSuccess(string? username, string? ip)
        => _cache.Remove(Key(username, ip));
}
