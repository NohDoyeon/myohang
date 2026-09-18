using System.Collections.Concurrent;
using Harbor.Server.Config;
using Harbor.Server.Data;
using Harbor.Server.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Harbor.Server.Rooms;

/// <summary>룸 인스턴스 레지스트리. 공용 방은 기동 시, 개인 방은 저장본 복원 또는 로그인 시 생성.</summary>
public sealed class RoomManager
{
    private readonly ConcurrentDictionary<long, RoomInstance> _rooms = new();
    private readonly DefinitionStore _defs;
    private readonly SaveStore _save;
    private readonly ServerOptions _opt;
    private readonly EconomyOptions _eco;
    private readonly ILoggerFactory _lf;
    private long _nextId;

    public RoomManager(DefinitionStore defs, SaveStore save, IOptions<ServerOptions> opt, IOptions<EconomyOptions> eco, ILoggerFactory lf)
    { _defs = defs; _save = save; _opt = opt.Value; _eco = eco.Value; _lf = lf; }

    public RoomInstance? Get(long id) => _rooms.GetValueOrDefault(id);

    /// <summary>같은 키(공용=템플릿, 개인=닉)의 저장된 가구 배치가 있으면 함께 복원한다. seed 를 주면 그게 이긴다(방 넓히기).</summary>
    public RoomInstance Create(string templateId, long ownerId = 0, string ownerNick = "", string? name = null, IReadOnlyList<ItemSave>? seed = null)
    {
        var def = _defs.Rooms[templateId];
        var id = Interlocked.Increment(ref _nextId);
        var saved = _save.GetRoom(SaveStore.RoomKey(ownerNick, templateId));
        var room = new RoomInstance(id, def, _defs, _opt.TickMs, _lf.CreateLogger<RoomInstance>(),
                                    ownerId, ownerNick, name ?? saved?.Name, _save, seed ?? saved?.Items ?? TemplateFurni(def), _eco);
        _rooms[id] = room;
        return room;
    }

    /// <summary>
    /// 방을 다음 템플릿으로 넓힌다. 실제 작업은 옛 방의 루프에서 일어난다 — 그래야 그 순간 놓여 있던 가구를
    /// 그대로 넘길 수 있고, 옛 방이 뒤늦게 저장해 새 방(같은 키)을 덮어쓰는 일도 없다.
    /// 넓힌 템플릿은 이전 템플릿의 걸을 수 있는 칸을 같은 좌표로 품도록 만들어져 있다(data/rooms/*.json 의 _note).
    /// </summary>
    public void Upgrade(RoomInstance old, Session s, string reason, long refund)
    {
        if (old.NextTemplate is not { } next) return;
        old.Post(new RoomCommand.Upgrade(s, reason, refund, items =>
        {
            var room = Create(next.RoomId, old.OwnerId, old.OwnerNick, old.Name, items);
            _rooms.TryRemove(old.Id, out _);      // 옛 방을 빼야 방 목록·FindHomeByNick 에 남지 않는다
            return room;
        }));
    }

    /// <summary>
    /// 다른 계열의 집으로 이사. 방 모양이 달라 가구를 제자리에 둘 수 없으므로 **빈 방으로 새로 짓고**,
    /// 놓여 있던 가구는 룸 루프가 주인 가방으로 돌려준다.
    /// 저장 키(u:닉)는 그대로라 새 방이 같은 자리를 이어받는다 — 그래서 옛 배치를 seed 로 읽지 않도록 빈 목록을 명시한다.
    /// </summary>
    public void Remodel(RoomInstance old, Session s, string templateId, string reason, long refund)
    {
        if (!_defs.Rooms.TryGetValue(templateId, out var next) || next.Kind == "public") return;
        old.Post(new RoomCommand.Upgrade(s, reason, refund, items =>
        {
            // items 는 쓰지 않는다 — 이사는 빈 방으로 시작하고, 가구는 룸 루프가 주인 가방으로 돌려준다.
            var room = Create(next.RoomId, old.OwnerId, old.OwnerNick, old.Name, Array.Empty<ItemSave>());
            _rooms.TryRemove(old.Id, out _);
            return room;
        }, ReturnItems: true));
    }

    /// <summary>
    /// 이사할 수 있는 집 목록. 계열마다 한 채씩, **지금 단계와 같은 단계**(그 계열에 없으면 마지막 단계)를 고른다.
    /// 단계를 유지해야 넓히는 데 쓴 루피가 이사 한 번에 날아가지 않는다.
    /// </summary>
    public IEnumerable<RoomDef> HouseStyles(int tier)
        => _defs.Rooms.Values
            .Where(r => r.Kind != "public" && r.Family.Length > 0)
            .GroupBy(r => r.Family)
            .Select(g => g.OrderBy(r => r.Tier).LastOrDefault(r => r.Tier <= tier) ?? g.OrderBy(r => r.Tier).First())
            .OrderBy(r => r.Family);

    /// <summary>저장본이 없을 때만 쓰는 템플릿 기본 배치 (예: 댄스홀의 콜라 자판기).</summary>
    private static List<ItemSave> TemplateFurni(RoomDef def)
        => def.Furni.Select(f => new ItemSave
        {
            FurniId = f.FurniId, X = (short)f.X, Y = (short)f.Y, Dir = (byte)f.Dir,
            WallU = (byte)f.WallU, WallV = (byte)f.WallV, Tilt = (sbyte)f.Tilt,
        }).ToList();

    /// <summary>저장된 개인 방을 전부 되살린다 — 주인이 접속하지 않아도 방 목록에 보이고 놀러 갈 수 있다.</summary>
    public int RestoreSaved()
    {
        int n = 0;
        foreach (var (key, s) in _save.AllRooms())
        {
            if (!key.StartsWith("u:", StringComparison.Ordinal)) continue;   // 공용 방은 Create 시점에 복원됨
            if (s.OwnerNick.Length == 0 || !_defs.Rooms.ContainsKey(s.TemplateId)) continue;
            if (FindHomeByNick(s.OwnerNick) is not null) continue;
            Create(s.TemplateId, 0, s.OwnerNick, s.Name);                    // OwnerId 는 그 유저가 로그인할 때 채워진다
            n++;
        }
        return n;
    }

    /// <summary>닉으로 개인 방 찾기 (재접속 시 같은 방을 돌려주기 위해).</summary>
    public RoomInstance? FindHomeByNick(string nick)
        => _rooms.Values.FirstOrDefault(r => r.OwnerNick.Length > 0 && string.Equals(r.OwnerNick, nick, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<RoomInstance> All => _rooms.Values;
}
