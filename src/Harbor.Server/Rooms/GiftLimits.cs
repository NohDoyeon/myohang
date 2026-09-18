using System.Collections.Concurrent;

namespace Harbor.Server.Rooms;

/// <summary>
/// "같은 사람에게 하루 몇 개까지" 를 세는 곳.
///
/// 일부러 **메모리에만** 둔다. 서버를 재시작하면 한도가 풀리지만, 이 한도의 목적은
/// 자전거래를 *채산이 안 맞게* 만드는 것이지 완벽히 봉쇄하는 것이 아니다
/// (경제적 방어는 따로 있다 — 잎을 혼자 팔면 40, 남에게 주면 상대가 50을 받는다).
/// 매 요청마다 DB 를 치지 않기 위한 선택이기도 하다. 영구 기록은 `gift_log` 가 남긴다.
/// </summary>
public static class GiftLimits
{
    private static readonly ConcurrentDictionary<(string giver, string owner, DateOnly day), int> Counts = new();
    private static DateOnly _today = DateOnly.FromDateTime(DateTime.Now);

    /// <summary>한도가 남아 있으면 1 올리고 true. 한도를 넘었으면 아무것도 하지 않고 false.</summary>
    public static bool TryUse(string giver, string owner, int limit)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today != _today) { Counts.Clear(); _today = today; }    // 날이 바뀌면 통째로 비운다

        var key = (giver.ToLowerInvariant(), owner.ToLowerInvariant(), today);
        while (true)
        {
            int used = Counts.GetValueOrDefault(key);
            if (used >= limit) return false;
            if (used == 0 ? Counts.TryAdd(key, 1) : Counts.TryUpdate(key, used + 1, used)) return true;
        }
    }

    /// <summary>오늘 이 상대에게 몇 개 줬는지 (안내 문구용).</summary>
    public static int Used(string giver, string owner)
        => Counts.GetValueOrDefault((giver.ToLowerInvariant(), owner.ToLowerInvariant(), DateOnly.FromDateTime(DateTime.Now)));
}
