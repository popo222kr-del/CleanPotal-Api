using CleanPotal.Api.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace CleanPotal.Tests;

public class LoginThrottleTests
{
    private static LoginThrottle New() => new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void 실패가_한도_미만이면_잠기지_않는다()
    {
        var t = New();
        for (int i = 0; i < 4; i++) t.RecordFailure("1004", "10.0.0.1");
        Assert.Null(t.RetryAfter("1004", "10.0.0.1"));
    }

    [Fact]
    public void 연속_5회_실패하면_잠긴다()
    {
        var t = New();
        for (int i = 0; i < 5; i++) t.RecordFailure("1004", "10.0.0.1");
        var remain = t.RetryAfter("1004", "10.0.0.1");
        Assert.NotNull(remain);
        Assert.True(remain!.Value > TimeSpan.Zero);
    }

    [Fact]
    public void 로그인_성공하면_실패_기록이_초기화된다()
    {
        var t = New();
        for (int i = 0; i < 4; i++) t.RecordFailure("1004", "10.0.0.1");
        t.RecordSuccess("1004", "10.0.0.1");
        for (int i = 0; i < 4; i++) t.RecordFailure("1004", "10.0.0.1");
        Assert.Null(t.RetryAfter("1004", "10.0.0.1"));   // 초기화됐으므로 누적 8회여도 잠기지 않는다
    }

    [Fact]
    public void 다른_IP_는_잠금에_영향받지_않는다()
    {
        // 아이디만으로 잠그면 남의 계정을 고의로 잠글 수 있다 — IP 를 함께 보는지 확인.
        var t = New();
        for (int i = 0; i < 5; i++) t.RecordFailure("1004", "10.0.0.1");
        Assert.NotNull(t.RetryAfter("1004", "10.0.0.1"));
        Assert.Null(t.RetryAfter("1004", "10.0.0.2"));
    }

    [Fact]
    public void 아이디_대소문자와_공백은_같은_키로_센다()
    {
        var t = New();
        for (int i = 0; i < 5; i++) t.RecordFailure(" Admin ", "10.0.0.1");
        Assert.NotNull(t.RetryAfter("admin", "10.0.0.1"));
    }
}
