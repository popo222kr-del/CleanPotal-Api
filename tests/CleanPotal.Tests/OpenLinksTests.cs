using CleanPotal.Api.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>엑셀로 바로 열기 — 짧은 주소는 만든 PC 에서만, 정해진 횟수까지만</summary>
public class OpenLinksTests
{
    private static OpenLinks Links() => new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void 만든_PC에서만_열린다()
    {
        var l = Links();
        var t = l.Create("/x/a.xlsx", "a.xlsx", "홍길동", "10.10.10.20");
        Assert.Equal(48, t.Length);
        Assert.Equal("/x/a.xlsx", l.Use(t, "10.10.10.20")?.Path);
        Assert.Null(l.Use(t, "10.10.10.99"));
    }

    [Fact]
    public void 횟수를_넘기거나_모르는_주소는_안_열린다()
    {
        var l = Links();
        var t = l.Create("/x/a.xlsx", "a.xlsx", "홍길동", null);
        for (var i = 0; i < OpenLinks.MaxUses; i++) Assert.NotNull(l.Use(t, "1.1.1.1"));
        Assert.Null(l.Use(t, "1.1.1.1"));
        Assert.Null(l.Use("abc", null));
        Assert.Null(l.Use(new string('0', 48), null));
    }
}
