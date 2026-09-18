using Godot;
using Harbor.Core;
using Harbor.Protocol;

namespace HarborClient;

/// <summary>
/// 가구 노드. 스프라이트 키 = {sprite}_{dir}_{state}_{frame} (아틀라스 규약).
/// 아틀라스가 없으면 `_Draw` 로 미니어처를 그린다 — 모양은 `FurniPalette.Shape`.
/// 원점(0,0): 바닥 가구는 타일 중심(발 닿는 곳), 벽걸이는 벽 슬롯 중심.
/// </summary>
public partial class FurniSprite : Node2D
{
    [Export] public Sprite2D Sprite = null!;
    [Export] public Label NameLabel = null!;

    public long ItemId { get; private set; }
    public (int x, int y) Tile { get; private set; }
    public string FurniId { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public string State { get; private set; } = "";
    public bool Solid { get; private set; }
    public bool Wall { get; private set; }
    public byte Dir { get; private set; }
    public byte WallU { get; private set; }
    public byte WallV { get; private set; }
    /// <summary>포스트잇 등 벽에 삐뚜름하게 붙이는 기울기(도).</summary>
    public sbyte Tilt { get; private set; }
    public string Interaction { get; private set; } = "";
    /// <summary>지금 상태에서 '사용'이 먹히는가 (서버 판단). 자라는 중인 식물은 false.</summary>
    public bool Usable { get; private set; }
    public string Author { get; private set; } = "";
    /// <summary>포스트잇 본문 (ItemDto.Extra / S_ItemState.Extra).</summary>
    public string Body { get; private set; } = "";
    public bool IsPostit => Interaction == "postit";
    public bool IsGhost { get; private set; }

    private Label _stateLabel = null!;
    private bool _placeholder = true;
    private Color _tint = Ui.Sea;
    private int _heightPx = 24;
    private FurniPalette.Shape _shape = FurniPalette.Shape.Box;

    public void Bind(ItemDto it)
    {
        Sprite ??= GetNode<Sprite2D>("Sprite");
        NameLabel ??= GetNode<Label>("NameLabel");
        _stateLabel = GetNode<Label>("StateLabel");

        ItemId = it.Id; Tile = (it.X, it.Y); FurniId = it.FurniId; Dir = it.Dir;
        Solid = it.Solid; Wall = it.Wall; Interaction = it.Interaction; Author = it.Author; Body = it.Extra ?? ""; Usable = it.Usable;
        WallU = it.WallU; WallV = it.WallV;

        var e = FurniPalette.Find(it.FurniId);
        DisplayName = it.Name.Length > 0 ? it.Name : (e?.Name ?? it.FurniId);
        _tint = e?.Tint ?? Ui.ColorFromHash(it.FurniId);
        _heightPx = e?.HeightPx ?? 24;
        _shape = e?.Shape ?? (Wall ? FurniPalette.Shape.Panel : FurniPalette.Shape.Box);
        SetTilt(it.Tilt);

        NameLabel.Text = IsPostit && Author.Length > 0 ? $"{Author}의 포스트잇" : DisplayName;
        float top = Wall ? -_heightPx / 2f - 2 : -_heightPx - 12;
        NameLabel.Position = new Vector2(-50, top - 24); NameLabel.Size = new Vector2(100, 12);
        _stateLabel.Position = new Vector2(-50, top - 12); _stateLabel.Size = new Vector2(100, 12);
        SetState(it.State);
    }

    public void SetState(string state, string? extra = null, bool? usable = null)
    {
        State = state;
        if (extra is not null) Body = extra;
        if (usable is { } u) Usable = u;
        var tex = PickTexture(FurniId, Dir, state);
        _placeholder = tex is null;
        Sprite.Texture = tex;
        NameLabel.Visible = _placeholder;
        _stateLabel.Text = FurniPalette.StateLabel(state);
        _stateLabel.Visible = _placeholder && !IsGhost && _stateLabel.Text.Length > 0;
        QueueRedraw();
    }

    /// <summary>
    /// 그림을 **한 장씩** 채워 넣을 수 있게 넓게 대체한다. 아바타(`AvatarView.PickTexture`)와 같은 이유다 —
    /// 방향 하나, 상태 하나가 없다고 가구가 통째로 placeholder 로 떨어지면 아트를 조금씩 넣는 작업 자체가 불가능하다.
    /// 순서: 그 방향+그 상태 → 그 방향+기본 → **dir 0**+그 상태 → dir 0+기본.
    /// </summary>
    private static Texture2D? PickTexture(string furniId, byte dir, string state)
        => AssetCatalog.TryGet($"{furniId}_{dir}_{state}_0")
        ?? AssetCatalog.TryGet($"{furniId}_{dir}_default_0")
        ?? (dir == 0 ? null : AssetCatalog.TryGet($"{furniId}_0_{state}_0") ?? AssetCatalog.TryGet($"{furniId}_0_default_0"));

    public void SetDir(byte dir) { Dir = dir; SetState(State); }

    /// <summary>기울기는 벽걸이에만. 노드 회전 = 물건만 삐뚜름(벽면 기울기는 _Draw 가 따로 맞춘다).</summary>
    public void SetTilt(sbyte t)
    {
        Tilt = t;
        Rotation = Wall ? Mathf.DegToRad(t) : 0f;
        QueueRedraw();
    }

    /// <summary>배치 미리보기용 반투명 고스트.</summary>
    public void SetGhost(bool on)
    {
        IsGhost = on;
        Modulate = on ? new Color(1, 1, 1, 0.6f) : Colors.White;
        _stateLabel.Visible = false;
    }
    public void SetValid(bool ok) => Modulate = ok ? new Color(0.8f, 1f, 0.8f, 0.65f) : new Color(1f, 0.55f, 0.55f, 0.65f);

    private bool Active => State is "open" or "on";
    private Color Line => Ui.Alpha(Ui.Ink, 0.8f);

    // ================= 그리기 =================
    public override void _Draw()
    {
        if (!_placeholder) return;
        switch (_shape)
        {
            case FurniPalette.Shape.Rug: DrawRug(); break;
            case FurniPalette.Shape.Table: DrawTable(); break;
            case FurniPalette.Shape.Chair: DrawChair(); break;
            case FurniPalette.Shape.Plant: DrawPlant(); break;
            case FurniPalette.Shape.Planter: DrawGiftPlanter(); break;
            case FurniPalette.Shape.Flower: DrawCutFlower(); break;
            case FurniPalette.Shape.Lamp: DrawLamp(); break;
            case FurniPalette.Shape.Can: DrawCan(); break;
            case FurniPalette.Shape.Postit: DrawPostit(); break;
            case FurniPalette.Shape.Window: DrawWindow(); break;
            case FurniPalette.Shape.Frame: DrawFrame(); break;
            case FurniPalette.Shape.Clock: DrawClock(); break;
            case FurniPalette.Shape.Panel: DrawWallPanel(); break;
            default: if (Wall) DrawWallPanel(); else DrawBox(); break;
        }
    }

    // ----- 바닥 -----
    private void DrawShadow(float rx = 0.72f)
    {
        float hw = Iso.TileW / 2f * rx, hh = Iso.TileH / 2f * rx;
        DrawColoredPolygon(new Vector2[] { new(0, -hh + 2), new(hw, 2), new(0, hh + 2), new(-hw, 2) }, new Color(0, 0, 0, 0.18f));
    }

    /// <summary>아이소 박스. dir 2/4 면 앞면에 문/패널을 넣어 방향을 보여준다.</summary>
    private void DrawBox()
    {
        const float s = 0.72f;
        float hw = Iso.TileW / 2f * s, hh = Iso.TileH / 2f * s;
        float e = 0, h = -_heightPx;

        var topC = Active ? Ui.AccentSoft.Lerp(_tint, 0.25f) : Ui.Lighten(_tint, 0.35f);
        DrawShadow();

        var left = new Vector2[] { new(-hw, h), new(0, h + hh), new(0, e + hh), new(-hw, e) };
        var right = new Vector2[] { new(0, h + hh), new(hw, h), new(hw, e), new(0, e + hh) };
        var top = new Vector2[] { new(0, h - hh), new(hw, h), new(0, h + hh), new(-hw, h) };
        DrawColoredPolygon(left, Ui.Darken(_tint, 0.45f));
        DrawColoredPolygon(right, Ui.Darken(_tint, 0.2f));
        DrawColoredPolygon(top, topC);

        var face = Dir switch { 2 => right, 4 => left, _ => null };
        if (face is not null && _heightPx >= 12)
        {
            var inset = Inset(face, 0.22f);
            DrawColoredPolygon(inset, Active ? Ui.Alpha(Ui.AccentSoft, 0.55f) : Ui.Alpha(Ui.Paper, 0.18f));
            DrawPolyline(Close(inset), Ui.Alpha(Ui.Ink, 0.35f), 1f);
        }
        DrawPolyline(Close(left), Line, 1f);
        DrawPolyline(Close(right), Line, 1f);
        DrawPolyline(Close(top), Line, 1f);
    }

    private void DrawRug()
    {
        var pts = Ellipse(0, Iso.TileH / 2f, Iso.TileW / 2f * 0.82f, Iso.TileH / 2f * 0.82f, 24);
        DrawColoredPolygon(pts, _tint);
        DrawPolyline(Loop(pts), Ui.Darken(_tint, 0.3f), 1.5f);
        var inner = Ellipse(0, Iso.TileH / 2f, Iso.TileW / 2f * 0.5f, Iso.TileH / 2f * 0.5f, 20);
        DrawPolyline(Loop(inner), Ui.Lighten(_tint, 0.35f), 1.5f);
    }

    private void DrawTable()
    {
        float rx = Iso.TileW / 2f * 0.62f, ry = Iso.TileH / 2f * 0.62f;
        float h = -_heightPx;
        DrawShadow(0.6f);
        foreach (float lx in new[] { -rx * 0.55f, 0f, rx * 0.55f })
            DrawLine(new Vector2(lx, ry * 0.4f), new Vector2(lx * 0.8f, h), Ui.Darken(_tint, 0.45f), 2.5f);
        var top = Ellipse(0, h, rx, ry, 24);
        DrawColoredPolygon(Shift(top, new Vector2(0, 3)), Ui.Darken(_tint, 0.4f));   // 상판 두께
        DrawColoredPolygon(top, Ui.Lighten(_tint, 0.3f));
        DrawPolyline(Loop(top), Line, 1f);
    }

    private void DrawChair()
    {
        const float s = 0.5f;
        float hw = Iso.TileW / 2f * s, hh = Iso.TileH / 2f * s;
        float seat = -_heightPx * 0.55f;
        DrawShadow(0.5f);
        foreach (var c in new[] { new Vector2(-hw, 0), new Vector2(hw, 0), new Vector2(0, hh), new Vector2(0, -hh) })
            DrawLine(c * 0.8f + new Vector2(0, hh * 0.4f), c * 0.8f + new Vector2(0, seat), Ui.Darken(_tint, 0.5f), 2f);

        var top = new Vector2[] { new(0, seat - hh), new(hw, seat), new(0, seat + hh), new(-hw, seat) };
        DrawColoredPolygon(Shift(top, new Vector2(0, 3)), Ui.Darken(_tint, 0.35f));
        DrawColoredPolygon(top, Ui.Lighten(_tint, 0.3f));
        DrawPolyline(Close(top), Line, 1f);

        // 등받이: 앉는 사람이 바라보는 반대쪽
        var back = Dir switch { 2 => new Vector2(-hw, seat), 4 => new Vector2(0, seat - hh), 6 => new Vector2(hw, seat), _ => new Vector2(0, seat + hh) };
        var up = new Vector2(0, -_heightPx * 0.6f);
        DrawLine(back, back + up, Ui.Darken(_tint, 0.25f), 3f);
        DrawLine(back + up, back + up + (back.X == 0 ? new Vector2(hw * 0.9f, 0) : new Vector2(0, hh * 0.9f)), Ui.Darken(_tint, 0.25f), 3f);
        DrawLine(back + up, back + up - (back.X == 0 ? new Vector2(hw * 0.9f, 0) : new Vector2(0, hh * 0.9f)), Ui.Darken(_tint, 0.25f), 3f);
    }

    /// <summary>
    /// 선물 화분 — **남들이 꽂아 준 캣닢**이 쌓인 만큼 잎이 늘어난다(시간이 아니라 사람이 채운다).
    /// 가득 차면 반짝임이 붙어 멀리서도 "이 집은 인기가 많다"가 보인다.
    /// </summary>
    private void DrawGiftPlanter()
    {
        DrawShadow(0.55f);
        float potH = _heightPx * 0.34f;
        var pot = new Vector2[] { new(-8, -potH), new(8, -potH), new(6, 0), new(-6, 0) };
        DrawColoredPolygon(pot, new Color("c07242"));
        DrawPolyline(Close(pot), Line, 1f);
        DrawLine(new Vector2(-8, -potH + 2.5f), new Vector2(8, -potH + 2.5f), Ui.Alpha(Ui.Ink, 0.28f), 2.5f);

        // 화분에 붙은 고양이 얼굴 (레퍼런스 시트의 그 화분)
        float fy = -potH * 0.42f;
        DrawColoredPolygon(Ellipse(0, fy, 4.6f, 3.6f, 14), new Color("f3dcc0"));
        DrawCircle(new Vector2(-1.7f, fy - 0.4f), 0.7f, Ui.Ink);
        DrawCircle(new Vector2(1.7f, fy - 0.4f), 0.7f, Ui.Ink);
        DrawArc(new Vector2(0, fy + 0.9f), 1.1f, 0.2f, Mathf.Pi - 0.2f, 8, Ui.Alpha(Ui.Ink, 0.7f), 0.9f);

        float soil = -potH + 1.5f;
        DrawColoredPolygon(Ellipse(0, soil, 6.5f, 2.2f, 14), new Color("5a3a24"));

        int leaves = State switch { "few" => 2, "half" => 4, "almost" => 7, "full" => 10, _ => 0 };
        if (leaves == 0) return;

        var stem = Ui.Darken(_tint, 0.35f);
        float top = -_heightPx * (0.5f + 0.05f * leaves);      // 많이 모일수록 높이 자란다
        DrawLine(new Vector2(0, soil), new Vector2(0, top), stem, 2f);
        for (int i = 0; i < leaves; i++)
        {
            float t = (i + 1) / (float)(leaves + 1);
            float y = Mathf.Lerp(soil - 2f, top, t);
            float side = (i % 2 == 0) ? -1f : 1f;
            Leaf(new Vector2(side * (3.5f + (i % 3)), y), 4.2f + i * 0.15f, 2.3f);
        }

        if (State == "full")                                    // 반짝임
        {
            var spark = new Color("f6e07a");
            foreach (var (sx, sy, r) in new[] { (-9f, top + 3f, 2.2f), (9f, top + 7f, 1.8f), (0f, top - 4f, 2.4f) })
            {
                DrawLine(new Vector2(sx - r, sy), new Vector2(sx + r, sy), spark, 1.2f);
                DrawLine(new Vector2(sx, sy - r), new Vector2(sx, sy + r), spark, 1.2f);
            }
        }
    }

    /// <summary>화분. 상태가 있으면 자라는 단계(씨앗 → 새싹 → 꽃봉오리 → 활짝)로, 없으면 잎이 무성한 관엽으로 그린다.</summary>
    private void DrawPlant()
    {
        DrawShadow(0.5f);
        float potH = _heightPx * 0.38f;
        var pot = new Vector2[] { new(-7, -potH), new(7, -potH), new(5, 0), new(-5, 0) };
        DrawColoredPolygon(pot, new Color("b0653c"));
        DrawPolyline(Close(pot), Line, 1f);
        DrawLine(new Vector2(-7, -potH + 2), new Vector2(7, -potH + 2), Ui.Alpha(Ui.Ink, 0.3f), 2f);

        float soil = -potH + 1.5f;
        var stem = Ui.Darken(_tint, 0.3f);

        switch (State)
        {
            case "seed":
                DrawColoredPolygon(Ellipse(0, soil, 6f, 2.2f, 14), new Color("5a3a24"));      // 흙
                DrawCircle(new Vector2(1.5f, soil - 0.5f), 1.4f, new Color("8a6a3a"));        // 씨앗
                return;

            case "sprout":
                DrawColoredPolygon(Ellipse(0, soil, 6f, 2.2f, 14), new Color("5a3a24"));
                DrawLine(new Vector2(0, soil), new Vector2(0, soil - 8), stem, 2f);
                Leaf(new Vector2(-4.5f, soil - 6.5f), 4f, 2.2f);
                Leaf(new Vector2(4.5f, soil - 8f), 4f, 2.2f);
                return;

            case "bud":
                DrawLine(new Vector2(0, soil), new Vector2(0, soil - 15), stem, 2f);
                Leaf(new Vector2(-5f, soil - 7f), 4.5f, 2.5f);
                Leaf(new Vector2(5f, soil - 10f), 4.5f, 2.5f);
                DrawColoredPolygon(Ellipse(0, soil - 17, 3.2f, 4.5f, 14), Ui.Darken(new Color("dc6a90"), 0.25f));
                DrawArc(new Vector2(0, soil - 17), 3.6f, 0, Mathf.Tau, 14, Ui.Alpha(Ui.Ink, 0.35f), 1f);
                return;

            case "bloom":
                DrawLine(new Vector2(0, soil), new Vector2(0, soil - 16), stem, 2f);
                Leaf(new Vector2(-5.5f, soil - 7f), 5f, 2.6f);
                Leaf(new Vector2(5.5f, soil - 10f), 5f, 2.6f);
                var center = new Vector2(0, soil - 19);
                for (int i = 0; i < 6; i++)                                                   // 꽃잎
                {
                    float a = Mathf.Tau * i / 6;
                    DrawColoredPolygon(Ellipse(center.X + Mathf.Cos(a) * 4f, center.Y + Mathf.Sin(a) * 4f, 3.4f, 3.4f, 12), new Color("f090b0"));
                }
                DrawCircle(center, 3f, new Color("f6e07a"));
                DrawArc(center, 3f, 0, Mathf.Tau, 14, Ui.Alpha(Ui.Ink, 0.4f), 1f);
                return;
        }

        // 상태 없는 관엽 화분
        DrawLine(new Vector2(0, soil), new Vector2(0, -_heightPx * 0.75f), stem, 2f);
        foreach (var (dx, dy, r) in new[] { (-6f, 0.72f, 6f), (6f, 0.78f, 5.5f), (0f, 0.95f, 6.5f), (-4f, 0.9f, 4.5f), (5f, 0.92f, 4.5f) })
        {
            var c = new Vector2(dx, -_heightPx * dy);
            DrawCircle(c, r, Ui.Lighten(_tint, dx < 0 ? 0.1f : 0.28f));
            DrawArc(c, r, 0, Mathf.Tau, 16, Ui.Alpha(Ui.Ink, 0.35f), 1f);
        }

    }

    /// <summary>잎 한 장. 화분 종류가 둘이라(자라는 화분·선물 화분) 공용으로 뺐다.</summary>
    private void Leaf(Vector2 at, float rx, float ry)
    {
        DrawColoredPolygon(Ellipse(at.X, at.Y, rx, ry, 12), Ui.Lighten(_tint, 0.2f));
        DrawArc(at, Mathf.Max(rx, ry), 0, Mathf.Tau, 12, Ui.Alpha(Ui.Ink, 0.25f), 1f);
    }

    /// <summary>꺾은 꽃 — 화분에서 수확한 것. 바닥에 놓는 작은 장식.</summary>
    private void DrawCutFlower()
    {
        DrawShadow(0.35f);
        float h = _heightPx;
        DrawLine(new Vector2(0, 0), new Vector2(-1.5f, -h * 0.65f), Ui.Darken(new Color("649658"), 0.2f), 1.8f);
        DrawColoredPolygon(Ellipse(4f, -h * 0.35f, 3.5f, 1.8f, 12), new Color("649658"));
        var center = new Vector2(-1.5f, -h * 0.75f);
        for (int i = 0; i < 5; i++)
        {
            float a = Mathf.Tau * i / 5 - Mathf.Pi / 2;
            DrawColoredPolygon(Ellipse(center.X + Mathf.Cos(a) * 3.2f, center.Y + Mathf.Sin(a) * 3.2f, 2.8f, 2.8f, 10), _tint);
        }
        DrawCircle(center, 2.4f, new Color("f6e07a"));
        DrawArc(center, 2.4f, 0, Mathf.Tau, 12, Ui.Alpha(Ui.Ink, 0.4f), 1f);
    }

    private void DrawLamp()
    {
        DrawShadow(0.45f);
        var baseC = Ui.Darken(_tint, 0.55f);
        DrawColoredPolygon(Ellipse(0, -2, 8, 3.5f, 14), baseC);
        DrawLine(new Vector2(0, -3), new Vector2(0, -_heightPx * 0.72f), baseC, 2.5f);

        float shadeTop = -_heightPx, shadeBot = -_heightPx * 0.7f;
        var shade = new Vector2[] { new(-6, shadeTop), new(6, shadeTop), new(10, shadeBot), new(-10, shadeBot) };
        if (Active)
        {
            DrawCircle(new Vector2(0, shadeBot + 2), 20, Ui.Alpha(Ui.AccentSoft, 0.16f));
            DrawCircle(new Vector2(0, shadeBot + 2), 12, Ui.Alpha(Ui.AccentSoft, 0.22f));
        }
        DrawColoredPolygon(shade, Active ? Ui.Lighten(_tint, 0.45f) : Ui.Darken(_tint, 0.15f));
        DrawPolyline(Close(shade), Line, 1f);
    }

    private void DrawCan()
    {
        DrawShadow(0.3f);
        float h = _heightPx;
        var body = new Vector2[] { new(-4, -h), new(4, -h), new(4, -1), new(-4, -1) };
        DrawColoredPolygon(body, State == "empty" ? Ui.Darken(_tint, 0.4f) : _tint);
        DrawColoredPolygon(Ellipse(0, -h, 4, 1.8f, 12), Ui.Lighten(_tint, 0.5f));
        DrawPolyline(Close(body), Line, 1f);
        DrawLine(new Vector2(-4, -h * 0.55f), new Vector2(4, -h * 0.55f), Ui.Alpha(Ui.Paper, 0.6f), 1.5f);
    }

    // ----- 벽 -----
    /// <summary>벽면 좌표계: (0,0) = 슬롯 중심, x 를 따라가면 벽 기울기만큼 y 가 변한다 (북쪽 +0.5, 서쪽 -0.5).</summary>
    private Vector2 W(float dx, float dy) => new(dx, dy + dx * (Dir == 4 ? 0.5f : -0.5f));

    /// <summary>벽걸이 일반형: 벽에서 방 쪽으로 살짝 튀어나온 상자(에어컨 등).</summary>
    private void DrawWallPanel()
    {
        float len = Iso.TileW / 2f * 0.42f, rise = Iso.TileH / 2f * 0.42f;
        float depth = 5f, ht = Mathf.Max(_heightPx, 10);
        float e = ht / 2f;
        var along = Dir == 4 ? new Vector2(len, rise) : new Vector2(len, -rise);
        var outward = Dir == 4 ? new Vector2(-depth * 0.5f, depth * 0.25f) : new Vector2(depth * 0.5f, depth * 0.25f);
        var up = new Vector2(0, ht);

        var frontC = Active ? Ui.AccentSoft.Lerp(_tint, 0.3f) : Ui.Lighten(_tint, 0.15f);
        var baseA = new Vector2(0, e) - along;
        var baseB = new Vector2(0, e) + along;
        var front = new Vector2[] { baseA + outward, baseB + outward, baseB + outward - up, baseA + outward - up };
        var side = Dir == 4
            ? new Vector2[] { baseA, baseA + outward, baseA + outward - up, baseA - up }
            : new Vector2[] { baseB, baseB + outward, baseB + outward - up, baseB - up };
        var topFace = new Vector2[] { baseA - up, baseB - up, baseB + outward - up, baseA + outward - up };

        DrawColoredPolygon(side, Ui.Darken(_tint, 0.4f));
        DrawColoredPolygon(topFace, Ui.Lighten(_tint, 0.4f));
        DrawColoredPolygon(front, frontC);
        var inset = Inset(front, 0.2f);
        DrawColoredPolygon(inset, Active ? Ui.Alpha(Ui.AccentSoft, 0.6f) : Ui.Alpha(Ui.Paper, 0.2f));
        DrawPolyline(Close(front), Line, 1f);
        DrawPolyline(Close(side), Line, 1f);
    }

    private void DrawWindow()
    {
        float r = _heightPx / 2f;
        var c = W(0, 0);
        DrawCircle(c, r + 2, new Color("6e5a48"));                       // 창틀
        DrawCircle(c, r, new Color("bfe4f2"));                            // 하늘
        DrawColoredPolygon(Ellipse(c.X, c.Y + r * 0.45f, r * 0.95f, r * 0.5f, 16), new Color("7fb6d8"));   // 바다
        DrawCircle(new Vector2(c.X - r * 0.35f, c.Y - r * 0.35f), r * 0.22f, new Color("fdf3c8"));         // 해
        DrawLine(W(-r, 0), W(r, 0), new Color("6e5a48"), 1.5f);
        DrawLine(W(0, -r), W(0, r), new Color("6e5a48"), 1.5f);
        DrawArc(c, r + 2, 0, Mathf.Tau, 24, Line, 1f);
    }

    private void DrawFrame()
    {
        float hw = 11f, hh = _heightPx / 2f;
        var outer = new[] { W(-hw, -hh), W(hw, -hh), W(hw, hh), W(-hw, hh) };
        DrawColoredPolygon(outer, new Color("8a6242"));
        var inner = Inset(outer, 0.18f);
        DrawColoredPolygon(inner, Ui.Lighten(_tint, 0.25f));
        // 사진: 언덕 두 개와 해
        DrawColoredPolygon(new[] { W(-7, hh * 0.45f), W(-1, -hh * 0.15f), W(5, hh * 0.45f) }, new Color("7a9e6a"));
        DrawColoredPolygon(new[] { W(0, hh * 0.45f), W(6, -hh * 0.05f), W(10, hh * 0.45f) }, new Color("5e8656"));
        DrawCircle(W(5, -hh * 0.4f), 2.2f, new Color("fdf3c8"));
        DrawPolyline(Close(outer), Line, 1f);
    }

    private void DrawClock()
    {
        float r = _heightPx / 2f;
        var c = W(0, 0);
        DrawCircle(c, r, Ui.Lighten(_tint, 0.2f));
        DrawArc(c, r, 0, Mathf.Tau, 20, Line, 1.2f);
        for (int i = 0; i < 12; i += 3)
        {
            float a = Mathf.Tau * i / 12 - Mathf.Pi / 2;
            DrawLine(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r * 0.72f), c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r * 0.9f), Ui.Alpha(Ui.Ink, 0.6f), 1f);
        }
        DrawLine(c, c + new Vector2(0, -r * 0.55f), Ui.Ink, 1.6f);        // 시침
        DrawLine(c, c + new Vector2(r * 0.7f, 0), Ui.Ink, 1.2f);          // 분침
        DrawCircle(c, 1.2f, Ui.Danger);
    }

    /// <summary>포스트잇: 벽면에 붙은 사각 메모지 + 테이프 + 접힌 귀퉁이 + 본문 첫 줄.</summary>
    private void DrawPostit()
    {
        float half = 8f;
        var shade = Ui.Darken(_tint, 0.18f);
        var quad = new[] { W(-half, -half), W(half, -half), W(half, half), W(-half, half) };
        DrawColoredPolygon(Shift(quad, new Vector2(1.5f, 2f)), new Color(0, 0, 0, 0.15f));   // 그림자
        DrawColoredPolygon(quad, _tint);
        DrawColoredPolygon(new[] { W(half - 4, half), W(half, half - 4), W(half, half) }, shade);   // 접힌 귀퉁이
        DrawPolyline(Close(quad), Ui.Alpha(Ui.Ink, 0.55f), 1f);
        DrawColoredPolygon(new[] { W(-3, -half - 2), W(3, -half - 2), W(3, -half + 2), W(-3, -half + 2) }, Ui.Alpha(Ui.Ink, 0.3f));   // 테이프

        if (Body.Length > 0 && !IsGhost)
        {
            var font = NameLabel.GetThemeFont("font");
            string first = Body.Split('\n')[0];
            if (first.Length > 5) first = first[..5] + "…";
            var at = W(-half + 1.5f, -half + 7);
            DrawSetTransform(at, Mathf.Atan(Dir == 4 ? 0.5f : -0.5f), Vector2.One);
            DrawString(font, Vector2.Zero, first, HorizontalAlignment.Left, 2 * half - 2, 6, Ui.Alpha(Ui.Ink, 0.85f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        else if (!IsGhost)
        {
            for (int i = 0; i < 3; i++)   // 빈 메모지는 줄만
                DrawLine(W(-half + 2, -half + 5 + i * 4), W(half - 2 - i * 2, -half + 5 + i * 4), Ui.Alpha(Ui.Ink, 0.18f), 1f);
        }
    }

    // ----- 도형 헬퍼 -----
    private static Vector2[] Close(Vector2[] p) => new[] { p[0], p[1], p[2], p[3], p[0] };
    private static Vector2[] Loop(Vector2[] p)
    {
        var r = new Vector2[p.Length + 1];
        p.CopyTo(r, 0); r[^1] = p[0];
        return r;
    }
    private static Vector2[] Shift(Vector2[] p, Vector2 d)
    {
        var r = new Vector2[p.Length];
        for (int i = 0; i < p.Length; i++) r[i] = p[i] + d;
        return r;
    }
    private static Vector2[] Inset(Vector2[] p, float f)
    {
        var c = Vector2.Zero;
        foreach (var v in p) c += v;
        c /= p.Length;
        var r = new Vector2[p.Length];
        for (int i = 0; i < p.Length; i++) r[i] = p[i].Lerp(c, f);
        return r;
    }
    private static Vector2[] Ellipse(float cx, float cy, float rx, float ry, int n)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++) { float a = Mathf.Tau * i / n; pts[i] = new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry); }
        return pts;
    }
}
