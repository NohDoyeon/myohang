using Godot;
using Harbor.Core;
using Harbor.Protocol;

namespace HarborClient;

/// <summary>
/// 룸 렌더러 + 입력 + 클라 상태(가방/지갑/카탈로그/방 목록). 서버 스냅샷/이벤트를 노드로 반영. 상태의 진실은 서버.
/// HUD(HudView) 는 이 클래스의 이벤트만 구독하고 공개 API 로 명령한다.
/// 조작: 좌클릭 = 이동 / 가구 사용(멀면 다가가서 사용) · 우클릭 = 줍기 · 배치 모드: 좌클릭 놓기, R 회전, 우클릭/ESC 종료
///       휠 = 줌, 휠 드래그 = 화면 이동
/// </summary>
public partial class RoomView : Node2D
{
    public enum Phase { Idle, Connecting, InRoom }

    [Export] public Node2D FloorLayer = null!;
    [Export] public Node2D ObjectLayer = null!;     // Y Sort Enabled
    [Export] public Texture2D FloorTile = null!;    // 64x32 placeholder
    [Export] public PackedScene AvatarScene = null!;
    [Export] public PackedScene FurniScene = null!;
    [Export] public string Host = "127.0.0.1";
    [Export] public int Port = 30000;
    [Export] public string Login = "tester";
    /// <summary>자동 입장(헤드리스 e2e)용 기본 비밀번호. 사람이 들어올 땐 시작 화면에서 입력받는다.</summary>
    [Export] public string Password = "harbor1234";

    public Phase State { get; private set; } = Phase.Idle;
    public string RoomName { get; private set; } = "";
    public string RoomOwnerNick { get; private set; } = "";
    public string RoomKind { get; private set; } = "";
    public long RoomId { get; private set; }
    public long HomeRoomId { get; private set; }
    public long MyId => _myId;
    public bool IsMyRoom => RoomId != 0 && RoomId == HomeRoomId;
    /// <summary>이 방을 넓히는 값. 0 이면 더 넓힐 수 없다(최종 단계이거나 공용 방).</summary>
    public long UpgradePrice { get; private set; }
    /// <summary>넓혔을 때의 방 이름 (표시용).</summary>
    public string UpgradeName { get; private set; } = "";
    public int RoomWidth { get; private set; }
    public int RoomHeight { get; private set; }
    /// <summary>공용 방은 누구나, 개인 방은 주인만 꾸밀 수 있다 (서버와 같은 규칙).</summary>
    public bool CanEdit => State == Phase.InRoom && (RoomKind == "public" || IsMyRoom);
    public int UserCount => _users.Count;
    public long Rupee { get; private set; }
    /// <summary>내 아바타 외모 (Harbor.Core.Figure 형식).</summary>
    public string MyFigure { get; private set; } = Figure.Default;
    public string? BuildFurni => _buildFurni;
    public IReadOnlyDictionary<string, InventoryEntry> Inventory => _inventory;
    public IReadOnlyList<CatalogEntry> Catalog => _catalog;
    public IReadOnlyList<RoomInfo> RoomList => _roomList;
    /// <summary>이사할 수 있는 집 계열 (서버가 지금 단계에 맞춰 골라 준다).</summary>
    public IReadOnlyList<HouseStyle> Houses => _houses;

    public event Action<Phase>? PhaseChanged;
    /// <summary>(메시지, 오류 여부). 입장 전엔 시작 패널, 입장 후엔 토스트.</summary>
    public event Action<string, bool>? Status;
    public event Action? RoomChanged;
    /// <summary>(닉, 텍스트, 시스템 메시지 여부)</summary>
    public event Action<string, string, bool>? ChatLine;
    /// <summary>(furniId 또는 null, 방향)</summary>
    public event Action<string?, byte>? BuildModeChanged;
    public event Action? WalletChanged;
    public event Action? InventoryChanged;
    public event Action? CatalogChanged;
    public event Action? RoomListChanged;
    public event Action? HouseListChanged;
    public event Action? FigureChanged;
    /// <summary>포스트잇을 클릭했거나(읽기), 내가 방금 붙였을 때(쓰기).</summary>
    public event Action<FurniSprite>? PostitOpened;
    /// <summary>방 안의 문을 눌렀을 때 — 방 목록을 연다(원작처럼 "공간 안 오브젝트"로 이동).</summary>
    public event Action? DoorClicked;
    /// <summary>복권 결과 (등수, 상금, 가격, 메시지).</summary>
    public event Action<S_LotteryResult>? LotteryResult;

    private static readonly int[] ZoomSteps = { 1, 2, 3, 4 };
    private static readonly Color RiserLeft = new("6a422a");
    private static readonly Color RiserRight = new("9a6a46");
    private const int WallUnitPx = 24;

    private Heightmap? _map;
    private Walls? _walls;
    private readonly Dictionary<(int x, int y, int dir), (Vector2 a, Vector2 b)> _wallSeg = new();   // 벽 면의 아랫변 a→b (화면 좌표)
    private float _wallH; private int _wallRows = 1;
    private (int x, int y, int dir, int u, int v)? _hoverSlot;   // 벽 배치 모드에서 마우스가 가리키는 벽 슬롯
    private readonly Dictionary<long, FurniSprite> _items = new();
    private readonly Dictionary<long, AvatarView> _users = new();
    private readonly Dictionary<string, InventoryEntry> _inventory = new();
    private readonly List<CatalogEntry> _catalog = new();
    private readonly List<RoomInfo> _roomList = new();
    private readonly List<HouseStyle> _houses = new();
    private long _myId;
    private string _loginNotice = "";
    private (int x, int y) _myTile;
    private long _pendingUse;                 // 도착 후 사용할 가구
    private string? _buildFurni; private byte _buildDir = 2; private bool _buildWall, _buildPostit;
    private sbyte _buildTilt;
    private static readonly sbyte[] TiltSteps = { 0, -7, 7, -14, 14 };
    private FurniSprite? _ghost;
    private Node2D _wallLayer = null!;
    private DoorMarker? _door;
    private (int x, int y) _doorTile = (-1, -1);
    private TileCursor _cursor = null!;
    private Camera2D _cam = null!;
    private bool _dragging; private int _zoomIdx = 1;

