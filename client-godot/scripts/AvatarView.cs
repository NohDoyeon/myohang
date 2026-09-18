using Godot;
using Harbor.Core;
using Harbor.Protocol;

namespace HarborClient;

/// <summary>
/// 아바타. 서버 경로를 타일 단위로 보간(서버 틱 60ms 기준).
/// 아틀라스가 없으면 `_Draw` 로 **두 발로 다니는 고양이**를 그린다.
/// 이족보행을 유지하는 이유: 앉기(seat FSM)·옷·모자·춤 동작이 전부 그 전제 위에 만들어져 있다.
/// </summary>
public partial class AvatarView : Node2D
{
    [Export] public Node2D Layers = null!;        // 파츠별 Sprite2D 자식: hd, hr, ch, lg, sh, ha
    [Export] public Label NameLabel = null!;
    [Export] public float TileSeconds = 0.06f;

    // 떠오르는 채팅 연출 — 원작처럼 친 말이 머리 위에서 하늘로 올라가며 사라진다.
    private const float BubbleStartY = -74f;
    private const float BubbleRisePx = 54f;
    private const float BubbleRiseSeconds = 4.2f;
    private const float BubbleFadeSeconds = 1.3f;

    // 무늬 (figure 의 hr 모델)
    private const int PatternPlain = 1, PatternTuxedo = 2, PatternTabby = 3, PatternCow = 4;

    /// <summary>경로 보간이 끝나 멈췄을 때(도착).</summary>
    public event Action? Arrived;
    public string Nick { get; private set; } = "";
    public (int x, int y) Tile => _tile;

    private Label _badge = null!;
    private readonly Queue<(Vector2 pos, byte dir, (int x, int y) tile)> _targets = new();
    private Vector2 _from, _to; private float _t = 1f; private bool _moving;
    private bool _lastLeg;      // 이 칸이 경로의 마지막인가 (도착할 때만 감속)
    private int _frame;         // 지금 보여 주는 애니메이션 프레임 (walk 0/1)
    private (int x, int y) _tile;
    private string _figure = ""; private byte _dir = 4; private string _action = "stand";
    private bool _placeholder = true; private float _anim;

    private Color _shirt = Ui.ShirtColors[0], _pants = Ui.PantsColors[0], _fur = Ui.Fur, _patternColor = Ui.PatternColors[0];
    private Color? _hatColor; private int _pattern = PatternPlain;
    private Color _earInner = Ui.EarColors[0], _eyeColor = Ui.EyeColors[0];

    public void Bind(UserDto u)
    {
        Layers ??= GetNode<Node2D>("Layers");
        NameLabel ??= GetNode<Label>("NameLabel");
        _badge = GetNode<Label>("Badge");

        Nick = u.Nick; _tile = (u.X, u.Y);
        SetFame(u.Fame);
        _figure = u.Figure;
        ApplyFigureColors();
        SetAction(u.Action, u.Dir);
    }

    public void MarkMe() => NameLabel.AddThemeColorOverride("font_color", Ui.AccentSoft);

    /// <summary>인기도 — 내 화분에 남들이 꽂아 준 캣닢의 누적 개수. 0 이면 닉만 보여 조용하다.</summary>
    public void SetFame(int fame)
    {
        _fame = Math.Max(0, fame);
        NameLabel.Text = _fame > 0 ? $"{Nick}  🌿{_fame}" : Nick;
    }
    private int _fame;

    /// <summary>외모가 바뀌었을 때(S_UserFigure). 즉시 다시 그린다.</summary>
    public void SetFigure(string figure)
    {
        _figure = figure;
        ApplyFigureColors();
        ApplyFigure(_dir, _action);
        QueueRedraw();
    }

