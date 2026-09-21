using System.Collections.Concurrent;

namespace Harbor.Server.Net;

/// <summary>
/// 지금 접속 중인 사람들. 두 가지 일을 한다:
///  1. **같은 닉 동시 접속 차단** — 둘이 들어오면 방·지갑이 서로 덮어써진다.
///  2. **살아 있는 세션 찾기** — 관리 API 의 킥·지급이 세션을 거쳐야 하기 때문이다(`/admin`).
///     DB 를 직접 고치면 `SaveStore` 가 메모리를 내려쓸 때 덮어써진다 → docs/platform-plan.md §6
///
/// 세션이 끊기면 `SessionRunner` 가 반드시 Release 한다.
/// </summary>
public sealed class OnlineUsers
{
    private readonly ConcurrentDictionary<string, Session> _byNick = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>선점 성공 시 true. 이미 접속 중이면 false.</summary>
    public bool TryClaim(string nick, Session session) => _byNick.TryAdd(nick, session);

    /// <summary>자기가 잡은 것만 놓는다(재로그인으로 다른 세션이 잡은 걸 지우지 않도록).</summary>
    public void Release(string nick, long sessionId)
    {
        if (nick.Length == 0) return;
        if (_byNick.TryGetValue(nick, out var s) && s.Id == sessionId)
            _byNick.TryRemove(new KeyValuePair<string, Session>(nick, s));
    }

    public bool IsOnline(string nick) => nick.Length > 0 && _byNick.ContainsKey(nick);

    /// <summary>접속 중이면 그 세션. 관리 명령이 사람을 찾을 때 쓴다.</summary>
    public Session? Find(string nick) => nick.Length > 0 ? _byNick.GetValueOrDefault(nick) : null;

    public IEnumerable<Session> Sessions => _byNick.Values;

    public int Count => _byNick.Count;
}
