using System.Threading.Channels;
using Harbor.Core;
using Harbor.Protocol;
using Harbor.Server.Data;
using Harbor.Server.Net;
using Microsoft.Extensions.Logging;

namespace Harbor.Server.Rooms;

public sealed class RoomItem
{
    public long Id; public FurniDef Def = null!; public int X, Y; public float Z; public byte Dir;
    public string State = ""; public string? Extra; public ItemFsm? Fsm;
    public string Author = "";   // 놓은 사람 닉 (포스트잇 글쓴이)
    public byte WallU;           // 벽 슬롯 가로 (벽걸이만)
    public byte WallV;           // 벽 슬롯 세로
    public sbyte Tilt;           // 기울기(도)
    /// <summary>지금 상태가 시작된 시각. 재시작 후에도 남은 시간을 이어가기 위해 저장한다(자라는 식물 등).</summary>
    public DateTime StateAtUtc = DateTime.UtcNow;
    /// <summary>밀린 시간을 따라잡는 중 연속으로 건너뛴 단계 수. 순환 FSM 이 폭주하지 않도록 상한을 둔다.</summary>
    public int CatchUpSteps;
    public bool IsPostit => Def.Interaction.Type == "postit";
    public ItemDto ToDto() => new()
    {
        Id = Id, FurniId = Def.FurniId, X = (short)X, Y = (short)Y, Z = Z, Dir = Dir, State = State, Extra = Extra,
        Name = Def.DisplayName, Solid = Def.Solid, Interaction = Def.Interaction.Type, Wall = Def.Wall, Author = Author,
        WallU = WallU, WallV = WallV, Tilt = Tilt, Usable = Usable,
    };
    /// <summary>지금 상태에서 '사용'이 먹히는가. 자라는 중인 식물은 false 라 클라가 안내를 띄운다.</summary>
    public bool Usable => Fsm?.Apply(State, "use") is not null;
    public ItemSave ToSave() => new()
    {
        FurniId = Def.FurniId, X = (short)X, Y = (short)Y, Dir = Dir, WallU = WallU, WallV = WallV, Tilt = Tilt,
        State = State, StateAt = StateAtUtc.ToString("o"), Extra = Extra, Author = Author,
    };
}

public sealed class RoomUser
{
    public Session S = null!; public int X, Y; public float Z; public byte Dir; public string Action = "stand";
    public Queue<(int x, int y)> Path = new();
    public UserDto ToDto() => new() { Id = S.UserId, Nick = S.Nick, Figure = S.Figure, X = (short)X, Y = (short)Y, Z = Z, Dir = Dir, Action = Action };
}

/// <summary>룸 1개 = 단일 소비자 루프. 락 없음. (OwnerId/UserCount 만 바깥에서 읽는다.)</summary>
public sealed class RoomInstance
{
    public long Id { get; }
    public RoomDef Def { get; }
    public string Name { get; }
    /// <summary>0 = 공용. 같은 닉 재로그인 시 새 UserId 로 갱신됨.</summary>
    public long OwnerId { get; set; }
    public string OwnerNick { get; }
    public int UserCount => Volatile.Read(ref _userCount);

    private readonly Heightmap _map;
    private readonly Walls _walls;
    private readonly DefinitionStore _defs;
    private readonly ILogger _log;
    private readonly Channel<RoomCommand> _inbox = Channel.CreateUnbounded<RoomCommand>(new() { SingleReader = true });
    private readonly Dictionary<long, RoomUser> _users = new();
    private readonly Dictionary<long, RoomItem> _items = new();
    private readonly Dictionary<(int, int), RoomItem> _solidAt = new();
    private static long _nextItemId = 1;
    private readonly int _tickMs;
    private readonly SaveStore? _save;
    private int _userCount;