    /// <summary>
    /// figure 문자열 → placeholder 색. 값이 없으면 닉 해시로 정해 모든 클라가 같은 고양이를 본다.
    /// 파츠 코드는 사람 시절 그대로 두고 **뜻만** 바꿨다(hd=털색, hr=무늬). 저장 데이터가 그대로 살아남는다.
    /// </summary>
    private void ApplyFigureColors()
    {
        uint h = Ui.Hash(Nick);
        _fur = Ui.FurColors[h % (uint)Ui.FurColors.Length];
        _patternColor = Ui.PatternColors[(h >> 8) % (uint)Ui.PatternColors.Length];
        _shirt = Ui.ShirtColors[(h >> 16) % (uint)Ui.ShirtColors.Length];
        _pants = Ui.Darken(_shirt, 0.55f);
        _pattern = PatternPlain;
        _hatColor = null;

        if (Figure.Get(_figure, "hd") is { } hd) _fur = Ui.Pick(Ui.FurColors, hd.palette);
        if (Figure.Get(_figure, "hr") is { } hr) { _pattern = hr.model; _patternColor = Ui.Pick(Ui.PatternColors, hr.palette); }
        if (Figure.Get(_figure, "ch") is { } ch) _shirt = Ui.Pick(Ui.ShirtColors, ch.palette);
        if (Figure.Get(_figure, "lg") is { } lg) _pants = Ui.Pick(Ui.PantsColors, lg.palette);
        if (Figure.Get(_figure, "ha") is { } ha) _hatColor = Ui.Pick(Ui.HatColors, ha.palette);
        if (Figure.Get(_figure, "ea") is { } ea) _earInner = Ui.Pick(Ui.EarColors, ea.palette);
        if (Figure.Get(_figure, "ey") is { } ey) _eyeColor = Ui.Pick(Ui.EyeColors, ey.palette);

        ApplyPaletteSwap();
    }

    /// <summary>
    /// 도트 스프라이트에 팔레트 스왑을 건다. **도트가 있으면 `_Draw` 는 아예 돌지 않으므로**
    /// (`_placeholder == false`) 코드 그림에 쓰던 색이 화면에 반영될 곳이 없다 — 색은 여기서 먹인다.
    /// 아바타마다 색이 다르므로 머티리얼은 **파츠 스프라이트마다 하나씩** 따로 가진다(공유하면 전부 같은 색이 된다).
    /// </summary>
    private void ApplyPaletteSwap()
    {
        var shader = GD.Load<Shader>(PaletteShaderPath);
        if (shader is null)
        {
            if (!_paletteLogged) { _paletteLogged = true; GD.PrintErr($"[Palette] 셰이더를 못 읽음: {PaletteShaderPath}"); }
            return;                                        // 셰이더가 없으면 조용히 원본 색 그대로
        }

        var targets = SpritePalette.Targets(_fur, _patternColor, _shirt, _earInner);
        var sources = SpritePalette.Sources();
        int n = 0;
        foreach (var child in Layers.GetChildren())
        {
            if (child is not Sprite2D spr) continue;
            if (spr.Material is not ShaderMaterial mat || mat.Shader != shader)
                spr.Material = mat = new ShaderMaterial { Shader = shader };
            mat.SetShaderParameter("pair_count", SpritePalette.PairCount);
            mat.SetShaderParameter("src_colors", sources);
            mat.SetShaderParameter("dst_colors", targets);
            n++;
        }
        if (!_paletteLogged)
        {
            _paletteLogged = true;
            GD.Print($"[Palette] 스프라이트 {n}개에 적용 · 쌍 {SpritePalette.PairCount} · 털 {_fur.ToHtml(false)} → {targets[0]}");
        }
    }

    /// <summary>진단 출력은 첫 아바타에서 한 번만 (방에 사람이 많으면 로그가 덮인다).</summary>
    private static bool _paletteLogged;

    private const string PaletteShaderPath = "res://ui/palette_swap.gdshader";

    /// <summary>
    /// 그림의 방향 번호를 엔진 방향에 맞추는 보정. 엔진은 아이소메트릭 기준(화면 아래 = dir 3)이고
    /// 시트는 back(0)→front(4) 를 위아래 5단계로 그려서 한 칸 밀려 있다.
    /// **방향이 통째로 어긋나면 이 숫자를 ±1 해 보면 된다.**
    /// </summary>
    private const int DirOffset = 1;

