using CleanPotal.Api.Infrastructure;
using Xunit;

namespace CleanPotal.Tests;

public class PortalLaunchTicketStoreTests
{
    [Fact]
    public void 실행권은_한_번만_사용할_수_있다()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-09-17T00:00:00Z"));
        var store = new PortalLaunchTicketStore(clock);
        var ticket = store.Issue(27);

        Assert.True(store.TryRedeem(ticket.Token, out var itemId));
        Assert.Equal(27, itemId);
        Assert.False(store.TryRedeem(ticket.Token, out _));
    }

    [Fact]
    public void 만료된_실행권은_거부한다()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-09-17T00:00:00Z"));
        var store = new PortalLaunchTicketStore(clock);
        var ticket = store.Issue(27, TimeSpan.FromSeconds(1));
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.False(store.TryRedeem(ticket.Token, out _));
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
