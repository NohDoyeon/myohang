using Godot;

namespace HarborClient;

/// <summary>
/// 팝플 UI 디자인 토큰 + 위젯 팩토리. 색은 art/palette48.json 램프에서 뽑았다.
/// HUD 는 전부 코드로 조립한다(손으로 쓴 .tscn 의 export 미주입 문제 회피, 토큰 한 곳에서 관리).
/// </summary>
public static class Ui
{
    // ----- 토큰 -----
    public static readonly Color Ink = new("1a141c");         // 글자·외곽선
    public static readonly Color Paper = new("faf6ec");       // 밝은 글자·말풍선
    public static readonly Color Muted = new("8c7686");       // 보조 글자
    public static readonly Color Line = new("5c4c60");        // 패널 테두리
    public static readonly Color Accent = new("e6b432");      // 강조(노랑)
    public static readonly Color AccentSoft = new("fae68c");
    public static readonly Color Sea = new("58789f");         // 바닥 파랑
    public static readonly Color Leaf = new("649658");        // 가능(초록)
    public static readonly Color Danger = new("dc5448");      // 오류(빨강)
    public static readonly Color Panel = new(0.10f, 0.08f, 0.11f, 0.90f);
    public static readonly Color PanelSoft = new(0.10f, 0.08f, 0.11f, 0.60f);
    public static readonly Color ButtonBg = new("3a2e3e");

    public static readonly Color[] ShirtColors =
    {
        new("dc5448"), new("58789f"), new("649658"), new("e6b432"),
        new("a064be"), new("c86e8c"), new("50aab4"), new("ac7850"),
    };
    public static readonly Color[] HairColors =
    {
        new("1a141c"), new("462a1e"), new("784c30"), new("a03030"), new("e6b432"), new("3a4868"),
        new("a064be"), new("50aab4"), new("c86e8c"), new("e4e4e4"),
    };
    public static readonly Color[] PantsColors =
    {
        new("3a4868"), new("2e2a34"), new("6e4a34"), new("4a5a44"), new("7a5a6a"), new("8a8a8a"),
    };
    /// <summary>털색 — 고양이 아바타의 바탕. figure 의 `hd` 팔레트가 가리킨다.</summary>
    public static readonly Color[] FurColors =
    {
        new("f2e4d2"), // 크림
        new("e8b878"), // 치즈
        new("d88a48"), // 주황 태비
        new("b08050"), // 갈색
        new("9a8e88"), // 회색
        new("6a5e5a"), // 진회색
        new("3c343c"), // 검정
        new("fbf7ef"), // 흰색
    };
    /// <summary>무늬색 — 턱시도·얼룩·젖소 무늬에 쓰인다. figure 의 `hr` 팔레트.</summary>
    public static readonly Color[] PatternColors =
    {
        new("fbf7ef"), new("3c343c"), new("c87848"), new("9a8e88"), new("e8b878"), new("a06a4a"),
    };
    /// <summary>모자 색은 상의 팔레트를 공유한다.</summary>
    public static Color[] HatColors => ShirtColors;
    public static readonly Color Fur = new("e8b878");
    public static readonly Color EarInner = new("e8a0a8");

    /// <summary>팔레트 인덱스는 figure 문자열에서 1부터 센다. 범위를 벗어나면 순환.</summary>
    public static Color Pick(Color[] arr, int oneBased)
        => arr[((oneBased - 1) % arr.Length + arr.Length) % arr.Length];

    // ----- 유틸 -----
    public static Color Lighten(Color c, float f) => c.Lerp(Colors.White, f);
    public static Color Darken(Color c, float f) => c.Lerp(Ink, f);
    public static Color Alpha(Color c, float a) => new(c, a);

    /// <summary>프로세스마다 달라지는 string.GetHashCode 대신 FNV-1a (모든 클라가 같은 색을 보도록).</summary>
    public static uint Hash(string s)
    {
        uint h = 2166136261;
        foreach (char ch in s) { h ^= ch; h *= 16777619; }
        return h;
    }
    public static Color ColorFromHash(string s) => ShirtColors[Hash(s) % (uint)ShirtColors.Length];