    /// <summary>
    /// 시트의 옆모습이 **화면 왼쪽**을 보고 있는가. 'right' 라벨이 캐릭터 기준인지 화면 기준인지
    /// 그림마다 다르다 — 좌우만 반대로 보이면 이 값을 뒤집는다.
    /// </summary>
    private const bool SheetFacesLeft = false;

    public void SetPath(List<(int x, int y)> path, Heightmap map)
    {
        _targets.Clear();
        var prev = _tile;
        foreach (var (x, y) in path)
        {
            var (sx, sy) = Iso.ToScreen(x + 0.5f, y + 0.5f, map.Height(x, y));
            _targets.Enqueue((new Vector2(sx, sy), (byte)Iso.DirectionBetween(prev, (x, y)), (x, y)));
            prev = (x, y);
        }
        if (_targets.Count > 0) SetAction("walk", _targets.Peek().dir);
    }

    public void SetAction(string action, byte dir)
    {
        _action = action; _dir = dir;
        ApplyFigure(dir, action);
        _badge.Text = FurniPalette.ActionBadge(action);
        _badge.Visible = _badge.Text.Length > 0;
        QueueRedraw();
    }

    /// <summary>
    /// 말풍선을 하나 띄운다. 고정되지 않고 머리 위에서 떠올라 사라지므로 여러 개가 자연스럽게 쌓인다.
    /// 노드를 매번 새로 만들고 트윈이 끝나면 스스로 정리한다.
    /// </summary>
    public async void ShowBubble(string text)
    {
        var label = Ui.Text(text, 9, Ui.Ink, center: true);
        if (text.Length > 14)   // 폰트 측정 대신 글자 수로 — 트리에 들어가기 전이라 테마 조회가 불안정하다
        {
            label.AutowrapMode = TextServer.AutowrapMode.Word;
            label.CustomMinimumSize = new Vector2(112, 0);
        }
        var panel = Ui.Card(label, Ui.Paper, null, 6, 4);
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.ZIndex = 20;
        panel.Modulate = new Color(1, 1, 1, 0);          // 크기가 잡히기 전 한 프레임 깜빡임 방지
        panel.Position = new Vector2(-56, BubbleStartY);
        AddChild(panel);

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInstanceValid(panel)) return;

        float y = BubbleStartY - panel.Size.Y;
        panel.Position = new Vector2(-panel.Size.X / 2f, y);
        panel.Modulate = Colors.White;