    public RoomInstance(long id, RoomDef def, DefinitionStore defs, int tickMs, ILogger log,
                        long ownerId = 0, string ownerNick = "", string? name = null,
                        SaveStore? save = null, IReadOnlyList<ItemSave>? seed = null)
    {
        Id = id; Def = def; _defs = defs; _tickMs = tickMs; _log = log; _save = save;
        OwnerId = ownerId; OwnerNick = ownerNick; Name = name ?? def.Name;
        _map = new Heightmap(def.Heightmap);
        _walls = new Walls(_map);
        if (seed is not null) Seed(seed);      // 루프 시작 전 — 경합 없음
        // 개인 방은 가구가 없어도 "존재"가 저장돼야 한다. 그래야 주인이 접속 안 해도 방 목록에 뜨고 놀러 갈 수 있다.
        if (OwnerNick.Length > 0) SaveRoom();
        _ = Task.Run(Loop);
        _ = Task.Run(TickPump);
    }

    /// <summary>
    /// 가구 배치 복원 — 저장본과 템플릿 기본 배치 양쪽이 여기를 지난다.
    /// 정의가 사라진 가구만 버리고, 위치가 어긋난 것은 **버리지 않고 경고 후 보정**한다.
    /// (저장본이 섞여 오므로 조용히 버리면 사용자가 놓은 가구가 사라진다.)
    /// </summary>
    private void Seed(IReadOnlyList<ItemSave> seed)
    {
        foreach (var s in seed)
        {
            if (!_defs.Furni.TryGetValue(s.FurniId, out var def)) { _log.LogWarning("seed: unknown furni {F}", s.FurniId); continue; }

            byte dir = s.Dir, u = s.WallU, v = s.WallV;
            if (def.Wall)
            {
                int rows = Math.Max(1, Def.Wall.Height);
                if (!_walls.CanHang(s.X, s.Y, dir))
                {
                    // 그 모서리에 벽이 없다 — 반대쪽 벽이라도 있으면 그쪽으로, 아니면 그대로 두고 알린다.
                    byte other = dir == 4 ? (byte)2 : (byte)4;
                    if (_walls.CanHang(s.X, s.Y, other))
                    { _log.LogWarning("seed: no wall for {F} at {X},{Y} dir {D} — dir {O} 로 보정", s.FurniId, s.X, s.Y, dir, other); dir = other; }
                    else
                        _log.LogWarning("seed: no wall for {F} at {X},{Y} dir {D} — 그대로 둠(렌더 위치가 어긋날 수 있음)", s.FurniId, s.X, s.Y, dir);
                }
                if (!Walls.SlotValid(u, v, rows))
                {
                    byte cu = (byte)Math.Clamp((int)u, 0, Walls.SlotCols - 1), cv = (byte)Math.Clamp((int)v, 0, rows - 1);
                    _log.LogWarning("seed: bad wall slot {F} at {X},{Y} u{U} v{V} — u{CU} v{CV} 로 보정", s.FurniId, s.X, s.Y, u, v, cu, cv);
                    u = cu; v = cv;
                }
            }
            else if (!_map.Walkable(s.X, s.Y))
                _log.LogWarning("seed: unwalkable tile for {F} at {X},{Y}", s.FurniId, s.X, s.Y);

            var fsm = def.BuildFsm();
            var item = new RoomItem
            {
                Id = Interlocked.Increment(ref _nextItemId), Def = def, X = s.X, Y = s.Y, Z = _map.Height(s.X, s.Y),
                Dir = dir, WallU = def.Wall ? u : (byte)0, WallV = def.Wall ? v : (byte)0,
                Tilt = def.Wall ? Walls.ClampTilt(s.Tilt) : (sbyte)0, Author = s.Author,
                State = def.States.Contains(s.State) ? s.State : (fsm?.Initial ?? def.States[0]), Extra = s.Extra, Fsm = fsm,
            };
            // 상태 시작 시각을 되살려 "그동안 흐른 시간"을 인정한다. 값이 없으면 지금부터.
            if (DateTime.TryParse(s.StateAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at))
                item.StateAtUtc = at.ToUniversalTime();

            _items[item.Id] = item;
            if (def.Solid) for (int dx = 0; dx < def.Footprint.W; dx++) for (int dy = 0; dy < def.Footprint.H; dy++) _solidAt[(item.X + dx, item.Y + dy)] = item;
            ScheduleTimer(item);   // 루프 시작 전이라도 Post 는 채널에 쌓이므로 안전
        }
    }

