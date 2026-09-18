using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Harbor.Server.Net;

/// <summary>
/// 웹에서 로그인한 사람에게 주는 **일회용 입장권**.
/// 웹페이지는 비밀번호를 게임 클라이언트에 넘기지 않는다. 대신 짧게 사는 토큰을 주고,
/// 클라이언트는 그것만 들고 접속한다. 한 번 쓰면 사라지고 시간이 지나면 만료된다.
/// </summary>
public sealed class TicketStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Entry> _tickets = new(StringComparer.Ordinal);
    private readonly record struct Entry(string Nick, DateTime ExpiresUtc);

    public string Issue(string nick)
    {
        Sweep();
        var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
        _tickets[ticket] = new Entry(nick, DateTime.UtcNow + Lifetime);
        return ticket;
    }

    /// <summary>쓰면 사라진다. 유효하지 않거나 만료면 null.</summary>
    public string? Redeem(string? ticket)
    {
        if (string.IsNullOrEmpty(ticket) || !_tickets.TryRemove(ticket, out var e)) return null;
        return e.ExpiresUtc > DateTime.UtcNow ? e.Nick : null;
    }

    public int Count => _tickets.Count;

    private void Sweep()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _tickets)
            if (kv.Value.ExpiresUtc <= now) _tickets.TryRemove(kv.Key, out _);
    }
}