        var tw = CreateTween();
        tw.SetParallel(true);
        tw.TweenProperty(panel, "position:y", y - BubbleRisePx, BubbleRiseSeconds)
          .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(panel, "modulate:a", 0f, BubbleFadeSeconds)
          .SetDelay(BubbleRiseSeconds - BubbleFadeSeconds);
        tw.SetParallel(false);
        tw.TweenCallback(Callable.From(panel.QueueFree));
    }

    public override void _Process(double delta)
    {
        _anim += (float)delta;
        if (_t >= 1f)
        {
            if (_targets.TryDequeue(out var next))
            {
                _from = Position; _to = next.pos; _t = 0f; _moving = true; _tile = next.tile;
                _lastLeg = _targets.Count == 0;
                if (_dir != next.dir) { _dir = next.dir; ApplyFigure(_dir, _action, _frame); }
            }
            else if (_moving)
            {
                _moving = false;
                if (_action == "walk") SetAction("stand", _dir);   // 서버의 최종 stand/sit 이 곧 덮어씀
                Arrived?.Invoke();
            }
        }
        if (_t < 1f)
        {
            _t = Mathf.Min(1f, _t + (float)delta / TileSeconds);
            // 마지막 한 칸에서만 살짝 감속한다. 중간 칸까지 이징을 넣으면 칸마다 멈칫거려 오히려 부자연스럽다.
            float e = _lastLeg ? 1f - (1f - _t) * (1f - _t) : _t;
            Position = _from.Lerp(_to, e);
            // 단차를 오르내릴 땐 살짝 호를 그린다(발이 턱을 넘는 느낌).
            if (Mathf.Abs(_to.Y - _from.Y) > 4f) Position -= new Vector2(0, StepArcPx * Mathf.Sin(Mathf.Pi * e));
        }

        // 걷기 2프레임을 **한 칸에 한 걸음**으로 맞춘다 — 벽시계가 아니라 이동 진행도에 맞춰야 발이 땅을 딛는다.
        int want = _moving && _action == "walk" ? (_t < 0.5f ? 0 : 1) : 0;
        if (want != _frame) { _frame = want; ApplyFigure(_dir, _action, _frame); }

        QueueRedraw();                     // 그림자와 placeholder 는 매 프레임 다시 그린다
        if (!_placeholder) AnimateSprite();
    }

    /// <summary>단차를 넘을 때 그리는 호의 높이(px).</summary>
    private const float StepArcPx = 4f;

    /// <summary>
    /// 스프라이트 프레임이 부족해도 캐릭터가 죽어 보이지 않게 몸통을 통째로 위아래로 흔든다.
    /// **정수 픽셀 단위로만 움직인다** — 소수점으로 움직이면 도트가 반 칸에 걸쳐 찢어진다.
    /// 프레임이 늘어나면 이건 걷기 애니메이션 위에 얹히는 보조 움직임이 된다.
    /// </summary>
    private void AnimateSprite()
    {
        int bob = _action switch
        {
            // 걷기는 **이동 진행도**에 맞춘다(벽시계가 아니라). 그림 자체에 걸음이 들어 있으므로 폭은 1px 로 줄였다.
            "walk" => -(int)MathF.Round(MathF.Sin(MathF.PI * _t) * 1f),
            "dance" => -(int)MathF.Round(MathF.Abs(MathF.Sin(_anim * 8f)) * 4f),
            "laugh" => -(int)MathF.Round(MathF.Abs(MathF.Sin(_anim * 16f)) * 2f),
            "wave" => -(int)MathF.Round(MathF.Abs(MathF.Sin(_anim * 6f)) * 1f),
            "sleep" => 2,
            "sit" => 0,
            _ => -(int)MathF.Round(MathF.Abs(MathF.Sin(_anim * 1.6f)) * 1f),   // 숨쉬기 0~1px
        };
        if (Layers.Position.Y != bob) Layers.Position = new Vector2(0, bob);
    }

    private void ApplyFigure(byte dir, string action, int frame = 0)
    {
        // figure "hd-001-01.hr-012-05..." → 파츠별 텍스처 키 avatar/{part}/{id}_{dir}_{anim}_{frame}
        //
        // 그림의 방향 번호와 엔진의 방향 번호가 다르다. 엔진 dir 은 **아이소메트릭 기준**이라
        // 화면 '아래'(정면으로 다가옴)가 dir 3, '위'가 dir 7, '오른쪽'이 dir 1 이다.
        // 반면 시트는 back(0) → front(4) 를 위아래 5단계로 그렸다 → 한 칸씩 밀려 있어 보정한다.
        byte artDir = (byte)((dir + DirOffset) % 8);
        bool mirror = artDir is 5 or 6 or 7;
        byte baseDir = mirror ? (byte)(8 - artDir) : artDir;   // 5방향만 제작, 나머지는 좌우 반전
        bool any = false;
        foreach (var part in _figure.Split('.'))
        {
            var p = part.Split('-');
            if (p.Length < 3) continue;
            if (Layers.GetNodeOrNull<Sprite2D>(p[0]) is not { } spr) continue;

            var tex = PickTexture(p[0], p[1], baseDir, action, frame, out bool exactDir);
            spr.Texture = tex;
            // 좌우 뒤집기. 정면 대체 프레임을 쓸 땐 뒤집지 않는다(미러는 옆모습 그림이 있을 때만 의미 있다).
            //
            spr.FlipH = exactDir && (mirror ^ SheetFacesLeft);
            spr.SetMeta("palette", p[2]);   // 팔레트 스왑 셰이더 uniform 은 후속 작업
            // 원점은 발끝(y=0). Sprite2D 는 가운데 정렬이므로 높이의 절반만큼 올려야 바닥에 선다.
            // 스프라이트 크기가 바뀌어도 .tscn 을 고칠 필요가 없도록 텍스처에서 계산한다.
            if (tex is not null) spr.Offset = new Vector2(0, -tex.GetHeight() / 2f);
            any |= tex is not null;
        }
        _placeholder = !any;
    }

    /// <summary>
    /// 그림을 한 장씩 채워 넣을 수 있도록 넓게 대체한다. 없는 것 하나 때문에 아바타가 통째로
    /// placeholder 로 떨어지면 아트를 조금씩 넣는 작업이 불가능하다.
    /// 순서: 그 방향+그 동작 → 그 방향+서기 → 정면+그 동작 → 정면+서기.
    /// </summary>
    private static Texture2D? PickTexture(string part, string model, byte baseDir, string action, int frame, out bool exactDir)
    {
        const byte Front = 4;
        exactDir = true;
        // 그 방향+그 동작의 그 프레임 → 같은 동작의 0번 프레임(2프레임 중 하나만 있어도 돌아간다)
        if (AssetCatalog.TryGet($"avatar/{part}/{model}_{baseDir}_{action}_{frame}") is { } a) return a;
        if (frame != 0 && AssetCatalog.TryGet($"avatar/{part}/{model}_{baseDir}_{action}_0") is { } a0) return a0;
        if (AssetCatalog.TryGet($"avatar/{part}/{model}_{baseDir}_stand_0") is { } b) return b;

        exactDir = false;
        if (baseDir == Front) return null;                 // 정면인데 없으면 더 볼 것도 없다
        return AssetCatalog.TryGet($"avatar/{part}/{model}_{Front}_{action}_{frame}")
            ?? AssetCatalog.TryGet($"avatar/{part}/{model}_{Front}_{action}_0")
            ?? AssetCatalog.TryGet($"avatar/{part}/{model}_{Front}_stand_0");
    }

    // ================= 고양이 그리기 =================
    public override void _Draw()
    {
        // 그림자는 **도트 모드에서도** 그린다. 없으면 발이 바닥에 닿아 보이지 않아 떠다니는 느낌이 난다.
        // 걸을 때 몸이 뜨는 만큼 조금 작아지고 옅어져서, 한 칸에 한 걸음 딛는 리듬이 바닥에도 드러난다.
        float lift = _moving && _action == "walk" ? Mathf.Sin(Mathf.Pi * _t) : 0f;
        float sx = 11f - lift * 2f, sy = 4.5f - lift * 0.8f;
        DrawColoredPolygon(Ellipse(0, 1, sx, sy), new Color(0, 0, 0, 0.22f - lift * 0.05f));

        if (!_placeholder) return;

        bool sit = _action == "sit";
        float bob = _action switch
        {
            "walk" => Mathf.Abs(Mathf.Sin(_anim * 14f)) * 2f,
            "dance" => Mathf.Abs(Mathf.Sin(_anim * 9f)) * 4f,
            "laugh" => Mathf.Abs(Mathf.Sin(_anim * 18f)) * 1.5f,
            _ => 0f,
        };
        float y0 = -bob + (sit ? 6f : 0f);          // 발끝 기준선
        var line = Ui.Alpha(Ui.Ink, 0.85f);
        int facing = _dir switch { 3 or 4 or 5 => 2, 2 or 6 => 1, _ => 0 };   // 2=정면 1=측면 0=뒷모습
        float side = _dir is 2 or 1 or 3 ? 1f : -1f;                          // 측면일 때 바라보는 쪽

        DrawTail(y0, sit, line);

        // ----- 다리 -----
        var legC = _pants;
        if (sit) DrawRect(new Rect2(-7, y0 - 6, 14, 6), legC);
        else
        {
            float swing = _action == "walk" ? Mathf.Sin(_anim * 14f) * 2f : 0f;
            DrawRect(new Rect2(-6 + swing, y0 - 9, 5, 9), legC);
            DrawRect(new Rect2(1 - swing, y0 - 9, 5, 9), legC);
            DrawColoredPolygon(Ellipse(-3.5f + swing, y0 - 0.5f, 3.2f, 1.6f, 10), _fur);   // 앞발
            DrawColoredPolygon(Ellipse(3.5f - swing, y0 - 0.5f, 3.2f, 1.6f, 10), _fur);
        }

        // ----- 몸통 -----
        float bodyTop = y0 - 25, bodyH = 16;
        DrawRect(new Rect2(-7, bodyTop, 14, bodyH), _shirt);
        if (_pattern == PatternTuxedo)                                         // 턱시도: 가슴받이
            DrawColoredPolygon(new Vector2[] { new(-3.5f, bodyTop), new(3.5f, bodyTop), new(0, bodyTop + 8) }, _patternColor);
        if (_pattern == PatternCow)
            DrawColoredPolygon(Ellipse(3.5f, bodyTop + 10, 3.5f, 2.8f, 12), _patternColor);
        DrawRect(new Rect2(-7, bodyTop, 14, bodyH), line, false, 1f);

        // ----- 팔 -----
        if (_action == "wave")
        {
            float k = Mathf.Sin(_anim * 12f) * 3f;
            DrawLine(new Vector2(7, y0 - 21), new Vector2(13 + k, y0 - 34), _fur, 3.5f);
            DrawCircle(new Vector2(13 + k, y0 - 34), 2.2f, _fur);
        }
        else if (_action == "dance")
        {
            float k = Mathf.Sin(_anim * 9f) * 4f;
            DrawLine(new Vector2(-7, y0 - 21), new Vector2(-12, y0 - 30 - k), _fur, 3.5f);
            DrawLine(new Vector2(7, y0 - 21), new Vector2(12, y0 - 30 + k), _fur, 3.5f);
        }

        // ----- 머리 -----
        float hy = y0 - 34;
        const float hr = 9f;
        DrawEars(hy, hr, line);
        DrawCircle(new Vector2(0, hy), hr, _fur);

        if (_pattern == PatternTabby)                                          // 얼룩: 이마 줄무늬
            for (int i = -1; i <= 1; i++)
                DrawLine(new Vector2(i * 3f, hy - hr + 1.5f), new Vector2(i * 3f, hy - hr + 5.5f), _patternColor, 1.6f);
        if (_pattern == PatternCow)                                            // 젖소: 한쪽 눈 덮는 얼룩
            DrawColoredPolygon(Ellipse(-3.5f, hy - 1f, 5f, 4.5f, 14), _patternColor);

        DrawArc(new Vector2(0, hy), hr, 0, Mathf.Tau, 28, line, 1f);

        if (facing > 0) DrawFace(hy, hr, facing, side, line);
        if (_hatColor is { } hat) DrawHat(hy, hr, hat, line);
    }

    /// <summary>귀 — 머리 뒤에 먼저 그려 머리 원이 밑동을 덮게 한다.</summary>
    private void DrawEars(float hy, float hr, Color line)
    {
        float twitch = _action == "sleep" ? -1.2f : Mathf.Sin(_anim * 1.7f) * 0.6f;
        foreach (float s in new[] { -1f, 1f })
        {
            var a = new Vector2(s * 6.5f, hy - hr + 3.5f);
            var b = new Vector2(s * 2.5f, hy - hr - 0.5f);
            var tip = new Vector2(s * 8.5f, hy - hr - 6.5f + twitch);
            DrawColoredPolygon(new[] { a, b, tip }, _fur);
            DrawColoredPolygon(new[] { a.Lerp(tip, 0.2f), b.Lerp(tip, 0.25f), tip.Lerp(a, 0.3f) }, _earInner);
            DrawPolyline(new[] { a, b, tip, a }, line, 1f);
        }
    }

    private void DrawFace(float hy, float hr, int facing, float side, Color line)
    {
        bool asleep = _action == "sleep";
        float ex = facing == 2 ? 3.2f : 3.4f * side;

        if (facing == 2)
        {
            DrawEye(new Vector2(-3.2f, hy - 0.5f), asleep, line);
            DrawEye(new Vector2(3.2f, hy - 0.5f), asleep, line);
        }
        else DrawEye(new Vector2(ex, hy - 0.5f), asleep, line);

        // 주둥이 + 코
        float mx = facing == 2 ? 0f : side * 2.2f;
        DrawColoredPolygon(Ellipse(mx, hy + 4f, 4.6f, 3.2f, 14), Ui.Lighten(_fur, 0.35f));
        DrawColoredPolygon(new Vector2[] { new(mx - 1.6f, hy + 2.4f), new(mx + 1.6f, hy + 2.4f), new(mx, hy + 4f) }, new Color("c06a7a"));
        if (_action == "laugh")
            DrawArc(new Vector2(mx, hy + 4.4f), 2.4f, 0.25f, Mathf.Pi - 0.25f, 10, line, 1.1f);
        else
            DrawLine(new Vector2(mx, hy + 4f), new Vector2(mx, hy + 5.4f), line, 1f);

        // 수염
        var whisker = Ui.Alpha(Ui.Ink, 0.4f);
        foreach (float s in facing == 2 ? new[] { -1f, 1f } : new[] { side })
            for (int i = 0; i < 2; i++)
                DrawLine(new Vector2(mx + s * 3.5f, hy + 3.5f + i * 2f),
                         new Vector2(mx + s * 11f, hy + 1.5f + i * 3.2f), whisker, 1f);
    }

    private void DrawEye(Vector2 at, bool asleep, Color line)
    {
        if (asleep) { DrawArc(at + new Vector2(0, 1f), 2f, Mathf.Pi, Mathf.Tau, 10, line, 1.2f); return; }
        DrawColoredPolygon(Ellipse(at.X, at.Y, 1.9f, 2.3f, 12), Ui.Ink);
        DrawCircle(at + new Vector2(0.6f, -0.7f), 0.7f, Ui.Paper);   // 눈빛
    }

    /// <summary>꼬리 — 늘 살랑거린다. 몸 뒤에 그려 실루엣을 만든다.</summary>
    private void DrawTail(float y0, bool sit, Color line)
    {
        float speed = _moving ? 12f : _action == "dance" ? 10f : 2.0f;
        float amp = _moving ? 6f : _action == "dance" ? 7f : 3.2f;
        float sway = Mathf.Sin(_anim * speed) * amp;

        Vector2[] tail = sit
            ? new Vector2[] { new(6, y0 - 4), new(11, y0 - 2), new(15 + sway * 0.3f, y0 - 5), new(16 + sway * 0.5f, y0 - 11) }
            : new Vector2[] { new(6, y0 - 11), new(11, y0 - 15), new(14 + sway * 0.3f, y0 - 22), new(13 + sway, y0 - 30) };

        DrawPolyline(tail, line, 6f);
        DrawPolyline(tail, _fur, 4f);
        if (_pattern == PatternTabby)
            for (int i = 1; i < tail.Length; i++)
                DrawLine(tail[i], tail[i].Lerp(tail[i - 1], 0.3f), _patternColor, 4f);
        DrawCircle(tail[^1], 2f, _pattern == PatternTuxedo ? _patternColor : _fur);
    }

    private void DrawHat(float hy, float hr, Color hat, Color line)
    {
        DrawRect(new Rect2(-11, hy - hr - 2.5f, 22, 2.8f), Ui.Darken(hat, 0.3f));
        DrawColoredPolygon(Arc(0, hy - hr - 2f, 7.5f, Mathf.Pi, Mathf.Tau), hat);
        DrawArc(new Vector2(0, hy - hr - 2f), 7.5f, Mathf.Pi, Mathf.Tau, 14, line, 1f);
    }

    private static Vector2[] Ellipse(float cx, float cy, float rx, float ry, int n = 20)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++) { float a = Mathf.Tau * i / n; pts[i] = new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry); }
        return pts;
    }

    private static Vector2[] Arc(float cx, float cy, float r, float a0, float a1, int n = 14)
    {
        var pts = new Vector2[n + 1];
        for (int i = 0; i <= n; i++) { float a = a0 + (a1 - a0) * i / n; pts[i] = new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r); }
        return pts;
    }
}
