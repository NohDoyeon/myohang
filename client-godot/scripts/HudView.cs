using Godot;
using Harbor.Core;
using Harbor.Protocol;

namespace HarborClient;

/// <summary>
/// 화면 고정 UI(CanvasLayer). 부모 RoomView 의 이벤트만 구독하고, 조작은 RoomView 공개 API 로 되돌린다.
/// 구성: 시작 패널 · 상단(방 이름·인원, 배치 모드 배지, 힌트) · 우상단(지갑) · 하단(채팅 로그·입력, 감정표현, 가방/상점/방 목록) · 창(가방/상점/방) · 토스트
/// </summary>
public partial class HudView : CanvasLayer
{
    private enum Win { None, Bag, Shop, Rooms, Postit, Lottery, Figure }

    private RoomView _room = null!;

    private Control _start = null!; private HangulInput _nick = null!; private HangulInput _pass = null!;
    private Label _startStatus = null!; private Button _joinBtn = null!; private Label _serverLabel = null!;
    private Control _top = null!; private Label _roomLabel = null!; private PanelContainer _modeCard = null!; private Label _modeLabel = null!;
    private Control _wallet = null!; private Label _walletLabel = null!;
    private Control _bottom = null!; private HangulInput _chat = null!; private RichTextLabel _log = null!;
    private Button _bagBtn = null!, _shopBtn = null!, _roomsBtn = null!, _lottoBtn = null!, _figureBtn = null!, _cancelBuild = null!;
    private readonly List<string> _lottoLog = new();
    private long _lottoPrice = 10;   // 서버 결과(S_LotteryResult.Price)로 갱신됨
    private PanelContainer _win = null!; private Label _winTitle = null!; private VBoxContainer _winBody = null!; private Win _open = Win.None;
    private bool _renderQueued;
    private PanelContainer _toast = null!; private Label _toastText = null!; private int _toastSeq;
    private FurniSprite? _postit;

    public override void _Ready()
    {
        _room = GetParent<RoomView>();
        Layer = 10;
        BuildStart(); BuildTop(); BuildWallet(); BuildBottom(); BuildWindow(); BuildToast();

        _room.PostitOpened += OpenPostit;
        _room.DoorClicked += () => { if (_open != Win.Rooms) ToggleWindow(Win.Rooms); };
        _room.LotteryResult += OnLotteryResult;
        _room.FigureChanged += () => { if (_open == Win.Figure) QueueRender(); };

        _room.PhaseChanged += OnPhase;
        _room.Status += OnStatus;
        _room.RoomChanged += OnRoomChanged;
        _room.ChatLine += OnChatLine;
        _room.BuildModeChanged += OnBuildMode;
        _room.WalletChanged += () => { _walletLabel.Text = $"루피 {_room.Rupee:N0}"; if (_open is Win.Shop or Win.Lottery or Win.Rooms) QueueRender(); };
        _room.InventoryChanged += () => { if (_open == Win.Bag) QueueRender(); };
        _room.CatalogChanged += () => { if (_open == Win.Shop) QueueRender(); };
        _room.RoomListChanged += () => { if (_open == Win.Rooms) QueueRender(); };
        _room.HouseListChanged += () => { if (_open == Win.Rooms) QueueRender(); };
        OnPhase(_room.State);
    }

    // ---------------- 시작 패널 ----------------
    private void BuildStart()
    {
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 10);
        v.AddChild(Ui.Text("묘항", 46, Ui.Accent, center: true));
        v.AddChild(Ui.Text("고양이들이 사는 항구 마을", 15, Ui.Muted, center: true));
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        _nick = new HangulInput { Placeholder = "닉네임 (16자까지)", MaxLength = 16, CustomMinimumSize = new Vector2(320, 36), Korean = false };
        _nick.Text = _room.Login;
        _nick.Submitted += _ => _pass.GrabFocus();
        v.AddChild(_nick);

        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
        _pass = new HangulInput { Placeholder = "비밀번호 (4자 이상)", MaxLength = 64, CustomMinimumSize = new Vector2(230, 36), Korean = false, Secret = true };
        _pass.Submitted += _ => Join();
        _joinBtn = Ui.Button("입장하기", primary: true); _joinBtn.Pressed += Join;
        row.AddChild(_pass); row.AddChild(_joinBtn);
        v.AddChild(row);

