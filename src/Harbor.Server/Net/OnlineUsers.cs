using System.Collections.Concurrent;

namespace Harbor.Server.Net;

/// <summary>
/// 지금 접속 중인 닉 목록. 같은 닉으로 동시에 둘이 들어오면 방·지갑이 서로 덮어써지므로 막는다.
/// 세션이 끊기면 TcpHost 가 반드시 Release 한다.
/// </summary>
public sealed class OnlineUsers
{
    private readonly ConcurrentDictionary<string, long> _byNick = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>선점 성공 시 true. 이미 접속 중이면 false.</summary>
    public bool TryClaim(string nick, long sessionId) => _byNick.TryAdd(nick, sessionId);

    /// <summary>자기가 잡은 것만 놓는다(재로그인으로 다른 세션이 잡은 걸 지우지 않도록).</summary>
    public void Release(string nick, long sessionId)
    {
        if (nick.Length > 0) _byNick.TryRemove(new KeyValuePair<string, long>(nick, sessionId));
    }

    public bool IsOnline(string nick) => nick.Length > 0 && _byNick.ContainsKey(nick);

    public int Count => _byNick.Count;
}
