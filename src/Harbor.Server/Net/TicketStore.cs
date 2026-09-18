using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Harbor.Server.Net;

/// <summary>
/// 웹에서 로그인한 사람에게 주는 **일회용 입장권**.
/// 웹페이지는 비밀번호를 게임 클라이언트에 넘기지 않는다. 대신 짧게 사는 토큰을 주고,
/// 클라이언트는 그것만 들고 접속한다.
///
/// 두 종류를 받는다:
///  · **메모리 입장권** — 이 프로세스가 직접 발급(웹과 게임이 한 프로세스일 때). 한 번 쓰면 사라진다.
///  · **서명 입장권**(`t1.…`) — **다른 곳**(Vercel 등)이 공유 비밀키로 서명해 발급한 것.
///    게임 서버는 서명과 만료만 검증하므로 발급자와 통신할 필요가 없다 — 웹을 떼어낼 수 있는 이유가 이것이다.
///    대신 만료 전까지는 재사용이 가능하다(같은 닉 동시 접속은 `OnlineUsers` 가 막는다).
/// </summary>
public sealed class TicketStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>서명 입장권의 형식 표시. 버전을 붙여 두면 나중에 형식을 바꿔도 옛 것을 구분할 수 있다.</summary>
    private const string SignedPrefix = "t1.";

    private readonly ConcurrentDictionary<string, Entry> _tickets = new(StringComparer.Ordinal);
    private readonly record struct Entry(string Nick, DateTime ExpiresUtc);

    /// <summary>서명 검증용 공유 비밀키. 없으면 서명 입장권을 받지 않는다(로컬 개발).</summary>
    private readonly byte[]? _secret;

    public TicketStore()
    {
        var s = Environment.GetEnvironmentVariable("HARBOR_TICKET_SECRET");
        _secret = string.IsNullOrWhiteSpace(s) ? null : Encoding.UTF8.GetBytes(s);
    }

    public bool AcceptsSigned => _secret is not null;

    public string Issue(string nick)
    {
        Sweep();
        var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
        _tickets[ticket] = new Entry(nick, DateTime.UtcNow + Lifetime);
        return ticket;
    }

    /// <summary>유효하면 닉, 아니면 null. 메모리 입장권은 쓰면 사라진다.</summary>
    public string? Redeem(string? ticket)
    {
        if (string.IsNullOrEmpty(ticket)) return null;
        if (ticket.StartsWith(SignedPrefix, StringComparison.Ordinal)) return VerifySigned(ticket);
        if (!_tickets.TryRemove(ticket, out var e)) return null;
        return e.ExpiresUtc > DateTime.UtcNow ? e.Nick : null;
    }

    /// <summary>
    /// `t1.&lt;payload&gt;.&lt;서명&gt;` — payload 는 `닉|만료(Unix초)` 를 base64url 로 담은 것.
    /// 서명이 맞고 만료 전이면 그 닉을 믿는다. **비밀키가 없으면 무조건 거부한다** — 조용히 통과시키면 안 된다.
    /// </summary>
    private string? VerifySigned(string ticket)
    {
        if (_secret is null) return null;
        var parts = ticket.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            var payloadBytes = FromBase64Url(parts[1]);
            var given = FromBase64Url(parts[2]);
            var expected = HMACSHA256.HashData(_secret, payloadBytes);
            if (!CryptographicOperations.FixedTimeEquals(given, expected)) return null;

            var fields = Encoding.UTF8.GetString(payloadBytes).Split('|');
            if (fields.Length != 2 || !long.TryParse(fields[1], out long exp)) return null;
            if (DateTimeOffset.FromUnixTimeSeconds(exp) <= DateTimeOffset.UtcNow) return null;
            return fields[0].Length > 0 ? fields[0] : null;
        }
        catch (FormatException) { return null; }
    }

    private static byte[] FromBase64Url(string s)
        => Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/').PadRight((s.Length + 3) / 4 * 4, '='));

    public int Count => _tickets.Count;

    private void Sweep()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _tickets)
            if (kv.Value.ExpiresUtc <= now) _tickets.TryRemove(kv.Key, out _);
    }
}