        _startStatus = Ui.Text("", 13, Ui.Muted, center: true);
        _startStatus.AutowrapMode = TextServer.AutowrapMode.Word;
        _startStatus.CustomMinimumSize = new Vector2(340, 0);
        v.AddChild(_startStatus);
        _serverLabel = Ui.Text("", 12, Ui.Muted, center: true);
        UpdateServerLabel();
        v.AddChild(_serverLabel);
        v.AddChild(Ui.Text("같은 닉네임·비밀번호로 다시 들어오면 내 방과 루피가 그대로 있어요", 12, Ui.Muted, center: true));
        v.AddChild(Ui.Text("웹에서 받은 입장권 코드는 비밀번호 칸에 붙여넣으세요", 12, Ui.Muted, center: true));

        center.AddChild(Ui.Card(v, Ui.Panel, Ui.Line, 14, 26));
        _start = center;
    }

    private async void Join()
    {
        var nick = _nick.Text.Trim();
        var pass = _pass.Text.Trim();
        // 웹에서 받은 입장권을 비밀번호 칸에 붙여넣은 경우 — 닉도 서버 주소도 그 코드가 정한다
        bool looksLikeTicket = RoomView.LooksLikeTicket(pass);

        if (!looksLikeTicket)
        {
            if (nick.Length == 0) { SetStartStatus("닉네임을 입력해 주세요", true); _nick.GrabFocus(); return; }
            if (pass.Length < 4) { SetStartStatus("비밀번호를 4자 이상 입력해 주세요", true); _pass.GrabFocus(); return; }
        }
        _joinBtn.Disabled = true;
        await _room.Join(looksLikeTicket ? "" : nick, pass);
        _joinBtn.Disabled = false;
    }

    /// <summary>붙여넣은 입장권에 다른 주소가 실려 있으면 접속 대상이 바뀐다 — 화면에도 그대로 보여준다.</summary>
    private void UpdateServerLabel()
        => _serverLabel.Text = $"서버 {_room.Host}:{_room.Port}  ·  처음 쓰는 닉네임이면 그대로 계정이 만들어져요";

    private void SetStartStatus(string msg, bool err)
    {
        _startStatus.Text = msg;
        _startStatus.AddThemeColorOverride("font_color", err ? Ui.Danger : Ui.Muted);
    }

    // ---------------- 상단 ----------------
    private void BuildTop()
    {
        var h = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        h.AddThemeConstantOverride("separation", 8);
        h.OffsetLeft = 12; h.OffsetTop = 12;
        AddChild(h);

        _roomLabel = Ui.Text("", 15, Ui.Paper);
        h.AddChild(Ui.Card(_roomLabel, Ui.Panel, null, 8, 8));

        _modeLabel = Ui.Text("", 13, Ui.Ink);
        _modeCard = Ui.Card(_modeLabel, Ui.AccentSoft, null, 8, 8);
        _modeCard.Visible = false;
        h.AddChild(_modeCard);

        h.AddChild(Ui.Card(Ui.Text("좌클릭 이동 · 가구 클릭 사용 · 문 클릭 다른 방 · 우클릭 줍기 · 휠 줌 · Enter 채팅", 12, Ui.Muted), Ui.PanelSoft, null, 8, 8));
        _top = h;
    }

    private void OnRoomChanged()
    {
        string who = _room.IsMyRoom ? "내 방" : _room.RoomKind == "public" ? "공용" : $"{_room.RoomOwnerNick}님의 방";
        _roomLabel.Text = $"{_room.RoomName}  ·  {who}  ·  {_room.UserCount}명";
        if (_open != Win.None) RenderWindow();
    }

    private void BuildWallet()
    {
        _walletLabel = Ui.Text("루피 0", 15, Ui.AccentSoft);
        var card = Ui.Card(_walletLabel, Ui.Panel, null, 8, 8);
        card.AnchorLeft = 1; card.AnchorRight = 1; card.AnchorTop = 0; card.AnchorBottom = 0;
        card.OffsetRight = -12; card.OffsetTop = 12; card.GrowHorizontal = Control.GrowDirection.Begin;
        AddChild(card);
        _wallet = card;
    }

    // ---------------- 하단 ----------------
    private void BuildBottom()
    {
        var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, GrowVertical = Control.GrowDirection.Begin };
        v.AddThemeConstantOverride("separation", 6);
        v.AnchorLeft = 0; v.AnchorRight = 1; v.AnchorTop = 1; v.AnchorBottom = 1;
        v.OffsetLeft = 12; v.OffsetRight = -12; v.OffsetTop = -12; v.OffsetBottom = -12;
        AddChild(v);

        _log = new RichTextLabel
        {
            BbcodeEnabled = true, ScrollFollowing = true, ScrollActive = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(440, 118), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
        };
        _log.AddThemeStyleboxOverride("normal", Ui.Box(Ui.PanelSoft, null, 8, 8));
        _log.AddThemeFontSizeOverride("normal_font_size", 13);
        _log.AddThemeColorOverride("default_color", Ui.Paper);
        v.AddChild(_log);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        v.AddChild(row);

        _chat = new HangulInput { Placeholder = "메시지를 입력하고 Enter (한/영: 오른쪽 Alt)", MaxLength = 120, CustomMinimumSize = new Vector2(0, 36) };
        _chat.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _chat.Submitted += OnChatSubmit;
        row.AddChild(_chat);

        row.AddChild(new VSeparator());
        foreach (var (label, action) in new[] { ("춤", "dance"), ("손인사", "wave"), ("웃음", "laugh"), ("잠", "sleep"), ("그만", "stand") })
        {
            var b = Ui.Button(label); string a = action;
            b.Pressed += () => _room.DoAction(a);
            row.AddChild(b);
        }

        row.AddChild(new VSeparator());
        _bagBtn = Ui.Button("가방"); _bagBtn.ToggleMode = true; _bagBtn.Pressed += () => ToggleWindow(Win.Bag);
        _shopBtn = Ui.Button("상점"); _shopBtn.ToggleMode = true; _shopBtn.Pressed += () => ToggleWindow(Win.Shop);
        _roomsBtn = Ui.Button("방 목록"); _roomsBtn.ToggleMode = true; _roomsBtn.Pressed += () => ToggleWindow(Win.Rooms);
        _lottoBtn = Ui.Button("복권"); _lottoBtn.ToggleMode = true; _lottoBtn.Pressed += () => ToggleWindow(Win.Lottery);
        _figureBtn = Ui.Button("외모"); _figureBtn.ToggleMode = true; _figureBtn.Pressed += () => ToggleWindow(Win.Figure);
        row.AddChild(_bagBtn); row.AddChild(_shopBtn); row.AddChild(_roomsBtn); row.AddChild(_lottoBtn); row.AddChild(_figureBtn);
        _cancelBuild = Ui.Button("배치 끝"); _cancelBuild.Visible = false;
        _cancelBuild.Pressed += () => _room.SetBuildMode(null);
        row.AddChild(_cancelBuild);

        _bottom = v;
    }

    private void OnChatSubmit(string text)
    {
        text = text.Trim();
        if (text.Length > 0) _room.SendChat(text);
        _chat.Clear();
        _chat.ReleaseFocus();
    }

    private void OnChatLine(string who, string text, bool system)
    {
        string t = Escape(text);
        _log.AppendText(system
            ? $"[color=#{Ui.Muted.ToHtml(false)}]{t}[/color]\n"
            : $"[color=#{Ui.Accent.ToHtml(false)}]{Escape(who)}[/color]  {t}\n");
    }
    private static string Escape(string s) => s.Replace("[", "[lb]");

    private void OnBuildMode(string? furniId, byte dir)
    {
        _cancelBuild.Visible = furniId is not null;
        _modeCard.Visible = furniId is not null;
        if (furniId is not null)
        {
            bool wall = _room.Inventory.TryGetValue(furniId, out var e) && e.Wall;
            string name = e?.Name ?? furniId;
            _modeLabel.Text = wall
                ? $"배치 중: {name}  ·  기울기 {_room.BuildTilt}°   (벽면의 칸을 가리키고 좌클릭 · R 기울이기 · 우클릭/ESC 끝)"
                : $"배치 중: {name}  ·  방향 {dir}   (좌클릭 놓기 · R 회전 · 우클릭/ESC 끝)";
        }
        if (_open == Win.Bag) RenderWindow();
    }

    // ---------------- 창 (가방 / 상점 / 방 목록) ----------------
    private void BuildWindow()
    {
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 8);
        var head = new HBoxContainer();
        _winTitle = Ui.Text("", 17, Ui.Accent); _winTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var close = Ui.Button("닫기"); close.Pressed += () => ToggleWindow(Win.None);
        head.AddChild(_winTitle); head.AddChild(close);
        v.AddChild(head);
        v.AddChild(new HSeparator());

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(420, 300), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _winBody = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _winBody.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_winBody);
        v.AddChild(scroll);

        _win = Ui.Card(v, Ui.Panel, Ui.Line, 12, 14);
        _win.Visible = false;
        _win.AnchorLeft = 1; _win.AnchorRight = 1; _win.AnchorTop = 0.5f; _win.AnchorBottom = 0.5f;
        _win.OffsetRight = -12; _win.GrowHorizontal = Control.GrowDirection.Begin; _win.GrowVertical = Control.GrowDirection.Both;
        AddChild(_win);
    }

    private void ToggleWindow(Win w)
    {
        _open = (w == Win.None || _open == w) ? Win.None : w;
        _bagBtn.ButtonPressed = _open == Win.Bag; _shopBtn.ButtonPressed = _open == Win.Shop;
        _roomsBtn.ButtonPressed = _open == Win.Rooms; _lottoBtn.ButtonPressed = _open == Win.Lottery;
        _figureBtn.ButtonPressed = _open == Win.Figure;
        _win.Visible = _open != Win.None;
        if (_open == Win.Rooms) { _room.RequestRoomList(); _room.RequestHouseList(); }
        if (_open != Win.None) RenderWindow();   // 창을 여는 순간은 즉시 그린다(다음 프레임까지 빈 창을 보이지 않게)
    }

    /// <summary>
    /// 한 프레임에 여러 이벤트가 겹쳐도 다시 그리기는 한 번만 하도록 모은다.
    /// (복권 한 번 뽑으면 지갑 갱신과 결과 도착이 따로 오는데, 예전엔 그때마다 다시 그렸다.)
    /// </summary>
    private void QueueRender()
    {
        if (_open == Win.None || _renderQueued) return;
        _renderQueued = true;
        CallDeferred(nameof(FlushRender));
    }

    private void FlushRender()
    {
        _renderQueued = false;
        if (_open != Win.None) RenderWindow();
    }

    private void RenderWindow()
    {
        // QueueFree 는 프레임 끝에 지운다. 즉시 떼어내지 않으면 한 프레임 동안 옛 위젯이 트리에 남아
        // 화면에도 보이고 클릭도 받는다 — 옛 버튼은 여전히 복권 뽑기·구매에 연결돼 있다.
        foreach (var c in _winBody.GetChildren()) { _winBody.RemoveChild(c); c.QueueFree(); }
        switch (_open)
        {
            case Win.Bag: RenderBag(); break;
            case Win.Shop: RenderShop(); break;
            case Win.Rooms: RenderRooms(); break;
            case Win.Postit: RenderPostit(); break;
            case Win.Lottery: RenderLottery(); break;
            case Win.Figure: RenderFigure(); break;
        }
    }

    // ---------------- 외모 꾸미기 ----------------
    private void RenderFigure()
    {
        _winTitle.Text = "외모 꾸미기";
        _winBody.AddChild(Note("고르면 바로 반영되고 저장돼요. 같은 방 사람들에게도 즉시 보입니다.", Ui.Muted));
        SwatchRow("털 바탕색", Ui.FurColors, "hd");
        SwatchRow("무늬색", Ui.PatternColors, "hr");
        SwatchRow("귀 안쪽", Ui.EarColors, "ea");
        SwatchRow("상의", Ui.ShirtColors, "ch");
        _winBody.AddChild(Note("아래 항목은 아직 도트 그림이 없어 화면에 반영되지 않아요 (고른 값은 저장되고, 그림이 들어오면 살아납니다).", Ui.Muted));
        SwatchRow("눈", Ui.EyeColors, "ey");
        StyleRow("무늬 모양", "hr", new[] { ("민무늬", 1), ("턱시도", 2), ("얼룩", 3), ("젖소", 4) });
        SwatchRow("하의", Ui.PantsColors, "lg");
        HatRow();
    }

    /// <summary>figure 의 한 파츠만 바꿔 서버로 보낸다. model·palette 가 둘 다 null 이면 그 파츠를 뺀다(모자 벗기).</summary>
    private void SetPart(string part, int? model, int? palette)
    {
        var parts = new Dictionary<string, (int model, int palette)>();
        foreach (var name in Figure.Parts)
            if (Figure.Get(_room.MyFigure, name) is { } v) parts[name] = v;

        if (model is null && palette is null) parts.Remove(part);
        else
        {
            var cur = parts.TryGetValue(part, out var c) ? c : (model: 1, palette: 1);
            parts[part] = (model ?? cur.model, palette ?? cur.palette);
        }
        _room.SetFigure(Figure.Build(parts));
    }

    private void SwatchRow(string label, Color[] colors, string part)
    {
        _winBody.AddChild(Ui.Text(label, 12, Ui.Muted));
        int cur = Figure.Get(_room.MyFigure, part)?.palette ?? 1;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        for (int i = 0; i < colors.Length; i++)
        {
            int idx = i + 1;
            var b = Ui.Swatch(colors[i], idx == cur);
            b.Pressed += () => SetPart(part, null, idx);
            row.AddChild(b);
        }
        _winBody.AddChild(row);
    }

    private void StyleRow(string label, string part, (string name, int model)[] options)
    {
        _winBody.AddChild(Ui.Text(label, 12, Ui.Muted));
        int cur = Figure.Get(_room.MyFigure, part)?.model ?? 1;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        foreach (var (name, model) in options)
        {
            var b = Ui.Button(name, primary: model == cur);
            int m = model;
            b.Pressed += () => SetPart(part, m, null);
            row.AddChild(b);
        }
        _winBody.AddChild(row);
    }

    private void HatRow()
    {
        _winBody.AddChild(Ui.Text("모자", 12, Ui.Muted));
        bool wearing = Figure.Get(_room.MyFigure, "ha") is not null;
        int cur = Figure.Get(_room.MyFigure, "ha")?.palette ?? 0;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);

        var off = Ui.Button("안 씀", primary: !wearing);
        off.Pressed += () => SetPart("ha", null, null);
        row.AddChild(off);
        for (int i = 0; i < Ui.HatColors.Length; i++)
        {
            int idx = i + 1;
            var b = Ui.Swatch(Ui.HatColors[i], wearing && idx == cur);
            b.Pressed += () => SetPart("ha", 1, idx);
            row.AddChild(b);
        }
        _winBody.AddChild(row);
    }

    // ---------------- 복권 ----------------
    private void OnLotteryResult(S_LotteryResult r)
    {
        if (r.Price > 0) _lottoPrice = r.Price;
        _lottoLog.Insert(0, r.Message);
        if (_lottoLog.Count > 12) _lottoLog.RemoveAt(_lottoLog.Count - 1);
        if (_open == Win.Lottery) QueueRender();
        if (r.Rank is 1 or 2) Toast(r.Message, false);
    }

    private void RenderLottery()
    {
        _winTitle.Text = $"복권  ·  루피 {_room.Rupee:N0}";
        _winBody.AddChild(Note($"한 장 {_lottoPrice} 루피. 1등 1000루피, 2등 200, 3등 50, 4등은 본전. 매일 첫 접속에 용돈을 드려요.", Ui.Muted));

        var buy = Ui.Button($"한 장 뽑기 ({_lottoPrice} 루피)", primary: true);
        buy.Disabled = _room.Rupee < _lottoPrice;
        buy.Pressed += () => _room.DrawLottery();
        _winBody.AddChild(buy);

        _winBody.AddChild(new HSeparator());
        if (_lottoLog.Count == 0) { _winBody.AddChild(Note("아직 뽑은 기록이 없어요.", Ui.Muted)); return; }
        _winBody.AddChild(Ui.Text("최근 결과", 12, Ui.Muted));
        foreach (var line in _lottoLog)
            _winBody.AddChild(Ui.Text(line, 14, line.StartsWith("꽝") ? Ui.Muted : Ui.AccentSoft));
    }

    // ---------------- 포스트잇 (읽기/쓰기) ----------------
    private void OpenPostit(FurniSprite it)
    {
        _postit = it;
        // 이미 열려 있으면 내용만 바꾼다. ToggleWindow 를 그냥 부르면 같은 창이라 **닫혀 버린다**.
        if (_open == Win.Postit) RenderWindow();
        else ToggleWindow(Win.Postit);
    }

    private void RenderPostit()
    {
        if (_postit is null || !IsInstanceValid(_postit)) { ToggleWindow(Win.None); return; }
        var it = _postit;
        bool canWrite = _room.CanWrite(it), canRemove = _room.CanRemove(it);
        _winTitle.Text = it.Author.Length > 0 ? $"{it.Author}의 포스트잇" : it.DisplayName;

        if (canWrite)
        {
            _winBody.AddChild(Note(it.Body.Length == 0 ? "메모를 적고 [붙이기]를 누르면 이 방에 남아요. (200자, 다른 사람도 읽을 수 있어요)" : "내 메모예요. 고쳐 쓰고 [붙이기]를 누르면 바뀌어요.", Ui.Muted));
            var te = new HangulInput { Multiline = true, MaxLength = 200, CustomMinimumSize = new Vector2(400, 130), Placeholder = "여기에 메모를 적어요 (Enter = 붙이기)" };
            te.Text = it.Body;
            _winBody.AddChild(te);
            void Save()
            {
                string body = te.Text.Trim();
                if (body.Length > 200) body = body[..200];
                if (body.Length == 0) { Toast("내용을 적어 주세요", true); return; }
                _room.WritePostit(it.ItemId, body);
                ToggleWindow(Win.None);
                Toast("포스트잇을 붙였어요", false);
            }
            te.Submitted += _ => Save();
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            var save = Ui.Button("붙이기", primary: true);
            save.Pressed += Save;
            row.AddChild(save);
            if (canRemove) { var rm = Ui.Button("떼기"); rm.Pressed += () => { _room.PickItem(it.ItemId); ToggleWindow(Win.None); }; row.AddChild(rm); }
            _winBody.AddChild(row);
            te.CallDeferred(Control.MethodName.GrabFocus);
        }
        else
        {
            var body = Ui.Text(it.Body.Length > 0 ? it.Body : "(아직 아무것도 안 적혀 있어요)", 15, it.Body.Length > 0 ? Ui.Paper : Ui.Muted);
            body.AutowrapMode = TextServer.AutowrapMode.Word;
            body.CustomMinimumSize = new Vector2(400, 0);
            _winBody.AddChild(Ui.Card(body, Ui.Alpha(FurniPalette.Find(it.FurniId)?.Tint ?? Ui.AccentSoft, 0.18f), null, 8, 12));
            if (canRemove)
            {
                _winBody.AddChild(Note("내 방이라 이 포스트잇을 뗄 수 있어요 (떼면 붙인 사람 가방으로는 돌아가지 않고 내 가방에 들어가요).", Ui.Muted));
                var rm = Ui.Button("떼기"); rm.Pressed += () => { _room.PickItem(it.ItemId); ToggleWindow(Win.None); };
                _winBody.AddChild(rm);
            }
        }
    }

    private void RenderBag()
    {
        _winTitle.Text = "가방";
        if (!_room.CanEdit)
            _winBody.AddChild(Note($"이 방은 {_room.RoomOwnerNick}님의 방이라 꾸밀 수 없어요. 내 방이나 공용 방에서 놓을 수 있어요.", Ui.Danger));
        else
            _winBody.AddChild(Note("배치를 누르고 바닥(벽걸이는 벽 옆)을 클릭하면 놓여요. 방에 놓인 가구는 우클릭으로 다시 가방에 담아요.", Ui.Muted));
        if (_room.Inventory.Count == 0) { _winBody.AddChild(Note("가방이 비었어요. 상점에서 루피로 사 보세요.", Ui.Muted)); return; }
        if (_room.Inventory.Values.Any(i => i.SellPrice > 0))
            _winBody.AddChild(Note("수확물은 [팔기]로 루피가 돼요. 묶음으로 팔면 낱개보다 더 쳐줍니다 — 모아서 한 번에 파세요.", Ui.Muted));
        foreach (var e in _room.Inventory.Values.OrderBy(i => i.Name))
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            var name = Ui.Text(e.Name + (e.Wall ? "  (벽걸이)" : ""), 14, Ui.Paper); name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(name);
            row.AddChild(Ui.Text($"×{e.Qty}", 14, Ui.AccentSoft));
            string id = e.FurniId;

            if (e.SellPrice > 0)
            {
                // 값은 서버가 다시 계산한다 — 여기 표시는 같은 순수 함수(Harbor.Core.Trade)로 뽑은 미리보기.
                var all = Trade.QuoteSell(e.Qty, e.SellPrice, e.SellBundleQty, e.SellBundlePrice);
                var one = Ui.Button($"1개 {e.SellPrice:N0}");
                one.Pressed += () => _room.Sell(id, 1);
                row.AddChild(one);
                var allBtn = Ui.Button($"전부 팔기 {all.Total:N0}", primary: true);
                allBtn.TooltipText = all.Bundles > 0 ? $"{e.SellBundleQty}개 묶음 {all.Bundles}개 포함" : $"{e.SellBundleQty}개를 모으면 묶음값({e.SellBundlePrice:N0})으로 팔려요";
                int qty = e.Qty;
                allBtn.Pressed += () => _room.Sell(id, qty);
                row.AddChild(allBtn);
            }

            var b = Ui.Button(_room.BuildFurni == e.FurniId ? "배치 중" : e.Interaction == "postit" ? "붙이기" : "배치", primary: _room.BuildFurni == e.FurniId);
            b.Disabled = !_room.CanEdit && e.Interaction != "postit";
            b.Pressed += () => _room.SetBuildMode(_room.BuildFurni == id ? null : id);
            row.AddChild(b);
            _winBody.AddChild(row);
        }
    }

    private void RenderShop()
    {
        _winTitle.Text = $"상점  ·  루피 {_room.Rupee:N0}";
        _winBody.AddChild(Note("루피로 가구를 사면 가방에 들어가요. 포스트잇은 다른 사람 방 벽에도 붙일 수 있어요. 지갑·가방·방 꾸밈은 저장되니 서버를 껐다 켜도 남아요.", Ui.Muted));
        if (_room.Catalog.Count == 0) { _winBody.AddChild(Note("카탈로그를 불러오는 중…", Ui.Muted)); return; }
        string? lastCat = null;
        foreach (var c in _room.Catalog)
        {
            if (c.Category != lastCat) { lastCat = c.Category; _winBody.AddChild(Ui.Text(CategoryName(c.Category), 12, Ui.Muted)); }
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            var name = Ui.Text(c.Name + (c.Wall ? "  (벽걸이)" : ""), 14, Ui.Paper); name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(name);
            row.AddChild(Ui.Text($"{c.Price:N0} 루피", 14, Ui.AccentSoft));
            var b = Ui.Button("구매", primary: true); b.Disabled = c.Currency != "rupee" || _room.Rupee < c.Price;
            string id = c.FurniId; b.Pressed += () => _room.Buy(id);
            row.AddChild(b);
            _winBody.AddChild(row);
        }
    }

    private void RenderRooms()
    {
        _winTitle.Text = "방 목록";
        _winBody.AddChild(Note("다른 사람 방은 구경만, 내 방과 공용 방은 꾸밀 수 있어요.", Ui.Muted));
        RenderUpgrade();
        RenderRemodel();
        if (_room.RoomList.Count == 0) { _winBody.AddChild(Note("방 목록을 불러오는 중…", Ui.Muted)); return; }
        foreach (var r in _room.RoomList)
        {
            bool mine = r.Id == _room.HomeRoomId, here = r.Id == _room.RoomId;
            string tag = mine ? "내 방" : r.Kind == "public" ? "공용" : $"{r.OwnerNick}님의 방";
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            var name = Ui.Text($"{r.Name}", 14, here ? Ui.AccentSoft : Ui.Paper); name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(name);
            row.AddChild(Ui.Text(tag, 12, mine ? Ui.Accent : Ui.Muted));
            row.AddChild(Ui.Text($"{r.Users}/{r.MaxUsers}", 12, Ui.Muted));
            var b = Ui.Button(here ? "여기" : "입장", primary: mine && !here); b.Disabled = here;
            long id = r.Id; b.Pressed += () => { _room.EnterRoom(id); ToggleWindow(Win.None); };
            row.AddChild(b);
            _winBody.AddChild(row);
        }
    }

    /// <summary>지금 내 방에 있고 더 넓힐 수 있으면 넓히기 칸을 보여준다. 가구는 그대로 따라온다(서버가 같은 자리로 옮겨 놓는다).</summary>
    private void RenderUpgrade()
    {
        if (!_room.IsMyRoom) return;
        if (_room.UpgradePrice <= 0)
        {
            _winBody.AddChild(Note($"내 방 '{_room.RoomName}'은(는) 가장 큰 단계예요.", Ui.Muted));
            _winBody.AddChild(new HSeparator());
            return;
        }
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
        var label = Ui.Text($"내 방 넓히기 → {_room.UpgradeName}", 14, Ui.Paper);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(label);
        row.AddChild(Ui.Text($"{_room.UpgradePrice:N0} 루피", 14, Ui.AccentSoft));
        var b = Ui.Button("넓히기", primary: true);
        b.Disabled = _room.Rupee < _room.UpgradePrice;
        b.Pressed += () => { _room.UpgradeRoom(); ToggleWindow(Win.None); };
        row.AddChild(b);
        _winBody.AddChild(row);
        _winBody.AddChild(Note($"지금 {_room.RoomWidth}×{_room.RoomHeight} 칸. 놓아 둔 가구는 그대로 있어요.", Ui.Muted));
        _winBody.AddChild(new HSeparator());
    }

    /// <summary>
    /// 이사 — 집 모양(계열)을 바꾼다. 방 모양이 달라 가구를 제자리에 둘 수 없으므로 **전부 가방으로 돌아온다**.
    /// 넓혀 둔 단계는 서버가 그대로 이어 준다(같은 단계가 없으면 그 계열의 마지막 단계).
    /// </summary>
    private void RenderRemodel()
    {
        if (!_room.IsMyRoom || _room.Houses.Count <= 1) return;
        _winBody.AddChild(Ui.Text("이사 — 집 모양 바꾸기", 14, Ui.Accent));
        _winBody.AddChild(Note("모양이 달라서 놓아 둔 가구는 전부 가방으로 돌아와요. 넓혀 둔 단계는 그대로 이어집니다.", Ui.Muted));
        foreach (var h in _room.Houses)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            var name = Ui.Text(h.Name, 14, h.Current ? Ui.AccentSoft : Ui.Paper);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(name);
            row.AddChild(Ui.Text($"{h.Tiles}칸", 12, Ui.Muted));
            if (h.Current) row.AddChild(Ui.Text("지금 집", 12, Ui.Accent));
            else
            {
                row.AddChild(Ui.Text($"{h.Price:N0} 루피", 14, Ui.AccentSoft));
                var b = Ui.Button("이사");
                b.Disabled = _room.Rupee < h.Price;
                string id = h.TemplateId;
                b.Pressed += () => { _room.Remodel(id); ToggleWindow(Win.None); };
                row.AddChild(b);
            }
            _winBody.AddChild(row);
        }
        _winBody.AddChild(new HSeparator());
    }

    private static string CategoryName(string c) => c switch
    {
        "appliance" => "가전", "seating" => "의자", "table" => "탁자", "consumable" => "소모품",
        "deco" => "장식", "wallart" => "벽 장식", "plant" => "식물 (키우기)", "postit" => "포스트잇 (방명록)",
        "harvest" => "수확물 (팔기)", _ => c,
    };

    private static Label Note(string text, Color color)
    {
        var l = Ui.Text(text, 12, color);
        l.AutowrapMode = TextServer.AutowrapMode.Word;
        l.CustomMinimumSize = new Vector2(400, 0);
        return l;
    }

    // ---------------- 토스트 ----------------
    private void BuildToast()
    {
        _toastText = Ui.Text("", 14, Ui.Paper, center: true);
        _toast = Ui.Card(_toastText, Ui.Panel, Ui.Line, 10, 10);
        _toast.Visible = false;
        _toast.AnchorLeft = 0.5f; _toast.AnchorRight = 0.5f; _toast.AnchorTop = 0; _toast.AnchorBottom = 0;
        _toast.OffsetTop = 14; _toast.GrowHorizontal = Control.GrowDirection.Both;
        AddChild(_toast);
    }

    private async void Toast(string msg, bool err)
    {
        int seq = ++_toastSeq;
        _toastText.Text = msg;
        _toast.AddThemeStyleboxOverride("panel", Ui.Box(err ? Ui.Danger.Lerp(Ui.Ink, 0.35f) : Ui.Panel, err ? Ui.Danger : Ui.Line, 10, 10));
        _toast.Visible = true;
        await ToSignal(GetTree().CreateTimer(err ? 3.5 : 2.5), SceneTreeTimer.SignalName.Timeout);
        if (seq == _toastSeq) _toast.Visible = false;
    }

    // ---------------- 이벤트 ----------------
    private void OnPhase(RoomView.Phase p)
    {
        bool inRoom = p == RoomView.Phase.InRoom;
        _start.Visible = !inRoom;
        _top.Visible = inRoom; _bottom.Visible = inRoom; _wallet.Visible = inRoom;
        if (inRoom) { _toast.Visible = false; SetStartStatus("", false); }
        else { ToggleWindow(Win.None); UpdateServerLabel(); if (p == RoomView.Phase.Idle) { _joinBtn.Disabled = false; _nick.GrabFocus(); } }
    }

    private void OnStatus(string msg, bool err)
    {
        if (_room.State == RoomView.Phase.InRoom) Toast(msg, err);
        else SetStartStatus(msg, err);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_room.State != RoomView.Phase.InRoom) return;
        if (e is not InputEventKey k || !k.Pressed || k.Echo) return;
        if (k.Keycode == Key.Escape && _open != Win.None) { ToggleWindow(Win.None); GetViewport().SetInputAsHandled(); return; }
        if ((k.Keycode is Key.Enter or Key.KpEnter) && !_chat.HasFocus()) { _chat.GrabFocus(); GetViewport().SetInputAsHandled(); }
    }
}
