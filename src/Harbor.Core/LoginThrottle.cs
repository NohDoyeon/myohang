namespace Harbor.Core;

/// <summary>
/// 로그인 실패가 몰리는 열쇠(닉·IP)를 잠근다. **무차별 대입을 막는 것이 전부다.**
///
/// 지금까지 게임 서버와 웹 어느 쪽에도 시도 횟수 제한이 없었다 — 공개하면 비밀번호 4자짜리 계정은
/// 몇 분이면 뚫린다.
///
/// **시계를 인자로 받는다.** 그래야 "10분 뒤에 풀린다"를 실제로 10분 기다리지 않고 시험할 수 있다.
/// 시간에 기대는 코드는 시계를 감추는 순간 테스트가 불가능해진다.
///
/// 성공하면 그 열쇠의 기록을 **지운다** — 한 번 틀렸다가 맞힌 사람이 남은 횟수 때문에 막히면 안 된다.
/// </summary>
public sealed class LoginThrottle
{
    private readonly int _maxFails;
    private readonly TimeSpan _window;
    private readonly Dictionary<string, List<DateTime>> _fails = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <summary>
    /// 기억할 열쇠 수 상한. 닉을 매번 바꿔 가며 메모리를 불리는 것도 공격이므로 상한이 필요하다.
    /// 넘으면 창이 지난 기록부터 버린다.
    /// </summary>
    private const int MaxKeys = 10_000;

    /// <param name="maxFails">창 안에서 이만큼 실패하면 잠근다.</param>
    /// <param name="window">세는 구간. 마지막 실패로부터 이 시간이 지나면 저절로 풀린다.</param>
    public LoginThrottle(int maxFails = 8, TimeSpan? window = null)
    {
        _maxFails = Math.Max(1, maxFails);
        _window = window ?? TimeSpan.FromMinutes(10);
    }

    public bool IsBlocked(string key, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(key)) return false;
        lock (_gate) return Recent(key, nowUtc) >= _maxFails;
    }

    /// <summary>실패를 한 번 기록하고 **남은 시도 횟수**를 돌려준다. 0 이면 지금부터 잠긴 것이다.</summary>
    public int Fail(string key, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(key)) return _maxFails;
        lock (_gate)
        {
            if (_fails.Count >= MaxKeys && !_fails.ContainsKey(key)) Sweep(nowUtc);
            if (!_fails.TryGetValue(key, out var list)) _fails[key] = list = new List<DateTime>();
            Prune(list, nowUtc);
            list.Add(nowUtc);
            return Math.Max(0, _maxFails - list.Count);
        }
    }

    /// <summary>로그인에 성공했다. 이 열쇠의 실패 기록을 지운다.</summary>
    public void Succeed(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        lock (_gate) _fails.Remove(key);
    }

    /// <summary>잠금이 풀릴 때까지 남은 시간. 안내 문구("N분 뒤에 다시")에 쓴다.</summary>
    public TimeSpan Remaining(string key, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(key)) return TimeSpan.Zero;
        lock (_gate)
        {
            if (!_fails.TryGetValue(key, out var list)) return TimeSpan.Zero;
            Prune(list, nowUtc);
            if (list.Count < _maxFails) return TimeSpan.Zero;
            // 가장 오래된 기록이 창을 벗어나면 한 번 더 시도할 수 있게 된다.
            var left = _window - (nowUtc - list[0]);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>창 안에 남은 실패 횟수. 세는 김에 지난 기록을 버린다.</summary>
    private int Recent(string key, DateTime nowUtc)
    {
        if (!_fails.TryGetValue(key, out var list)) return 0;
        Prune(list, nowUtc);
        if (list.Count == 0) _fails.Remove(key);
        return list.Count;
    }

    /// <summary>기록은 시간 순으로 쌓이므로 앞에서부터 지나간 것만 떼면 된다.</summary>
    private void Prune(List<DateTime> list, DateTime nowUtc)
    {
        int drop = 0;
        while (drop < list.Count && nowUtc - list[drop] >= _window) drop++;
        if (drop > 0) list.RemoveRange(0, drop);
    }

    private void Sweep(DateTime nowUtc)
    {
        var dead = new List<string>();
        foreach (var (k, list) in _fails)
        {
            Prune(list, nowUtc);
            if (list.Count == 0) dead.Add(k);
        }
        foreach (var k in dead) _fails.Remove(k);
    }
}
