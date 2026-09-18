using Godot;

namespace HarborClient;

/// <summary>
/// OS IME 를 쓰지 않는 한글 입력 상자 (두벌식 오토마타). Godot 창에서 Windows IME 조합이 깨지는 문제(자모가 낱개로 커밋됨)를 우회한다.
/// - 키코드(QWERTY 자리)로 자모를 받아 직접 조합. 한/영 전환: 오른쪽 Alt, Shift+Space, 또는 오른쪽 배지 클릭.
/// - Enter = 제출(Submitted), ESC = 포커스 해제, Backspace = 자모 단위 지우기, Ctrl+V = 붙여넣기.
/// - 캐럿은 항상 끝(채팅용 단순화). Multiline 이면 줄바꿈 그리기만 하고 Enter 는 그대로 제출.
/// </summary>
public partial class HangulInput : Control
{
    public event Action<string>? Submitted;
    public event Action<string>? Changed;

    public string Placeholder { get; set; } = "";
    public int MaxLength { get; set; } = 120;
    public bool Multiline { get; set; }
    public bool Korean { get; set; } = true;
    public int FontSize { get; set; } = 15;
    /// <summary>비밀번호 입력 — 화면에는 ● 로만 보인다(Value 는 그대로).</summary>
    public bool Secret { get; set; }

    /// <summary>조합 중인 글자까지 포함한 현재 값.</summary>
    public string Value => _text + (_composing ? ComposedChar().ToString() : "");

    private string _text = "";
    private bool _composing; private int _cho = -1, _jung = -1, _jong = 0;
    private StyleBoxFlat _box = null!, _focusBox = null!;

