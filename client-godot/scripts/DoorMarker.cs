using Godot;
using Harbor.Core;

namespace HarborClient;

/// <summary>
/// 방의 문. 원작처럼 "어디로든 문" 느낌의 자립형 문으로 그린다 — 누르면 방 목록이 열린다.
/// 원점(0,0) = 문이 서 있는 타일의 중심(발 닿는 곳).
/// </summary>
public partial class DoorMarker : Node2D
{
    private const float HalfW = 13f;
    private const float Height = 42f;
    private static readonly Color FrameC = new("6e4a34");
    private static readonly Color PanelC = new("a8cfe0");
    private static readonly Color PanelLit = new("d8f0fa");

    private float _t;
    private bool _hover;

    public void SetHover(bool on)
    {
        if (_hover == on) return;
        _hover = on; QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_hover) QueueRedraw();
    }

    public override void _Draw()
    {
        float arch = HalfW;                    // 윗부분 반원 반지름
        float bodyTop = -Height + arch;

        // 바닥 그림자
        DrawColoredPolygon(Ellipse(0, -1, HalfW + 3, 5f, 16), new Color(0, 0, 0, 0.2f));

        // 문틀: 기둥 + 아치
        var frame = Arch(HalfW + 3, Height + 3, arch + 3);
        DrawColoredPolygon(frame, FrameC);
        DrawPolyline(Loop(frame), Ui.Alpha(Ui.Ink, 0.8f), 1f);

        // 문짝: 반짝이는 하늘색 판 (다른 방으로 통하는 느낌)
        float glow = _hover ? 0.5f + 0.5f * Mathf.Sin(_t * 5f) : 0.25f;
        var panel = Arch(HalfW, Height, arch);
        DrawColoredPolygon(panel, PanelC.Lerp(PanelLit, glow));
        DrawPolyline(Loop(panel), Ui.Alpha(Ui.Ink, 0.45f), 1f);

        // 세로 하이라이트 두 줄 + 손잡이
        DrawLine(new Vector2(-HalfW * 0.45f, -4), new Vector2(-HalfW * 0.45f, bodyTop), Ui.Alpha(Ui.Paper, 0.45f), 1.5f);
        DrawLine(new Vector2(HalfW * 0.15f, -4), new Vector2(HalfW * 0.15f, bodyTop - 4), Ui.Alpha(Ui.Paper, 0.25f), 1f);
        DrawCircle(new Vector2(HalfW * 0.62f, -Height * 0.42f), 2.2f, Ui.Accent);
    }

    /// <summary>기둥(사각) + 반원 지붕 폴리곤. 아래가 y=0, 위가 y=-h.</summary>
    private static Vector2[] Arch(float halfW, float h, float r)
    {
        var pts = new List<Vector2> { new(-halfW, 0), new(halfW, 0), new(halfW, -h + r) };
        const int n = 12;
        for (int i = 0; i <= n; i++)
        {
            float a = -Mathf.Pi * i / n;                 // 0 → -π (오른쪽에서 왼쪽으로)
            pts.Add(new Vector2(Mathf.Cos(a) * halfW, -h + r + Mathf.Sin(a) * r));
        }
        pts.Add(new Vector2(-halfW, -h + r));
        return pts.ToArray();
    }

    private static Vector2[] Loop(Vector2[] p)
    {
        var r = new Vector2[p.Length + 1];
        p.CopyTo(r, 0); r[^1] = p[0];
        return r;
    }

    private static Vector2[] Ellipse(float cx, float cy, float rx, float ry, int n)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++) { float a = Mathf.Tau * i / n; pts[i] = new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry); }
        return pts;
    }
}
