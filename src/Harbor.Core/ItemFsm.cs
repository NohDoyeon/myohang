namespace Harbor.Core;

/// <summary>JSON 선언형 가구 상태 머신 정의.</summary>
public sealed record FsmTransition(string From, string On, string To, string? Effect);

public sealed class ItemFsm
{
    public string Initial { get; }
    private readonly List<FsmTransition> _t;

    public ItemFsm(string initial, IEnumerable<FsmTransition> transitions)
    {
        Initial = initial;
        _t = transitions.ToList();
    }

    /// <summary>이벤트 적용. 전이 없으면 null.</summary>
    public FsmTransition? Apply(string current, string @event)
        => _t.FirstOrDefault(t => t.From == current && t.On == @event);

    /// <summary>"timer:5000" 형식 전이의 지연(ms). 없으면 null.</summary>
    public int? TimerFor(string state)
    {
        var t = _t.FirstOrDefault(t => t.From == state && t.On.StartsWith("timer:", StringComparison.Ordinal));
        return t is null ? null : int.Parse(t.On.AsSpan(6));
    }
}