    // ----- 스타일 -----
    public static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 8, float pad = 8, float padX = -1, int borderWidth = 1)
    {
        var sb = new StyleBoxFlat { BgColor = bg };
        sb.SetCornerRadiusAll(radius);
        sb.ContentMarginTop = pad; sb.ContentMarginBottom = pad;
        sb.ContentMarginLeft = padX < 0 ? pad : padX; sb.ContentMarginRight = padX < 0 ? pad : padX;
        if (border is { } b) { sb.BorderColor = b; sb.SetBorderWidthAll(borderWidth); }
        return sb;
    }

    /// <summary>색 견본 버튼. 고른 것은 테두리가 굵어진다.</summary>
    public static Button Swatch(Color color, bool selected)
    {
        var b = new Button { CustomMinimumSize = new Vector2(26, 26), FocusMode = Control.FocusModeEnum.None };
        var border = selected ? Accent : Alpha(Ink, 0.55f);
        int w = selected ? 3 : 1;
        b.AddThemeStyleboxOverride("normal", Box(color, border, 6, 0, 0, w));
        b.AddThemeStyleboxOverride("hover", Box(Lighten(color, 0.18f), Accent, 6, 0, 0, w));
        b.AddThemeStyleboxOverride("pressed", Box(Darken(color, 0.2f), Accent, 6, 0, 0, 3));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return b;
    }

    // ----- 위젯 -----
    public static Label Text(string text, int size = 15, Color? color = null, bool center = false)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Paper);
        if (center) l.HorizontalAlignment = HorizontalAlignment.Center;
        return l;
    }

    public static PanelContainer Card(Control child, Color? bg = null, Color? border = null, int radius = 8, float pad = 8)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        p.AddThemeStyleboxOverride("panel", Box(bg ?? Panel, border, radius, pad));
        p.AddChild(child);
        return p;
    }

    public static Button Button(string text, bool primary = false)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34) };
        var bg = primary ? Accent : ButtonBg;
        var fg = primary ? Ink : Paper;
        b.AddThemeStyleboxOverride("normal", Box(bg, null, 8, 6, 12));
        b.AddThemeStyleboxOverride("hover", Box(Lighten(bg, 0.12f), null, 8, 6, 12));
        b.AddThemeStyleboxOverride("pressed", Box(Darken(bg, 0.25f), AccentSoft, 8, 6, 12));
        b.AddThemeStyleboxOverride("hover_pressed", Box(Darken(bg, 0.15f), AccentSoft, 8, 6, 12));
        b.AddThemeStyleboxOverride("disabled", Box(Alpha(bg, 0.4f), null, 8, 6, 12));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeColorOverride("font_color", fg);
        b.AddThemeColorOverride("font_hover_color", fg);
        b.AddThemeColorOverride("font_pressed_color", primary ? Ink : AccentSoft);
        b.AddThemeColorOverride("font_hover_pressed_color", primary ? Ink : AccentSoft);
        b.AddThemeColorOverride("font_disabled_color", Alpha(fg, 0.5f));
        return b;
    }

    public static LineEdit Input(string placeholder, int maxLength, float minWidth = 0)
    {
        var e = new LineEdit { PlaceholderText = placeholder, MaxLength = maxLength, CustomMinimumSize = new Vector2(minWidth, 36) };
        e.AddThemeStyleboxOverride("normal", Box(new Color(0, 0, 0, 0.35f), Line, 8, 6, 10));
        e.AddThemeStyleboxOverride("focus", Box(new Color(0, 0, 0, 0.45f), Accent, 8, 6, 10));
        e.AddThemeColorOverride("font_color", Paper);
        e.AddThemeColorOverride("font_placeholder_color", Muted);
        e.AddThemeColorOverride("caret_color", Accent);
        return e;
    }
}