    // ----- 자모 표 -----
    private const string Cho = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
    private const string Jung = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
    private const string Jong = "\0ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";
    private static readonly Dictionary<Key, (char normal, char shift)> KeyMap = new()
    {
        [Key.Q] = ('ㅂ', 'ㅃ'), [Key.W] = ('ㅈ', 'ㅉ'), [Key.E] = ('ㄷ', 'ㄸ'), [Key.R] = ('ㄱ', 'ㄲ'), [Key.T] = ('ㅅ', 'ㅆ'),
        [Key.Y] = ('ㅛ', 'ㅛ'), [Key.U] = ('ㅕ', 'ㅕ'), [Key.I] = ('ㅑ', 'ㅑ'), [Key.O] = ('ㅐ', 'ㅒ'), [Key.P] = ('ㅔ', 'ㅖ'),
        [Key.A] = ('ㅁ', 'ㅁ'), [Key.S] = ('ㄴ', 'ㄴ'), [Key.D] = ('ㅇ', 'ㅇ'), [Key.F] = ('ㄹ', 'ㄹ'), [Key.G] = ('ㅎ', 'ㅎ'),
        [Key.H] = ('ㅗ', 'ㅗ'), [Key.J] = ('ㅓ', 'ㅓ'), [Key.K] = ('ㅏ', 'ㅏ'), [Key.L] = ('ㅣ', 'ㅣ'),
        [Key.Z] = ('ㅋ', 'ㅋ'), [Key.X] = ('ㅌ', 'ㅌ'), [Key.C] = ('ㅊ', 'ㅊ'), [Key.V] = ('ㅍ', 'ㅍ'), [Key.B] = ('ㅠ', 'ㅠ'),
        [Key.N] = ('ㅜ', 'ㅜ'), [Key.M] = ('ㅡ', 'ㅡ'),
    };
    private static readonly Dictionary<(char, char), char> VowelPair = new()
    {
        [('ㅗ', 'ㅏ')] = 'ㅘ', [('ㅗ', 'ㅐ')] = 'ㅙ', [('ㅗ', 'ㅣ')] = 'ㅚ',
        [('ㅜ', 'ㅓ')] = 'ㅝ', [('ㅜ', 'ㅔ')] = 'ㅞ', [('ㅜ', 'ㅣ')] = 'ㅟ', [('ㅡ', 'ㅣ')] = 'ㅢ',
    };
    private static readonly Dictionary<(char, char), char> FinalPair = new()
    {
        [('ㄱ', 'ㅅ')] = 'ㄳ', [('ㄴ', 'ㅈ')] = 'ㄵ', [('ㄴ', 'ㅎ')] = 'ㄶ', [('ㄹ', 'ㄱ')] = 'ㄺ', [('ㄹ', 'ㅁ')] = 'ㄻ',
        [('ㄹ', 'ㅂ')] = 'ㄼ', [('ㄹ', 'ㅅ')] = 'ㄽ', [('ㄹ', 'ㅌ')] = 'ㄾ', [('ㄹ', 'ㅍ')] = 'ㄿ', [('ㄹ', 'ㅎ')] = 'ㅀ', [('ㅂ', 'ㅅ')] = 'ㅄ',
    };
    private static readonly Dictionary<char, (char, char)> Split = BuildSplit();
    private static Dictionary<char, (char, char)> BuildSplit()
    {
        var d = new Dictionary<char, (char, char)>();
        foreach (var kv in VowelPair) d[kv.Value] = kv.Key;
        foreach (var kv in FinalPair) d[kv.Value] = kv.Key;
        return d;
    }

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.Ibeam;
        _box = Ui.Box(new Color(0, 0, 0, 0.35f), Ui.Line, 8, 6, 10);
        _focusBox = Ui.Box(new Color(0, 0, 0, 0.45f), Ui.Accent, 8, 6, 10);
        if (CustomMinimumSize == Vector2.Zero) CustomMinimumSize = new Vector2(0, Multiline ? 130 : 36);
    }

    public string Text
    {
        get => Value;
        set { _text = value ?? ""; ResetComposition(); QueueRedraw(); }
    }
    public void Clear() => Text = "";

    // ---------------- 입력 ----------------
    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (!Secret && mb.Position.X > Size.X - 34) Korean = !Korean;   // 오른쪽 한/영 배지
            GrabFocus(); QueueRedraw(); AcceptEvent(); return;
        }
        if (e is not InputEventKey k || !k.Pressed) return;

        switch (k.Keycode)
        {
            case Key.Enter or Key.KpEnter:
                Commit(); Submitted?.Invoke(_text); AcceptEvent(); return;
            case Key.Escape:
                Commit(); ReleaseFocus(); AcceptEvent(); return;
            case Key.Backspace:
                Backspace(); AcceptEvent(); return;
            case Key.Alt when k.Location == KeyLocation.Right && !Secret:
            case Key.Space when k.ShiftPressed && !Secret:
                Korean = !Korean; QueueRedraw(); AcceptEvent(); return;
            case Key.V when k.CtrlPressed:
                Commit(); AppendText(DisplayServer.ClipboardGet()); AcceptEvent(); return;
        }
        if (k.CtrlPressed || k.AltPressed || k.MetaPressed) return;

        if (Korean && KeyMap.TryGetValue(k.Keycode, out var jamo))
        {
            FeedJamo(k.ShiftPressed ? jamo.shift : jamo.normal);
            AcceptEvent(); return;
        }
        if (k.Unicode >= 32 && k.Unicode != 127)
        {
            Commit(); AppendText(((char)k.Unicode).ToString());
            AcceptEvent();
        }
    }

    private void AppendText(string s)
    {
        foreach (char c in s)
        {
            if (c == '\r' || (c == '\n' && !Multiline)) continue;
            if (_text.Length >= MaxLength) break;
            _text += c;
        }
        Notify();
    }

    private void Notify() { Changed?.Invoke(Value); QueueRedraw(); }

    // ---------------- 오토마타 ----------------
    private static bool IsVowel(char j) => Jung.IndexOf(j) >= 0;

    private void FeedJamo(char j)
    {
        if (!_composing && _text.Length >= MaxLength) return;
        if (IsVowel(j)) FeedVowel(j); else FeedConsonant(j);
        Notify();
    }

    private void FeedConsonant(char c)
    {
        if (!_composing) { Start(c); return; }
        if (_jung < 0) { Commit(); Start(c); return; }                       // 초성만 있었음 → 커밋 후 새 초성
        if (_jong == 0)
        {
            int jongIdx = Jong.IndexOf(c);
            if (jongIdx > 0) { _jong = jongIdx; return; }                    // 받침
            Commit(); Start(c); return;
        }
        if (FinalPair.TryGetValue((Jong[_jong], c), out var pair)) { _jong = Jong.IndexOf(pair); return; }   // 겹받침
        Commit(); Start(c);
    }

    private void FeedVowel(char v)
    {
        if (!_composing) { StartVowel(v); return; }
        if (_cho < 0)                                                         // 모음만 조합 중
        {
            if (VowelPair.TryGetValue((Jung[_jung], v), out var vv)) { _jung = Jung.IndexOf(vv); return; }
            Commit(); StartVowel(v); return;
        }
        if (_jung < 0) { _jung = Jung.IndexOf(v); return; }                  // 초성 + 중성
        if (_jong == 0)
        {
            if (VowelPair.TryGetValue((Jung[_jung], v), out var vv)) { _jung = Jung.IndexOf(vv); return; }   // 복모음
            Commit(); StartVowel(v); return;
        }
        // 받침이 다음 글자 초성으로 넘어감 (겹받침이면 뒤 자모만)
        char jong = Jong[_jong]; char carry;
        if (Split.TryGetValue(jong, out var parts)) { _jong = Jong.IndexOf(parts.Item1); carry = parts.Item2; }
        else { _jong = 0; carry = jong; }
        Commit();
        Start(carry); _jung = Jung.IndexOf(v);
    }

    private void Start(char c) { _composing = true; _cho = Cho.IndexOf(c); _jung = -1; _jong = 0; if (_cho < 0) { _composing = false; _text += c; } }
    private void StartVowel(char v) { _composing = true; _cho = -1; _jung = Jung.IndexOf(v); _jong = 0; }
    private void ResetComposition() { _composing = false; _cho = -1; _jung = -1; _jong = 0; }

    private char ComposedChar()
    {
        if (_cho >= 0 && _jung >= 0) return (char)(0xAC00 + (_cho * 21 + _jung) * 28 + _jong);
        if (_cho >= 0) return Cho[_cho];
        return Jung[_jung];
    }

    private void Commit()
    {
        if (!_composing) return;
        if (_text.Length < MaxLength) _text += ComposedChar();
        ResetComposition();
    }

    private void Backspace()
    {
        if (_composing)
        {
            if (_jong > 0) { _jong = Split.TryGetValue(Jong[_jong], out var p) ? Jong.IndexOf(p.Item1) : 0; }
            else if (_jung >= 0 && Split.TryGetValue(Jung[_jung], out var vp)) _jung = Jung.IndexOf(vp.Item1);
            else if (_jung >= 0 && _cho >= 0) _jung = -1;
            else ResetComposition();
        }
        else if (_text.Length > 0) _text = _text[..^1];
        Notify();
    }

    // ---------------- 그리기 ----------------
    public override void _Notification(int what)
    {
        if (what == NotificationFocusEnter || what == NotificationFocusExit) QueueRedraw();
    }

    public override void _Draw()
    {
        bool focused = HasFocus();
        var sb = focused ? _focusBox : _box;
        DrawStyleBox(sb, new Rect2(Vector2.Zero, Size));
        var font = GetThemeFont("font");
        float left = sb.ContentMarginLeft, top = sb.ContentMarginTop;
        float innerW = Size.X - left - sb.ContentMarginRight - 30;   // 오른쪽 한/영 배지 자리
        float innerH = Size.Y - top - sb.ContentMarginBottom;
        float ascent = font.GetAscent(FontSize), descent = font.GetDescent(FontSize);

        // 한/영 배지 (비밀번호 칸은 영문 고정이라 표시하지 않는다)
        if (!Secret)
            DrawString(font, new Vector2(Size.X - 26, (Size.Y + ascent - descent) / 2f), Korean ? "한" : "A", HorizontalAlignment.Left, -1, 12, Korean ? Ui.Accent : Ui.Muted);

        string committed = Secret ? new string('●', _text.Length) : _text;
        string comp = _composing ? (Secret ? "●" : ComposedChar().ToString()) : "";
        string caret = focused ? "|" : "";
        if (committed.Length == 0 && comp.Length == 0)
        {
            if (!focused && Placeholder.Length > 0)
                DrawString(font, new Vector2(left, Multiline ? top + ascent : (Size.Y + ascent - descent) / 2f), Placeholder, HorizontalAlignment.Left, innerW, FontSize, Ui.Muted);
            else if (focused)
                DrawString(font, new Vector2(left, Multiline ? top + ascent : (Size.Y + ascent - descent) / 2f), caret, HorizontalAlignment.Left, -1, FontSize, Ui.Paper);
            return;
        }

        if (Multiline)
        {
            DrawMultilineString(font, new Vector2(left, top + ascent), committed + comp + caret, HorizontalAlignment.Left, innerW, FontSize, -1, Ui.Paper);
            return;
        }

        // 한 줄: 넘치면 앞을 잘라 끝이 보이게
        string shown = committed;
        float compW = comp.Length > 0 ? font.GetStringSize(comp, HorizontalAlignment.Left, -1, FontSize).X : 0;
        float caretW = font.GetStringSize("|", HorizontalAlignment.Left, -1, FontSize).X;
        while (shown.Length > 0 && font.GetStringSize(shown, HorizontalAlignment.Left, -1, FontSize).X + compW + caretW > innerW) shown = shown[1..];
        float baseline = (Size.Y + ascent - descent) / 2f;
        float x = left;
        DrawString(font, new Vector2(x, baseline), shown, HorizontalAlignment.Left, -1, FontSize, Ui.Paper);
        x += font.GetStringSize(shown, HorizontalAlignment.Left, -1, FontSize).X;
        if (comp.Length > 0)
        {
            DrawString(font, new Vector2(x, baseline), comp, HorizontalAlignment.Left, -1, FontSize, Ui.AccentSoft);
            DrawLine(new Vector2(x, baseline + descent), new Vector2(x + compW, baseline + descent), Ui.AccentSoft, 1f);   // 조합 중 밑줄
            x += compW;
        }
        if (focused) DrawString(font, new Vector2(x, baseline), caret, HorizontalAlignment.Left, -1, FontSize, Ui.Paper);
    }
}