    /// <summary>
    /// 현재 상태에 타이머 전이가 있으면 **남은 시간만큼** 예약한다.
    /// 서버가 꺼져 있던 동안 지났으면 즉시 발화 → 식물이 자란 채로 돌아온다.
    /// </summary>
    private void ScheduleTimer(RoomItem item)
    {
        if (item.Fsm?.TimerFor(item.State) is not { } ms) return;
        var expected = item.State;
        double remaining = ms - (DateTime.UtcNow - item.StateAtUtc).TotalMilliseconds;
        int delay = (int)Math.Clamp(remaining, 0, int.MaxValue);
        _ = Task.Delay(delay).ContinueWith(_ => Post(new RoomCommand.ItemTimer(item.Id, expected)));
    }

    public string SaveKey => SaveStore.RoomKey(OwnerNick, Def.RoomId);

    /// <summary>방을 넓히며 갈아탄 뒤의 껍데기. 더 이상 저장하지 않는다(새 인스턴스가 같은 키를 쓰므로 덮어쓰면 안 된다).</summary>
    private bool _closed;

    /// <summary>룸 루프 스레드에서만 호출 (_items 안전).</summary>
    private void SaveRoom()
    {
        if (_closed) return;
        _save?.UpdateRoom(SaveKey, Def.RoomId, Name, OwnerNick, _items.Values.Select(i => i.ToSave()).ToList());
    }

    public void Post(RoomCommand cmd) => _inbox.Writer.TryWrite(cmd);

    public RoomInfo ToInfo() => new() { Id = Id, Name = Name, Kind = Def.Kind, OwnerId = OwnerId, OwnerNick = OwnerNick, Users = UserCount, MaxUsers = Def.MaxUsers };

    /// <summary>이 방을 넓히면 되는 다음 템플릿. 없으면 최종 단계.</summary>
    public RoomDef? NextTemplate => Def.Upgrade.Length > 0 ? _defs.Rooms.GetValueOrDefault(Def.Upgrade) : null;

