using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 브라우저에서 로컬 실행 도우미로 넘기는 짧은 수명의 1회용 실행권을 보관한다.
/// 토큰에는 경로나 사용자 정보가 들어가지 않으며, 성공/실패와 관계없이 한 번 꺼내면 폐기한다.
/// </summary>
public sealed class PortalLaunchTicketStore
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<string, TicketEntry> _tickets = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public PortalLaunchTicketStore(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public PortalLaunchTicket Issue(int itemId, TimeSpan? lifetime = null)
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var pair in _tickets)
        {
            if (pair.Value.ExpiresAt <= now)
                _tickets.TryRemove(pair.Key, out _);
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiresAt = now.Add(lifetime ?? DefaultLifetime);
        _tickets[token] = new TicketEntry(itemId, expiresAt);
        return new PortalLaunchTicket(token, expiresAt);
    }

    public bool TryRedeem(string token, out int itemId)
    {
        itemId = 0;
        if (token.Length != 64 || token.Any(value => !Uri.IsHexDigit(value)))
            return false;

        if (!_tickets.TryRemove(token, out var ticket) || ticket.ExpiresAt <= _timeProvider.GetUtcNow())
            return false;

        itemId = ticket.ItemId;
        return true;
    }

    private sealed record TicketEntry(int ItemId, DateTimeOffset ExpiresAt);
}

public sealed record PortalLaunchTicket(string Token, DateTimeOffset ExpiresAt);