    public override void _Ready()
    {
        // 손으로 작성한 .tscn 의 노드/리소스 export 가 주입 안 될 때를 대비한 폴백 (에디터로 연결돼 있으면 그대로 사용).
        FloorLayer ??= GetNode<Node2D>("FloorLayer");
        ObjectLayer ??= GetNode<Node2D>("ObjectLayer");
        FloorTile ??= GD.Load<Texture2D>("res://floor_tile.png");
        AvatarScene ??= GD.Load<PackedScene>("res://Avatar.tscn");
        FurniScene ??= GD.Load<PackedScene>("res://Furni.tscn");
        _cam = GetNodeOrNull<Camera2D>("Camera2D") ?? MakeCamera();
        _cam.Zoom = Vector2.One * ZoomSteps[_zoomIdx];

        _wallLayer = new Node2D { Name = "WallLayer" };
        AddChild(_wallLayer);
        MoveChild(_wallLayer, FloorLayer.GetIndex());          // 바닥 뒤
        _cursor = new TileCursor { Name = "TileCursor", Visible = false };
        AddChild(_cursor);
        MoveChild(_cursor, FloorLayer.GetIndex() + 1);          // 바닥 위, 오브젝트 아래

        NetClient.Instance.PacketReceived += OnPacket;
        NetClient.Instance.Disconnected += OnDisconnected;

        ApplyHostOverrides();
        // 웹에서 "게임 시작"을 누르면 harbor://<주소>/<입장권> 으로 여기까지 온다 → 시작 화면 없이 바로 입장
        if (FindTicket() is { } ticket) _ = Join("", ticket);
        // 헤드리스 e2e / 빠른 개발용
        else if (OS.GetEnvironment("HARBOR_AUTOJOIN") == "1" || OS.GetCmdlineUserArgs().Contains("--autojoin")) _ = Join(Login, Password);
    }

    /// <summary>`--ticket=XXXX`, `harbor://…`, 또는 HARBOR_TICKET 환경변수에서 입장권을 찾는다.</summary>
    private static string? FindTicket()
    {
        var env = OS.GetEnvironment("HARBOR_TICKET");
        if (env.Length > 0) return Clean(env);

        foreach (var arg in OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()))
        {
            if (arg.StartsWith("--ticket=", StringComparison.Ordinal)) return Clean(arg["--ticket=".Length..]);
            if (arg.StartsWith("harbor://", StringComparison.Ordinal)) return Clean(arg);
        }
        return null;

