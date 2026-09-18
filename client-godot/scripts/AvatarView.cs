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
    private (int x, int y) _tile;
    private string _figure = ""; private byte _dir = 4; private string _action = "stand";
    private bool _placeholder = true; private float _anim;

    private Color _shirt = Ui.ShirtColors[0], _pants = Ui.PantsColors[0], _fur = Ui.Fur, _patternColor = Ui.PatternColors[0];
    private Color? _hatColor; private int _pattern = PatternPlain;

    public void Bind(UserDto u)
    {
        Layers ??= GetNode<Node2D>("Layers");
        NameLabel ??= GetNode<Label>("NameLabel");
        _badge = GetNode<Label>("Badge");

        Nick = u.Nick; NameLabel.Text = u.Nick; _tile = (u.X, u.Y);
        _figure = u.Figure;
        ApplyFigureColors();
        SetAction(u.Action, u.Dir);
    }

    public void MarkMe() => NameLabel.AddThemeColorOverride("font_color", Ui.AccentSoft);

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
    }

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
                if (_dir != next.dir) { _dir = next.dir; ApplyFigure(_dir, _action); }
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
            Position = _from.Lerp(_to, _t);
        }
        if (_placeholder) QueueRedraw();   // 꼬리가 늘 살랑거린다 — 가만히 있어도 살아 있어 보이게
        else AnimateSprite();              // 스프라이트가 한 장뿐이어도 최소한 움직이게
    }

    /// <summary>
    /// 스프라이트 프레임이 부족해도 캐릭터가 죽어 보이지 않게 몸통을 통째로 위아래로 흔든다.
    /// **정수 픽셀 단위로만 움직인다** — 소수점으로 움직이면 도트가 반 칸에 걸쳐 찢어진다.
    /// 프레임이 늘어나면 이건 걷기 애니메이션 위에 얹히는 보조 움직임이 된다.
    /// </summary>
    private void AnimateSprite()
    {
        int bob = _action switch
        {
            "walk" => -(int)MathF.Round(MathF.Abs(MathF.Sin(_anim * 12f)) * 3f),
            "dance" => -(int)MathF.Round(MathF.Abs(MathF.Sin(_anim * 8f)) * 4f),
            "laugh" => -(int)MathF.Round(MathF.Abs(MathF.Sin(_anim * 16f)) * 2f),
            "wave" => -(int)MathF.Round(MathF.Abs(MathF.Sin(_anim * 6f)) * 1f),
            "sleep" => 2,
            "sit" => 0,
            _ => -(int)MathF.Round(MathF.Abs(MathF.Sin(_anim * 1.6f)) * 1f),   // 숨쉬기 0~1px
        };
        if (Layers.Position.Y != bob) Layers.Position = new Vector2(0, bob);
    }

    private void ApplyFigure(byte dir, string action)
    {
        // figure "hd-001-01.hr-012-05..." → 파츠별 텍스처 키 avatar/{part}/{id}_{dir}_{anim}_{frame}
        bool mirror = dir is 5 or 6 or 7;
        byte baseDir = mirror ? (byte)(8 - dir) : dir;     // 5방향만 제작, 나머지 미러
        bool any = false;
        foreach (var part in _figure.Split('.'))
        {
            var p = part.Split('-');
            if (p.Length < 3) continue;
            if (Layers.GetNodeOrNull<Sprite2D>(p[0]) is not { } spr) continue;

            var tex = PickTexture(p[0], p[1], baseDir, action, out bool exactDir);
            spr.Texture = tex;
            // 정면 대체 프레임을 쓸 땐 뒤집으면 안 된다(왼쪽 미러는 옆모습 그림이 있을 때만 의미 있다).
            spr.FlipH = mirror && exactDir;
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
    private static Texture2D? PickTexture(string part, string model, byte baseDir, string action, out bool exactDir)
    {
        const byte Front = 4;
        exactDir = true;
        if (AssetCatalog.TryGet($"avatar/{part}/{model}_{baseDir}_{action}_0") is { } a) return a;
        if (AssetCatalog.TryGet($"avatar/{part}/{model}_{baseDir}_stand_0") is { } b) return b;

        exactDir = false;
        if (baseDir == Front) return null;                 // 정면인데 없으면 더 볼 것도 없다
        return AssetCatalog.TryGet($"avatar/{part}/{model}_{Front}_{action}_0")
            ?? AssetCatalog.TryGet($"avatar/{part}/{model}_{Front}_stand_0");
    }

    // ================= 고양이 그리기 =================
    public override void _Draw()
    {
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

        DrawColoredPolygon(Ellipse(0, 1, 11, 4.5f), new Color(0, 0, 0, 0.22f));   // 그림자
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
            DrawColoredPolygon(new[] { a.Lerp(tip, 0.2f), b.Lerp(tip, 0.25f), tip.Lerp(a, 0.3f) }, Ui.EarInner);
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