    private async Task TickPump()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_tickMs));
        try { while (await timer.WaitForNextTickAsync(_stopping.Token)) Post(new RoomCommand.Tick()); }
        catch (OperationCanceledException) { /* 방이 닫혔다 */ }
    }

    private readonly CancellationTokenSource _stopping = new();

    private async Task Loop()
    {
        await foreach (var cmd in _inbox.Reader.ReadAllAsync())
        {
            try { Handle(cmd); }
            catch (Exception ex) { _log.LogError(ex, "room {Id} cmd {Cmd} failed", Id, cmd.GetType().Name); }
        }
    }

    // ---------------- command handlers ----------------
    private void Handle(RoomCommand cmd)
    {
        switch (cmd)
        {
            case RoomCommand.Enter c: OnEnter(c.S); break;
            case RoomCommand.Leave c: OnLeave(c.S); break;
            case RoomCommand.Move c: OnMove(c.S, c.X, c.Y); break;
            case RoomCommand.Action c: OnAction(c.S, c.Name); break;
            case RoomCommand.Figure c:
                if (_users.ContainsKey(c.S.UserId))
                    Broadcast(Opcode.S_UserFigure, new S_UserFigure { UserId = c.S.UserId, Figure = c.S.Figure });
                break;
            case RoomCommand.Chat c: Broadcast(Opcode.S_ChatBubble, new S_ChatBubble { UserId = c.S.UserId, Text = c.Text[..Math.Min(120, c.Text.Length)], Kind = c.Kind }); break;
            case RoomCommand.PlaceItem c: OnPlace(c.S, c.FurniId, c.X, c.Y, c.Dir, c.WallU, c.WallV, c.Tilt); break;
            case RoomCommand.PickItem c: OnPick(c.S, c.ItemId); break;
            case RoomCommand.UseItem c: OnUse(c.S, c.ItemId); break;
            case RoomCommand.PostitWrite c: OnPostitWrite(c.S, c.ItemId, c.Body); break;
            case RoomCommand.ItemTimer c: OnItemTimer(c.ItemId, c.ExpectedState); break;
            case RoomCommand.Evacuate c: OnEvacuate(c.Target, c.Reason); break;
            case RoomCommand.Upgrade c: OnUpgrade(c.S, c.Reason, c.Refund, c.Build, c.ReturnItems); break;
            case RoomCommand.Tick: OnTick(); break;
        }
    }

    private void OnEnter(Session s)
    {
        if (_users.Count >= Def.MaxUsers)
        {
            _log.LogWarning("enter failed: {Nick} → {Room} (가득 참 {Count}/{Max})", s.Nick, Name, _users.Count, Def.MaxUsers);
            s.Send(Opcode.S_Error, new S_Error { Code = 10, Message = "room full" });
            return;
        }
        var spawn = Def.Spawn.Length > 0 ? Def.Spawn[0] : Def.Door;
        var u = new RoomUser { S = s, X = spawn.X, Y = spawn.Y, Z = _map.Height(spawn.X, spawn.Y), Dir = (byte)spawn.Dir };
        _users[s.UserId] = u;
        Volatile.Write(ref _userCount, _users.Count);
        s.Room = this;
        s.Send(Opcode.S_RoomSnapshot, new S_RoomSnapshot
        {
            Room = new RoomDto
            {
                Id = Id, TemplateId = Def.RoomId, Name = Name, Heightmap = Def.Heightmap,
                DoorX = (short)Def.Door.X, DoorY = (short)Def.Door.Y, WallStyle = Def.Wall.Style, FloorStyle = Def.Floor.Style,
                WallHeight = (byte)Math.Clamp(Def.Wall.Height, 0, 8), OwnerId = OwnerId, OwnerNick = OwnerNick, Kind = Def.Kind,
                UpgradePrice = NextTemplate is null ? 0 : Def.UpgradePrice, UpgradeName = NextTemplate?.Name ?? "",
                Width = _map.W, Height = _map.H,
            },
            Items = _items.Values.Select(i => i.ToDto()).ToList(),
            Users = _users.Values.Select(x => x.ToDto()).ToList()
        });
        Broadcast(Opcode.S_UserEnter, new S_UserEnter { User = u.ToDto() }, except: s.UserId);
        _log.LogInformation("enter: {Nick} → {Room} ({Count}명)", s.Nick, Name, _users.Count);
    }

    private void OnLeave(Session s)
    {
        if (!_users.Remove(s.UserId)) return;
        Volatile.Write(ref _userCount, _users.Count);
        if (s.Room == this) s.Room = null;
        Broadcast(Opcode.S_UserLeave, new S_UserLeave { UserId = s.UserId });
        _log.LogInformation("leave: {Nick} ← {Room} ({Count}명)", s.Nick, Name, _users.Count);
    }

    /// <summary>
    /// 방 갈아타기 — 지금 놓인 가구를 새 방에 넘기고(넓히기) 또는 주인 가방으로 돌려주고(이사) 사람들을 옮긴다.
    /// 가구 목록을 룸 루프에서 읽으므로, 갈아타는 순간 놓여 있던 것이 하나도 빠지지 않는다.
    /// </summary>
    private void OnUpgrade(Session s, string reason, long refund, Func<IReadOnlyList<ItemSave>, RoomInstance?> build, bool returnItems)
    {
        if (_closed)   // 요청이 겹쳐 이미 갈아탄 방 — 받은 값을 되돌려 준다
        {
            if (refund > 0) { s.RupeeAdd(refund, "방 넓히기 취소"); s.SendWallet(); }
            s.Send(Opcode.S_Error, new S_Error { Code = 33, Message = "already upgraded" });
            return;
        }
        _closed = true;      // 이 뒤로는 저장하지 않는다 — 같은 키를 쓰는 새 방이 진짜다
        // 이사는 방 모양이 달라 가구를 그 자리에 둘 수 없다 → 새 방은 빈 채로 만들고, 가구는 아래에서 가방으로 돌려준다.
        var target = build(returnItems ? Array.Empty<ItemSave>() : _items.Values.Select(i => i.ToSave()).ToList());
        if (target is null)
        {
            _closed = false;
            if (refund > 0) { s.RupeeAdd(refund, "방 넓히기 취소"); s.SendWallet(); }
            s.Send(Opcode.S_Error, new S_Error { Code = 33, Message = "cannot upgrade" });
            return;
        }
        if (returnItems)
        {
            // 새 방이 확정된 뒤에 돌려준다 — 중간에 실패해도 가구가 두 벌이 되지 않게.
            int n = 0;
            foreach (var item in _items.Values) { s.SendInventoryUpdate(item.Def, s.InvAdd(item.Def.FurniId, 1)); n++; }
            _items.Clear(); _solidAt.Clear();
            if (n > 0) s.Notice($"놓아 두었던 가구 {n}개를 가방에 넣었어요.");
            _log.LogInformation("remodel: {Nick} 의 방 가구 {Count}개를 가방으로", s.Nick, n);
        }
        if (target.OwnerId == s.UserId) s.HomeRoomId = target.Id;   // 방 id 가 바뀌었다 — 로그인 때 받은 값은 이제 옛것
        OnEvacuate(target, reason);
    }

    /// <summary>
    /// 안에 있던 사람을 전부 Target 으로 옮기고 이 인스턴스를 닫는다.
    /// 닫는 순간부터 저장하지 않는다 — 이 뒤로 SaveRoom 이 돌면 같은 키(u:닉)를 쓰는 새 방의 기록을 덮어쓴다.
    /// </summary>
    private void OnEvacuate(RoomInstance target, string reason)
    {
        _closed = true;
        foreach (var u in _users.Values.ToList())
        {
            var s = u.S;
            _users.Remove(s.UserId);
            if (s.Room == this) s.Room = null;
            if (reason.Length > 0) s.Notice(reason);
            target.Post(new RoomCommand.Enter(s));
        }
        Volatile.Write(ref _userCount, 0);
        _log.LogInformation("evacuate: {Room} → {Target} ({Reason})", Name, target.Name, reason);
        _stopping.Cancel();
        _inbox.Writer.TryComplete();
    }

    private void OnMove(Session s, int x, int y)
    {
        if (!_users.TryGetValue(s.UserId, out var u)) return;
        var path = AStar.Find(_map, IsBlocked, (u.X, u.Y), (x, y));
        if (path.Count < 2) return;
        u.Path = new Queue<(int, int)>(path.Skip(1));
        u.Action = "walk";
        Broadcast(Opcode.S_UserPath, new S_UserPath { UserId = s.UserId, Path = path.Skip(1).Select(p => new TilePos { X = (short)p.x, Y = (short)p.y }).ToList() });
    }

    private void OnTick()
    {
        // 1틱 = 1타일 진행 (클라이언트는 보간). 보행 완료 시 seat 판정.
        foreach (var u in _users.Values)
        {
            if (u.Path.Count == 0) continue;
            var next = u.Path.Dequeue();
            u.Dir = (byte)Iso.DirectionBetween((u.X, u.Y), next);
            u.X = next.x; u.Y = next.y; u.Z = _map.Height(u.X, u.Y);
            if (u.Path.Count == 0)
            {
                u.Action = "stand";
                if (_items.Values.FirstOrDefault(i => i.X == u.X && i.Y == u.Y && i.Def.Interaction.Type == "seat") is { } seat)
                { u.Action = "sit"; u.Dir = seat.Dir; u.Z += seat.Def.Height; }
                Broadcast(Opcode.S_UserAction, new S_UserAction { UserId = u.S.UserId, Action = u.Action, Dir = u.Dir });
            }
        }
    }

    private void OnAction(Session s, string name)
    {
        if (!_users.TryGetValue(s.UserId, out var u)) return;
        if (name is not ("stand" or "dance" or "wave" or "laugh" or "cry" or "angry" or "sleep")) return;
        u.Action = name;
        Broadcast(Opcode.S_UserAction, new S_UserAction { UserId = s.UserId, Action = name, Dir = u.Dir });
    }

    /// <summary>공용 방은 누구나, 개인 방은 주인만 꾸밀 수 있다.</summary>
    private bool CanEdit(Session s) => Def.Kind == "public" || (OwnerId != 0 && OwnerId == s.UserId);

    private void OnPlace(Session s, string furniId, int x, int y, byte dir, byte wallU, byte wallV, sbyte tilt)
    {
        if (!_users.ContainsKey(s.UserId)) return;
        if (!_defs.Furni.TryGetValue(furniId, out var def)) { s.Send(Opcode.S_Error, new S_Error { Code = 20, Message = "unknown furni" }); return; }
        bool postit = def.Interaction.Type == "postit";                 // 포스트잇(방명록)은 손님도 붙일 수 있다
        if (!postit && !CanEdit(s)) { s.Send(Opcode.S_Error, new S_Error { Code = 22, Message = "not your room" }); return; }
        if (!def.Rotations.Contains(dir)) dir = (byte)def.Rotations[0];

        if (def.Wall)
        {
            if (!_walls.CanHang(x, y, dir) || !Walls.SlotValid(wallU, wallV, Def.Wall.Height)) { s.Send(Opcode.S_Error, new S_Error { Code = 23, Message = "no wall here" }); return; }
            if (_items.Values.Any(i => i.Def.Wall && i.X == x && i.Y == y && i.Dir == dir && i.WallU == wallU && i.WallV == wallV)) { s.Send(Opcode.S_Error, new S_Error { Code = 21, Message = "tile occupied" }); return; }
        }
        else
        {
            for (int dx = 0; dx < def.Footprint.W; dx++) for (int dy = 0; dy < def.Footprint.H; dy++)
                if (!_map.Walkable(x + dx, y + dy) || _solidAt.ContainsKey((x + dx, y + dy)) || (def.Solid && _users.Values.Any(u => u.X == x + dx && u.Y == y + dy)))
                { s.Send(Opcode.S_Error, new S_Error { Code = 21, Message = "tile occupied" }); return; }
        }
        if (!s.InvTryTake(furniId, out int remaining)) { s.Send(Opcode.S_Error, new S_Error { Code = 24, Message = "not in inventory" }); return; }

        var fsm = def.BuildFsm();
        var item = new RoomItem
        {
            Id = Interlocked.Increment(ref _nextItemId), Def = def, X = x, Y = y, Z = _map.Height(x, y), Dir = dir,
            State = fsm?.Initial ?? def.States[0], Fsm = fsm, Author = s.Nick,
            WallU = def.Wall ? wallU : (byte)0, WallV = def.Wall ? wallV : (byte)0,
            Tilt = def.Wall ? Walls.ClampTilt(tilt) : (sbyte)0,
        };
        _items[item.Id] = item;
        if (def.Solid) for (int dx = 0; dx < def.Footprint.W; dx++) for (int dy = 0; dy < def.Footprint.H; dy++) _solidAt[(x + dx, y + dy)] = item;
        Broadcast(Opcode.S_ItemAdd, new S_ItemAdd { Item = item.ToDto() });
        s.SendInventoryUpdate(def, remaining);
        SaveRoom();
        ScheduleTimer(item);   // 놓자마자 시간이 흐르기 시작하는 가구(식물 등)
    }

    private void OnPick(Session s, long itemId)
    {
        if (!_users.ContainsKey(s.UserId)) return;
        if (!_items.TryGetValue(itemId, out var item)) return;
        // 방 주인(또는 공용 방 누구나) + 포스트잇은 글쓴이 본인도 뗄 수 있다
        if (!CanEdit(s) && !(item.IsPostit && item.Author == s.Nick)) { s.Send(Opcode.S_Error, new S_Error { Code = 22, Message = "not your room" }); return; }
        _items.Remove(itemId);
        foreach (var k in _solidAt.Where(kv => kv.Value == item).Select(kv => kv.Key).ToList()) _solidAt.Remove(k);
        Broadcast(Opcode.S_ItemRemove, new S_ItemRemove { ItemId = itemId });
        s.SendInventoryUpdate(item.Def, s.InvAdd(item.Def.FurniId, 1));
        SaveRoom();
    }

    private void OnUse(Session s, long itemId)
    {
        if (!_items.TryGetValue(itemId, out var item) || item.Fsm is null) return;
        if (!_users.TryGetValue(s.UserId, out var u)) return;
        if (Math.Max(Math.Abs(u.X - item.X), Math.Abs(u.Y - item.Y)) > 1) return;   // 인접 타일만
        var t = item.Fsm.Apply(item.State, "use");
        if (t is null) return;
        Transition(item, t, s);
    }

    /// <summary>포스트잇 본문 쓰기: 글쓴이 본인만, 200자. State "written" + Extra=본문 으로 방송.</summary>
    private void OnPostitWrite(Session s, long itemId, string body)
    {
        if (!_users.ContainsKey(s.UserId)) return;
        if (!_items.TryGetValue(itemId, out var item) || !item.IsPostit) return;
        if (item.Author != s.Nick) { s.Send(Opcode.S_Error, new S_Error { Code = 25, Message = "not your postit" }); return; }
        body = body.Trim();
        if (body.Length > 200) body = body[..200];
        item.Extra = body;
        item.State = body.Length > 0 ? "written" : "blank";
        Broadcast(Opcode.S_ItemState, new S_ItemState { ItemId = item.Id, State = item.State, Extra = item.Extra, Usable = item.Usable });
        SaveRoom();
    }

    /// <summary>
    /// 타이머 만기. 서버가 꺼져 있던 동안 여러 단계가 한꺼번에 밀렸을 수 있으므로,
    /// **지금이 아니라 원래 만기 시각**을 새 상태의 시작으로 삼는다 → 남은 밀린 시간이 다음 단계로 이어진다.
    /// (이렇게 하지 않으면 재시작 한 번에 한 단계씩만 자란다.)
    /// </summary>
    private void OnItemTimer(long itemId, string expected)
    {
        if (!_items.TryGetValue(itemId, out var item) || item.Fsm is null || item.State != expected) return;
        if (item.Fsm.TimerFor(item.State) is not { } ms) return;
        var t = item.Fsm.Apply(item.State, $"timer:{ms}");
        if (t is null) return;

        var due = item.StateAtUtc.AddMilliseconds(ms);
        bool behind = due <= DateTime.UtcNow && item.CatchUpSteps < MaxCatchUpSteps;
        item.CatchUpSteps = behind ? item.CatchUpSteps + 1 : 0;
        Transition(item, t, null, behind ? due : null);
    }

    /// <summary>한 번에 따라잡을 수 있는 단계 수. 순환 타이머가 있는 가구가 생겨도 폭주하지 않게.</summary>
    private const int MaxCatchUpSteps = 200;

    /// <param name="atUtc">새 상태가 시작된 것으로 칠 시각. null 이면 지금(보통의 전이). 밀린 시간 따라잡기에만 과거 시각이 온다.</param>
    private void Transition(RoomItem item, FsmTransition t, Session? actor, DateTime? atUtc = null)
    {
        item.State = t.To;
        item.StateAtUtc = atUtc ?? DateTime.UtcNow;
        if (atUtc is null) item.CatchUpSteps = 0;
        Broadcast(Opcode.S_ItemState, new S_ItemState { ItemId = item.Id, State = item.State, Extra = item.Extra, Usable = item.Usable });
        SaveRoom();
        if (t.Effect is not null && actor is not null) ApplyEffect(t.Effect, actor);
        ScheduleTimer(item);
    }

    private void ApplyEffect(string effect, Session actor)
    {
        var parts = effect.Split(':');
        switch (parts[0])
        {
            case "give_item":
                if (_defs.Furni.TryGetValue(parts[1], out var d)) actor.SendInventoryUpdate(d, actor.InvAdd(parts[1], 1));
                break;
            case "emote": Broadcast(Opcode.S_UserAction, new S_UserAction { UserId = actor.UserId, Action = parts[1], Dir = _users[actor.UserId].Dir }); break;
            default: _log.LogWarning("unknown effect {E}", effect); break;
        }
    }

    // ---------------- helpers ----------------
    private bool IsBlocked(int x, int y) => _solidAt.ContainsKey((x, y));

    private void Broadcast<T>(Opcode op, T body, long except = -1)
    {
        foreach (var u in _users.Values) if (u.S.UserId != except) u.S.Send(op, body);
    }
}