        static string? Clean(string s) => ParseConnect(s).Ticket is { Length: >= 8 } t ? s.Trim().Trim('"') : null;
    }

    /// <summary>
    /// 입장권 문자열에서 **서버 주소까지** 뽑는다. 남의 PC 에서 실행하면 127.0.0.1 로는 아무 데도 닿지 않으므로,
    /// 웹에서 받은 코드가 주소를 함께 싣고 온다.
    /// 받는 형태: `abc…`(코드만) · `host/abc…` · `host:30000/abc…` · 앞에 `harbor://` 가 붙은 것들.
    /// </summary>
    public static (string? Host, int Port, string Ticket) ParseConnect(string raw)
    {
        string s = (raw ?? "").Trim().Trim('"');
        if (s.StartsWith("harbor://", StringComparison.OrdinalIgnoreCase)) s = s["harbor://".Length..];
        s = s.Trim('/');

        int slash = s.LastIndexOf('/');
        if (slash < 0) return (null, 0, s);                       // 주소 없이 코드만

        string authority = s[..slash], ticket = s[(slash + 1)..];
        int colon = authority.LastIndexOf(':');
        if (colon > 0 && int.TryParse(authority[(colon + 1)..], out int port) && port is > 0 and < 65536)
            return (authority[..colon], port, ticket);
        return (authority.Length > 0 ? authority : null, 0, ticket);
    }

    /// <summary>입장권으로 보이는가 — 40자리 hex 코드(주소가 앞에 붙어 있어도 된다).</summary>
    public static bool LooksLikeTicket(string raw)
    {
        var t = ParseConnect(raw).Ticket;
        return t.Length >= 32 && t.All(Uri.IsHexDigit);
    }

    /// <summary>환경변수·실행 인자로 서버 주소를 덮어쓴다(웹을 거치지 않고 바로 띄울 때).</summary>
    private void ApplyHostOverrides()
    {
        var host = OS.GetEnvironment("HARBOR_HOST");
        if (host.Length > 0) Host = host;
        if (int.TryParse(OS.GetEnvironment("HARBOR_PORT"), out int envPort) && envPort > 0) Port = envPort;

        foreach (var arg in OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()))
        {
            if (arg.StartsWith("--host=", StringComparison.Ordinal)) Host = arg["--host=".Length..].Trim();
            else if (arg.StartsWith("--port=", StringComparison.Ordinal) && int.TryParse(arg["--port=".Length..], out int p) && p > 0) Port = p;
        }
    }

    private Camera2D MakeCamera()
    {
        var c = new Camera2D { Name = "Camera2D" };
        AddChild(c); c.MakeCurrent();
        return c;
    }

    // ---------------- 공개 API (HUD 에서 호출) ----------------
    public async Task Join(string nick, string password)
    {
        if (State != Phase.Idle) return;
        // 입장권에 서버 주소가 실려 오면 그쪽으로 붙는다 — 테스터 PC 에서 127.0.0.1 은 자기 자신이다.
        if (LooksLikeTicket(password))
        {
            var (host, port, ticket) = ParseConnect(password);
            if (!string.IsNullOrWhiteSpace(host)) Host = host;
            if (port > 0) Port = port;
            password = ticket;
        }
        Login = nick;
        SetPhase(Phase.Connecting);
        Status?.Invoke($"{Host}:{Port} 에 연결하는 중…", false);
        try { await NetClient.Instance.ConnectAsync(Host, Port); }
        catch (Exception ex)
        {
            SetPhase(Phase.Idle);
            Status?.Invoke($"서버에 연결할 수 없어요 ({Host}:{Port}).\n서버를 먼저 켜고 다시 시도해 주세요.\n({ex.Message})", true);
            return;
        }
        NetClient.Instance.Send(Opcode.C_Login, new C_Login { Login = nick, Token = password });
    }

    public void SendChat(string text) { if (State == Phase.InRoom) NetClient.Instance.Send(Opcode.C_Chat, new C_Chat { Text = text }); }
    public void DoAction(string action) { if (State == Phase.InRoom) NetClient.Instance.Send(Opcode.C_Action, new C_Action { Action = action }); }
    public void EnterRoom(long roomId) { if (State == Phase.InRoom && roomId != RoomId) NetClient.Instance.Send(Opcode.C_EnterRoom, new C_EnterRoom { RoomId = roomId }); }
    public void RequestRoomList() { if (State == Phase.InRoom) NetClient.Instance.Send(Opcode.C_RoomList, new Empty()); }
    public void RequestHouseList() { if (State == Phase.InRoom) NetClient.Instance.Send(Opcode.C_HouseList, new Empty()); }
    /// <summary>다른 계열의 집으로 이사. 놓아 둔 가구는 전부 가방으로 돌아온다(서버가 처리).</summary>
    public void Remodel(string templateId) { if (State == Phase.InRoom) NetClient.Instance.Send(Opcode.C_RemodelRoom, new C_RemodelRoom { TemplateId = templateId }); }
    public void Buy(string furniId, int qty = 1) { if (State == Phase.InRoom) NetClient.Instance.Send(Opcode.C_BuyCatalog, new C_BuyCatalog { FurniId = furniId, Qty = qty }); }
    /// <summary>가방의 수확물을 판다. 값은 서버가 계산한다(묶음 먼저).</summary>
    public void Sell(string furniId, int qty = 1) { if (State == Phase.InRoom) NetClient.Instance.Send(Opcode.C_SellItem, new C_SellItem { FurniId = furniId, Qty = qty }); }
    /// <summary>내 방을 한 단계 넓힌다. 가구는 그대로 따라온다.</summary>
    public void UpgradeRoom() { if (State == Phase.InRoom) NetClient.Instance.Send(Opcode.C_UpgradeRoom, new Empty()); }
    public void DrawLottery() { if (State == Phase.InRoom) NetClient.Instance.Send(Opcode.C_LotteryDraw, new Empty()); }
    public void SetFigure(string figure) { if (State == Phase.InRoom) NetClient.Instance.Send(Opcode.C_SetFigure, new C_SetFigure { Figure = figure }); }
    public void WritePostit(long itemId, string body) { if (State == Phase.InRoom) NetClient.Instance.Send(Opcode.C_PostitWrite, new C_PostitWrite { ItemId = itemId, Body = body }); }
    public void PickItem(long itemId) { if (State == Phase.InRoom) NetClient.Instance.Send(Opcode.C_PickItem, new C_PickItem { ItemId = itemId }); }
    /// <summary>포스트잇 본문을 쓸 수 있는가 = 내가 붙인 것.</summary>
    public bool CanWrite(FurniSprite it) => it.IsPostit && it.Author == Login;
    /// <summary>뗄 수 있는가 = 방을 꾸밀 수 있거나, 내가 붙인 포스트잇.</summary>
    public bool CanRemove(FurniSprite it) => CanEdit || CanWrite(it);

    /// <summary>가방에 있는 가구로 배치 모드 시작. null = 종료. 포스트잇은 남의 방에도 붙일 수 있다.</summary>
    public void SetBuildMode(string? furniId)
    {
        if (furniId is not null)
        {
            if (!_inventory.TryGetValue(furniId, out var inv) || inv.Qty <= 0) { Status?.Invoke("가방에 없는 가구예요", true); furniId = null; }
            else if (!CanEdit && inv.Interaction != "postit") { Status?.Invoke($"이 방은 {RoomOwnerNick}님의 방이라 꾸밀 수 없어요 (포스트잇은 붙일 수 있어요)", true); furniId = null; }
        }
        _buildFurni = furniId;
        if (_ghost is not null && IsInstanceValid(_ghost)) _ghost.QueueFree();
        _ghost = null;
        if (furniId is not null && State == Phase.InRoom)
        {
            var e = _inventory[furniId];
            _buildWall = e.Wall; _buildPostit = e.Interaction == "postit"; _buildTilt = 0;
            _buildDir = (byte)(_buildWall ? 4 : 2);
            _ghost = FurniScene.Instantiate<FurniSprite>();
            _ghost.Bind(new ItemDto { Id = 0, FurniId = furniId, Name = e.Name, Dir = _buildDir, State = "", Wall = e.Wall, Interaction = e.Interaction, Author = Login });
            _ghost.SetGhost(true);
            _ghost.Visible = false;
            ObjectLayer.AddChild(_ghost);
            UpdateHover();
        }
        BuildModeChanged?.Invoke(_buildFurni, _buildDir);
    }

    /// <summary>R 키. 바닥 가구는 방향 회전, 벽걸이는 기울기(벽 방향은 가리키는 벽이 정한다).</summary>
    public void RotateBuild()
    {
        if (_buildFurni is null) return;
        if (_buildWall)
        {
            int i = Array.IndexOf(TiltSteps, _buildTilt);
            _buildTilt = TiltSteps[(i + 1) % TiltSteps.Length];
            _ghost?.SetTilt(_buildTilt);
        }
        else
        {
            _buildDir = (byte)((_buildDir + 2) % 8);
            _ghost?.SetDir(_buildDir);
        }
        UpdateHover();
        BuildModeChanged?.Invoke(_buildFurni, _buildDir);
    }

    /// <summary>현재 배치 모드의 기울기(도) — HUD 표시용.</summary>
    public sbyte BuildTilt => _buildTilt;

    // ---------------- 네트워크 ----------------
    private void OnPacket(Opcode op, ReadOnlyMemory<byte> body)
    {
        switch (op)
        {
            case Opcode.S_LoginResult:
                var lr = Framing.Deserialize<S_LoginResult>(body);
                if (!lr.Ok) { NetClient.Instance.Close(); SetPhase(Phase.Idle); Status?.Invoke($"로그인 거부: {lr.Reason}", true); return; }
                _myId = lr.UserId; HomeRoomId = lr.HomeRoomId; _loginNotice = lr.Notice;
                if (lr.Nick.Length > 0) Login = lr.Nick;      // 입장권으로 들어오면 서버가 정한 닉이 진짜다
                NetClient.Instance.Send(Opcode.C_EnterRoom, new C_EnterRoom { RoomId = lr.HomeRoomId });
                break;
            case Opcode.S_RoomSnapshot: LoadSnapshot(Framing.Deserialize<S_RoomSnapshot>(body)); break;
            case Opcode.S_UserEnter:
                var ue = Framing.Deserialize<S_UserEnter>(body).User;
                AddUser(ue); RoomChanged?.Invoke();
                ChatLine?.Invoke("", $"{ue.Nick}님이 들어왔어요", true);
                break;
            case Opcode.S_UserLeave:
                var ul = Framing.Deserialize<S_UserLeave>(body).UserId;
                if (_users.TryGetValue(ul, out var gone)) ChatLine?.Invoke("", $"{gone.Nick}님이 나갔어요", true);
                RemoveUser(ul); RoomChanged?.Invoke();
                break;
            case Opcode.S_UserPath:
                var up = Framing.Deserialize<S_UserPath>(body);
                if (up.Path.Count > 0 && up.UserId == _myId) _myTile = (up.Path[^1].X, up.Path[^1].Y);
                if (_users.TryGetValue(up.UserId, out var av)) av.SetPath(up.Path.Select(p => ((int)p.X, (int)p.Y)).ToList(), _map!);
                break;
            case Opcode.S_UserAction:
                var ua = Framing.Deserialize<S_UserAction>(body);
                if (_users.TryGetValue(ua.UserId, out var av2)) av2.SetAction(ua.Action, ua.Dir);
                break;
            case Opcode.S_UserFigure:
                var uf = Framing.Deserialize<S_UserFigure>(body);
                if (_users.TryGetValue(uf.UserId, out var av4)) av4.SetFigure(uf.Figure);
                if (uf.UserId == _myId) { MyFigure = uf.Figure; FigureChanged?.Invoke(); }
                break;
            case Opcode.S_ChatBubble:
                var cb = Framing.Deserialize<S_ChatBubble>(body);
                if (_users.TryGetValue(cb.UserId, out var av3)) { av3.ShowBubble(cb.Text); ChatLine?.Invoke(av3.Nick, cb.Text, false); }
                break;
            case Opcode.S_ItemAdd:
                var added = Framing.Deserialize<S_ItemAdd>(body).Item;
                AddItem(added); UpdateHover();
                // 내가 방금 붙인 빈 포스트잇 → 바로 쓰기 창
                if (added.Interaction == "postit" && added.Author == Login && string.IsNullOrEmpty(added.Extra) && _items.TryGetValue(added.Id, out var mine))
                    PostitOpened?.Invoke(mine);
                break;
            case Opcode.S_ItemRemove: RemoveItem(Framing.Deserialize<S_ItemRemove>(body).ItemId); UpdateHover(); break;
            case Opcode.S_ItemState:
                var st = Framing.Deserialize<S_ItemState>(body);
                if (_items.TryGetValue(st.ItemId, out var fs)) fs.SetState(st.State, st.Extra, st.Usable);
                break;
            case Opcode.S_WalletUpdate:
                Rupee = Framing.Deserialize<S_WalletUpdate>(body).Rupee;
                WalletChanged?.Invoke();
                break;
            case Opcode.S_Inventory:
                _inventory.Clear();
                foreach (var e in Framing.Deserialize<S_Inventory>(body).Items) _inventory[e.FurniId] = e;
                InventoryChanged?.Invoke();
                break;
            case Opcode.S_InventoryUpdate: OnInventoryUpdate(Framing.Deserialize<S_InventoryUpdate>(body)); break;
            case Opcode.S_Catalog:
                _catalog.Clear(); _catalog.AddRange(Framing.Deserialize<S_Catalog>(body).Items);
                CatalogChanged?.Invoke();
                break;
            case Opcode.S_RoomList:
                _roomList.Clear(); _roomList.AddRange(Framing.Deserialize<S_RoomList>(body).Rooms);
                RoomListChanged?.Invoke();
                break;
            case Opcode.S_HouseList:
                _houses.Clear(); _houses.AddRange(Framing.Deserialize<S_HouseList>(body).Houses);
                HouseListChanged?.Invoke();
                break;
            case Opcode.S_LotteryResult:
                var lot = Framing.Deserialize<S_LotteryResult>(body);
                LotteryResult?.Invoke(lot);
                ChatLine?.Invoke("", $"복권 — {lot.Message}", true);
                break;
            case Opcode.S_Notice:
                var nt = Framing.Deserialize<S_Notice>(body).Text;
                if (nt.Length > 0) { Status?.Invoke(nt, false); ChatLine?.Invoke("", nt, true); }
                break;
            case Opcode.S_Error:
                var err = Framing.Deserialize<S_Error>(body);
                GD.PrintErr($"S_Error {err.Code}: {err.Message}");
                Status?.Invoke(Humanize(err), true);
                break;
        }
    }

    private void OnInventoryUpdate(S_InventoryUpdate u)
    {
        int before = _inventory.TryGetValue(u.FurniId, out var old) ? old.Qty : 0;
        if (u.Qty <= 0) _inventory.Remove(u.FurniId);
        else _inventory[u.FurniId] = new InventoryEntry
        {
            FurniId = u.FurniId, Name = u.Name, Qty = u.Qty, Wall = u.Wall, Interaction = u.Interaction,
            SellPrice = u.SellPrice, SellBundleQty = u.SellBundleQty, SellBundlePrice = u.SellBundlePrice,
        };
        InventoryChanged?.Invoke();
        if (u.Qty > before) Status?.Invoke($"{u.Name} +{u.Qty - before}  →  가방에 {u.Qty}개", false);
        if (_buildFurni == u.FurniId && u.Qty <= 0) { SetBuildMode(null); Status?.Invoke($"{u.Name}을(를) 다 놓았어요", false); }
    }

    private static string Humanize(S_Error e) => e.Code switch
    {
        10 => "방이 가득 찼어요",
        20 => "알 수 없는 가구예요",
        21 => "그 자리엔 놓을 수 없어요 (막혀 있거나 이미 가구가 있어요)",
        22 => "이 방의 주인만 꾸밀 수 있어요",
        23 => "벽걸이 가구는 벽이 있는 자리에만 붙일 수 있어요",
        24 => "가방에 없는 가구예요",
        25 => "내가 붙인 포스트잇에만 쓸 수 있어요",
        30 => "루피가 부족해요",
        31 => "상점에서 파는 가구가 아니에요",
        32 => "팔 수 없는 물건이에요",
        33 => "이 방은 더 넓힐 수 없어요",
        34 => "내 방에서만 넓히거나 이사할 수 있어요",
        35 => "이미 그 집에 살고 있어요",
        1 or 2 => $"서버 오류: {e.Message}",
        _ => e.Message,
    };

    private void OnDisconnected(string reason)
    {
        if (State == Phase.Idle) return;
        ClearRoom();
        SetPhase(Phase.Idle);
        Status?.Invoke($"서버와 연결이 끊겼어요. ({reason})\n서버를 확인하고 다시 입장해 주세요.", true);
    }

    private void SetPhase(Phase p)
    {
        if (State == p) return;
        State = p; PhaseChanged?.Invoke(p);
    }

    // ---------------- 룸 구성 ----------------
    private void ClearRoom()
    {
        SetBuildMode(null);
        foreach (var c in _wallLayer.GetChildren()) c.QueueFree();
        foreach (var c in FloorLayer.GetChildren()) c.QueueFree();
        foreach (var c in ObjectLayer.GetChildren()) c.QueueFree();
        _items.Clear(); _users.Clear(); _ghost = null; _pendingUse = 0; _door = null; _doorTile = (-1, -1);
        _map = null; _walls = null; _wallSeg.Clear(); _hoverSlot = null; _cursor.Visible = false; _cursor.SetShape(null);
    }

    private void LoadSnapshot(S_RoomSnapshot s)
    {
        ClearRoom();
        _map = new Heightmap(s.Room.Heightmap);
        _walls = new Walls(_map);
        RoomId = s.Room.Id; RoomName = s.Room.Name; RoomOwnerNick = s.Room.OwnerNick; RoomKind = s.Room.Kind;
        UpgradePrice = s.Room.UpgradePrice; UpgradeName = s.Room.UpgradeName;
        RoomWidth = s.Room.Width; RoomHeight = s.Room.Height;
        // 방을 넓히면 방 id 가 바뀐다 — 로그인 때 받은 HomeRoomId 는 그 순간 옛것이 되므로 여기서 다시 잡는다.
        if (RoomKind != "public" && string.Equals(RoomOwnerNick, Login, StringComparison.OrdinalIgnoreCase)) HomeRoomId = RoomId;
        _doorTile = (s.Room.DoorX, s.Room.DoorY);
        BuildFloor(s.Room);
        foreach (var it in s.Items) AddItem(it);
        foreach (var u in s.Users) AddUser(u);
        SetPhase(Phase.InRoom);
        if (OS.GetEnvironment("HARBOR_SELFTEST") == "1") RunSelfTest();
        RoomChanged?.Invoke();
        if (_loginNotice.Length > 0) { ChatLine?.Invoke("", _loginNotice, true); Status?.Invoke(_loginNotice, false); _loginNotice = ""; }
        ChatLine?.Invoke("", IsMyRoom
            ? $"내 방 '{RoomName}'에 입장했어요. 가방의 가구로 꾸며 보세요."
            : RoomKind == "public" ? $"'{RoomName}'에 입장했어요. 공용 공간이라 누구나 가구를 놓을 수 있어요."
            : $"{RoomOwnerNick}님의 방 '{RoomName}'에 놀러 왔어요. 구경만 할 수 있어요.", true);
    }

    /// <summary>벽(뒤쪽 두 면) + 바닥 타일 + 단차/받침(riser). 뒤(x+y 작음)부터 그려 앞 타일이 뒤 riser 를 자연스럽게 가린다.</summary>
    private void BuildFloor(RoomDto room)
    {
        var tiles = new List<(int x, int y)>();
        for (int y = 0; y < _map!.H; y++) for (int x = 0; x < _map.W; x++) if (_map.Walkable(x, y)) tiles.Add((x, y));
        tiles.Sort((a, b) => (a.x + a.y).CompareTo(b.x + b.y) != 0 ? (a.x + a.y).CompareTo(b.x + b.y) : a.x.CompareTo(b.x));

        var (westC, northC) = WallColors(room.WallStyle);
        float wh = room.WallHeight * WallUnitPx;
        _wallH = wh; _wallRows = Math.Max(1, (int)room.WallHeight);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var (x, y) in tiles)
        {
            float h = _map.Height(x, y);
            var (sx, sy) = Iso.ToScreen(x, y, h);
            var top = new Vector2(sx, sy);
            var left = new Vector2(sx - Iso.TileW / 2f, sy + Iso.TileH / 2f);
            var right = new Vector2(sx + Iso.TileW / 2f, sy + Iso.TileH / 2f);
            var bottom = new Vector2(sx, sy + Iso.TileH);

            if (wh > 0 && _walls!.West(x, y)) { AddWall(left, top, wh, westC); _wallSeg[(x, y, 2)] = (left, top); }
            if (wh > 0 && _walls!.North(x, y)) { AddWall(top, right, wh, northC); _wallSeg[(x, y, 4)] = (top, right); }
            AddRiser((x, y + 1), h, left, bottom, RiserLeft);    // 왼쪽 아래 면
            AddRiser((x + 1, y), h, bottom, right, RiserRight);  // 오른쪽 아래 면

            var spr = new Sprite2D { Texture = FloorTile, Position = top, Centered = false, Offset = new Vector2(-Iso.TileW / 2f, 0) };
            bool door = x == room.DoorX && y == room.DoorY;
            spr.Modulate = door ? new Color(1f, 0.93f, 0.7f) : ((x + y) & 1) == 0 ? Colors.White : new Color(0.93f, 0.94f, 0.98f);
            FloorLayer.AddChild(spr);

            minX = Mathf.Min(minX, left.X); maxX = Mathf.Max(maxX, right.X);
            minY = Mathf.Min(minY, sy - wh); maxY = Mathf.Max(maxY, bottom.Y);
        }
        // 문: 다른 방으로 통하는 자립형 문 (누르면 방 목록)
        if (_map.Walkable(_doorTile.x, _doorTile.y))
        {
            var (dx, dy) = Iso.ToScreen(_doorTile.x + 0.5f, _doorTile.y + 0.5f, _map.Height(_doorTile.x, _doorTile.y));
            _door = new DoorMarker { Name = "Door", Position = new Vector2(dx, dy) };
            ObjectLayer.AddChild(_door);
        }

        // 카메라: 방(벽 포함) 중앙, 하단 HUD 만큼 살짝 아래를 본다
        _cam.Position = new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f + 20);
    }

    private static (Color west, Color north) WallColors(string style)
        => style.Contains("wood") ? (new Color("6e4a34"), new Color("8a5f42"))
         : style.Contains("rail") ? (new Color("4a5a74"), new Color("5e7090"))
         : (new Color("7a6e80"), new Color("948898"));

    /// <summary>모서리 a→b 위로 wh 만큼 올라가는 벽 면 + 윗선 + 걸레받이.</summary>
    private void AddWall(Vector2 a, Vector2 b, float wh, Color color)
    {
        var up = new Vector2(0, wh);
        _wallLayer.AddChild(new Polygon2D { Polygon = new[] { a, b, b - up, a - up }, Color = color });
        _wallLayer.AddChild(new Polygon2D { Polygon = new[] { a, b, b - new Vector2(0, 4), a - new Vector2(0, 4) }, Color = Ui.Darken(color, 0.35f) });   // 걸레받이
        _wallLayer.AddChild(new Line2D { Points = new[] { a - up, b - up }, Width = 1.5f, DefaultColor = Ui.Lighten(color, 0.25f) });
    }

    private void AddRiser((int x, int y) neighbor, float h, Vector2 a, Vector2 b, Color color)
    {
        bool nWalk = _map!.Walkable(neighbor.x, neighbor.y);
        float drop = (h - (nWalk ? _map.Height(neighbor.x, neighbor.y) : 0f)) * Iso.StackPx + (nWalk ? 0f : 6f);   // 바깥 가장자리는 디오라마 받침
        if (drop <= 0f) return;
        var d = new Vector2(0, drop);
        FloorLayer.AddChild(new Polygon2D { Polygon = new[] { a, b, b + d, a + d }, Color = color });
    }

    /// <summary>
    /// 헤드리스 저장 검증용(`HARBOR_SELFTEST=1`). 입장 후 바닥에 의자, 벽에 포스트잇을 놓고 메모까지 쓴다.
    /// 사람 손 없이 "배치 → 저장 → 재시작 → 복원" 경로를 실제로 통과시키기 위한 것.
    /// </summary>
    private async void RunSelfTest()
    {
        await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        if (State != Phase.InRoom || _map is null) return;

        bool placedFloor = false;
        for (int y = 0; y < _map.H && !placedFloor; y++)
            for (int x = 0; x < _map.W && !placedFloor; x++)
                if (CanPlaceAt(x, y))
                {
                    NetClient.Instance.Send(Opcode.C_PlaceItem, new C_PlaceItem { FurniId = "chair_wood", X = (short)x, Y = (short)y, Dir = 2 });
                    placedFloor = true;
                }

        bool placedWall = false;
        foreach (var key in _wallSeg.Keys)
        {
            NetClient.Instance.Send(Opcode.C_PlaceItem, new C_PlaceItem { FurniId = "postit_yellow", X = (short)key.x, Y = (short)key.y, Dir = (byte)key.dir, WallU = 0, WallV = 0, Tilt = -7 });
            placedWall = true;
            break;
        }
        GD.Print($"[SelfTest] 의자={placedFloor} 포스트잇={placedWall}");

        await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        if (State != Phase.InRoom) return;
        if (_items.Values.FirstOrDefault(i => i.IsPostit && i.Author == Login) is { } note)
        {
            WritePostit(note.ItemId, "저장 테스트 메모");
            GD.Print("[SelfTest] 포스트잇 메모 작성 요청");
        }
        else GD.Print("[SelfTest] 포스트잇을 찾지 못함");

        // 검사가 끝나면 스스로 닫는다. 창을 남겨 두면 사람이 만져서 검증 결과가 오염된다
        // (실제로 남아 있던 창에서 복권이 69번 눌려 루피가 빠져나간 적이 있다).
        await ToSignal(GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);
        GD.Print("[SelfTest] 완료 — 종료합니다");
        GetTree().Quit();
    }

    /// <summary>가구 화면 위치. 벽걸이는 벽 슬롯(타일 모서리를 SlotCols 등분 × 벽 높이 단위) 중심.</summary>
    private Vector2 ItemPos(int x, int y, float z, bool wall, byte dir, int u, int v)
    {
        if (wall && _wallSeg.TryGetValue((x, y, dir), out var seg)) return WallSlotCenter(seg, u, v);
        var (cx, cy) = Iso.ToScreen(x + 0.5f, y + 0.5f, z);
        if (wall) return new Vector2(cx, cy - Iso.TileH / 4f);   // 벽 정보가 없을 때의 폴백
        return new Vector2(cx, cy);
    }

    private Vector2 WallSlotCenter((Vector2 a, Vector2 b) seg, int u, int v)
        => seg.a + (seg.b - seg.a) * ((u + 0.5f) / Walls.SlotCols) + new Vector2(0, -(v + 0.5f) * WallUnitPx);

    private Vector2[] WallSlotShape((Vector2 a, Vector2 b) seg, int u, int v)
    {
        var d = (seg.b - seg.a) / Walls.SlotCols;
        var p0 = seg.a + d * u + new Vector2(0, -v * WallUnitPx);
        var up = new Vector2(0, -WallUnitPx);
        return new[] { p0, p0 + d, p0 + d + up, p0 + up };
    }

    /// <summary>
    /// 마우스(로컬 좌표) 아래 벽 슬롯. 벽 면 = 아랫변 a→b 를 위로 _wallH 만큼 올린 평행사변형.
    /// 겹치는 벽이 있으면 화면상 앞쪽(x+y 큰 타일)을 고른다 — 보이는 것이 곧 집히는 것.
    /// </summary>
    private (int x, int y, int dir, int u, int v)? WallSlotAt(Vector2 p)
    {
        if (_wallH <= 0) return null;
        (int x, int y, int dir, int u, int v)? best = null;
        int bestDepth = int.MinValue;
        foreach (var (key, seg) in _wallSeg)
        {
            var d = seg.b - seg.a;
            float s = (p.X - seg.a.X) / d.X;                        // d.X = 32 (모서리는 항상 가로 32px)
            if (s < 0 || s >= 1) continue;
            float t = (seg.a.Y + s * d.Y - p.Y) / _wallH;            // 0 = 바닥, 1 = 벽 꼭대기
            if (t < 0 || t >= 1) continue;
            int depth = key.x + key.y;
            if (depth <= bestDepth) continue;
            bestDepth = depth;
            best = (key.x, key.y, key.dir, (int)(s * Walls.SlotCols), (int)(t * _wallRows));
        }
        return best;
    }

    private FurniSprite? WallItemAtSlot((int x, int y, int dir, int u, int v) s)
        => _items.Values.FirstOrDefault(i => i.Wall && i.Tile == (s.x, s.y) && i.Dir == s.dir && i.WallU == s.u && i.WallV == s.v);

    private FurniSprite? WallItemAtMouse()
        => WallSlotAt(FloorLayer.ToLocal(GetGlobalMousePosition())) is { } s ? WallItemAtSlot(s) : null;

    private bool CanPlaceWall((int x, int y, int dir, int u, int v) s)
        => (CanEdit || _buildPostit) && _walls!.CanHang(s.x, s.y, s.dir) && Walls.SlotValid(s.u, s.v, _wallRows) && WallItemAtSlot(s) is null;

    private void AddItem(ItemDto it)
    {
        var node = FurniScene.Instantiate<FurniSprite>();
        node.Bind(it);
        node.Position = ItemPos(it.X, it.Y, it.Z, it.Wall, it.Dir, it.WallU, it.WallV);
        ObjectLayer.AddChild(node); _items[it.Id] = node;
    }
    private void RemoveItem(long id) { if (_items.Remove(id, out var n)) n.QueueFree(); }

    private void AddUser(UserDto u)
    {
        if (_users.Remove(u.Id, out var old)) old.QueueFree();
        var node = AvatarScene.Instantiate<AvatarView>();
        node.Bind(u);
        var (sx, sy) = Iso.ToScreen(u.X + 0.5f, u.Y + 0.5f, u.Z);
        node.Position = new Vector2(sx, sy);
        if (u.Id == _myId)
        {
            node.MarkMe(); node.Arrived += OnMyArrival; _myTile = (u.X, u.Y);
            MyFigure = Figure.Sanitize(u.Figure); FigureChanged?.Invoke();
        }
        ObjectLayer.AddChild(node); _users[u.Id] = node;
    }
    private void RemoveUser(long id) { if (_users.Remove(id, out var n)) n.QueueFree(); }

    private void OnMyArrival()
    {
        if (_pendingUse == 0) return;
        long id = _pendingUse; _pendingUse = 0;
        if (_items.TryGetValue(id, out var it) && Cheb(_myTile, it.Tile) <= 1)
            NetClient.Instance.Send(Opcode.C_UseItem, new C_UseItem { ItemId = id });
    }

    // ---------------- 입력 ----------------
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey k && k.Pressed && !k.Echo && _buildFurni is not null)
        {
            if (k.Keycode == Key.Escape) { SetBuildMode(null); GetViewport().SetInputAsHandled(); return; }
            if (k.Keycode == Key.R) { RotateBuild(); GetViewport().SetInputAsHandled(); return; }
        }
        if (_map is null || State != Phase.InRoom) return;

        if (e is InputEventMouseButton mb)
        {
            switch (mb.ButtonIndex)
            {
                case MouseButton.WheelUp when mb.Pressed: Zoom(+1); return;
                case MouseButton.WheelDown when mb.Pressed: Zoom(-1); return;
                case MouseButton.Middle: _dragging = mb.Pressed; return;
            }
            if (!mb.Pressed) return;
            var (x, y) = MouseTile();
            if (mb.ButtonIndex == MouseButton.Left) OnLeftClick(x, y);
            else if (mb.ButtonIndex == MouseButton.Right) OnRightClick(x, y);
        }
        else if (e is InputEventMouseMotion mm)
        {
            if (_dragging) _cam.Position -= mm.Relative / _cam.Zoom;
            UpdateHover();
        }
    }

    private void OnLeftClick(int x, int y)
    {
        if (_buildFurni is not null)
        {
            if (_buildWall)
            {
                if (_hoverSlot is { } s && CanPlaceWall(s))
                    NetClient.Instance.Send(Opcode.C_PlaceItem, new C_PlaceItem { FurniId = _buildFurni, X = (short)s.x, Y = (short)s.y, Dir = (byte)s.dir, WallU = (byte)s.u, WallV = (byte)s.v, Tilt = _buildTilt });
                else Status?.Invoke("벽면의 빈 칸을 가리키고 클릭하세요", true);
                return;
            }
            if (CanPlaceAt(x, y)) NetClient.Instance.Send(Opcode.C_PlaceItem, new C_PlaceItem { FurniId = _buildFurni, X = (short)x, Y = (short)y, Dir = _buildDir });
            else Status?.Invoke("여기엔 놓을 수 없어요", true);
            return;
        }
        _pendingUse = 0;
        var hit = WallItemAtMouse() ?? FloorItemAt(x, y);
        if (hit is not null && hit.IsPostit) { PostitOpened?.Invoke(hit); return; }     // 읽기는 어디서든
        if (hit is not null && hit.Interaction == "fsm") { UseOrApproach(hit); return; }
        if ((x, y) == _doorTile) { DoorClicked?.Invoke(); return; }                      // 문 = 다른 방으로
        if (_map!.Walkable(x, y) && !(hit?.Solid ?? false))
            NetClient.Instance.Send(Opcode.C_Move, new C_Move { X = (short)x, Y = (short)y });
    }

    private void OnRightClick(int x, int y)
    {
        if (_buildFurni is not null) { SetBuildMode(null); return; }
        var hit = WallItemAtMouse() ?? FloorItemAt(x, y);
        if (hit is null) return;
        if (!CanRemove(hit)) { Status?.Invoke($"이 방은 {RoomOwnerNick}님의 방이라 가구를 주울 수 없어요", true); return; }
        PickItem(hit.ItemId);
    }

    /// <summary>인접하면 바로 사용, 멀면 가장 가까운 빈 인접 타일로 걸어간 뒤 사용.</summary>
    private void UseOrApproach(FurniSprite hit)
    {
        if (!hit.Usable)   // 자라는 중인 식물처럼 지금은 할 게 없는 상태 — 헛클릭 대신 상태를 알려준다
        {
            var label = FurniPalette.StateLabel(hit.State);
            Status?.Invoke(label.Length > 0 ? $"{hit.DisplayName} — {label}" : $"{hit.DisplayName}는 지금 할 수 있는 게 없어요", false);
            return;
        }
        if (Cheb(_myTile, hit.Tile) <= 1) { NetClient.Instance.Send(Opcode.C_UseItem, new C_UseItem { ItemId = hit.ItemId }); return; }
        (int x, int y)? best = null; int bestD = int.MaxValue;
        for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            var t = (hit.Tile.x + dx, hit.Tile.y + dy);
            if (!_map!.Walkable(t.Item1, t.Item2) || (FloorItemAt(t.Item1, t.Item2)?.Solid ?? false)) continue;
            int d = Cheb(_myTile, t) * 10 + Math.Abs(_myTile.x - t.Item1) + Math.Abs(_myTile.y - t.Item2);
            if (d < bestD) { bestD = d; best = t; }
        }
        if (best is null) { Status?.Invoke("가구에 다가갈 수 없어요", true); return; }
        _pendingUse = hit.ItemId;
        NetClient.Instance.Send(Opcode.C_Move, new C_Move { X = (short)best.Value.x, Y = (short)best.Value.y });
    }

    private void UpdateHover()
    {
        if (_map is null || State != Phase.InRoom) { _cursor.Visible = false; return; }
        var local = FloorLayer.ToLocal(GetGlobalMousePosition());

        // 벽걸이 배치 모드: 바닥이 아니라 벽면 슬롯을 가리킨다 (벽 방향은 자동)
        if (_buildFurni is not null && _buildWall)
        {
            _hoverSlot = WallSlotAt(local);
            if (_hoverSlot is not { } s) { _cursor.Visible = false; if (_ghost is not null) _ghost.Visible = false; return; }
            var seg = _wallSeg[(s.x, s.y, s.dir)];
            bool ok = CanPlaceWall(s);
            _cursor.Position = Vector2.Zero; _cursor.SetShape(WallSlotShape(seg, s.u, s.v)); _cursor.SetColor(ok ? Ui.Leaf : Ui.Danger); _cursor.Visible = true;
            if (_ghost is not null)
            {
                if (_buildDir != s.dir) { _buildDir = (byte)s.dir; _ghost.SetDir(_buildDir); BuildModeChanged?.Invoke(_buildFurni, _buildDir); }
                _ghost.Visible = true; _ghost.Position = WallSlotCenter(seg, s.u, s.v); _ghost.SetValid(ok);
            }
            return;
        }

        // 일반 모드: 벽걸이 가구 위면 그 슬롯을 강조
        if (_buildFurni is null && WallSlotAt(local) is { } ws && WallItemAtSlot(ws) is not null)
        {
            _cursor.Position = Vector2.Zero; _cursor.SetShape(WallSlotShape(_wallSeg[(ws.x, ws.y, ws.dir)], ws.u, ws.v)); _cursor.SetColor(Ui.Accent); _cursor.Visible = true;
            return;
        }

        _cursor.SetShape(null);
        var (x, y) = MouseTile();
        bool walk = _map.Walkable(x, y);
        _cursor.Visible = walk;
        if (!walk) { if (_ghost is not null) _ghost.Visible = false; return; }
        var (sx, sy) = Iso.ToScreen(x, y, _map.Height(x, y));
        _cursor.Position = new Vector2(sx, sy);
        if (_buildFurni is not null)
        {
            bool ok = CanPlaceAt(x, y);
            _cursor.SetColor(ok ? Ui.Leaf : Ui.Danger);
            if (_ghost is not null)
            {
                _ghost.Visible = true;
                _ghost.Position = ItemPos(x, y, _map.Height(x, y), false, _buildDir, 0, 0);
                _ghost.SetValid(ok);
            }
        }
        else
        {
            bool onDoor = (x, y) == _doorTile;
            _door?.SetHover(onDoor);
            _cursor.SetColor(onDoor || FloorItemAt(x, y) is { Interaction: "fsm" or "postit" } ? Ui.Accent : Ui.AccentSoft);
        }
    }

    private void Zoom(int delta)
    {
        _zoomIdx = Math.Clamp(_zoomIdx + delta, 0, ZoomSteps.Length - 1);
        _cam.Zoom = Vector2.One * ZoomSteps[_zoomIdx];
    }

    // ---------------- 헬퍼 ----------------
    /// <summary>마우스 아래 타일. 높은 타일은 화면상 위로 올라가 있으므로 높이 후보를 위에서부터 검사.</summary>
    private (int x, int y) MouseTile()
    {
        var local = FloorLayer.ToLocal(GetGlobalMousePosition());
        for (int h = 4; h >= 1; h--)
        {
            var (x, y) = Iso.ToWorld(local.X, local.Y + h * Iso.StackPx);
            if (_map!.Walkable(x, y) && (int)_map.Height(x, y) == h) return (x, y);
        }
        return Iso.ToWorld(local.X, local.Y);
    }

    private FurniSprite? FloorItemAt(int x, int y) => _items.Values.FirstOrDefault(i => !i.Wall && i.Tile == (x, y));

    /// <summary>바닥 가구 배치 가능 여부 (벽걸이는 CanPlaceWall).</summary>
    private bool CanPlaceAt(int x, int y)
    {
        if (!_map!.Walkable(x, y) || !CanEdit) return false;
        return FloorItemAt(x, y) is null && !_users.Values.Any(u => u.Tile == (x, y));
    }

    private static int Cheb((int x, int y) a, (int x, int y) b) => Math.Max(Math.Abs(a.x - b.x), Math.Abs(a.y - b.y));
}
