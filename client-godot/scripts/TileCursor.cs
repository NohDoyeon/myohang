using Godot;
using Harbor.Core;

namespace HarborClient;

/// <summary>마우스 아래 타일 하이라이트(다이아몬드). Position = 타일 꼭짓점(Iso.ToScreen(x, y, h)).</summary>
public partial class TileCursor : Node2D
{
    private Color _color = Ui.AccentSoft;
    private Vector2[]? _shape;   // null = 바닥 다이아몬드, 아니면 임의 사각형(벽 슬롯 등, 로컬 좌표)

    public void SetColor(Color c)
    {
        if (_color == c) return;
        _color = c; QueueRedraw();
    }

    public void SetShape(Vector2[]? shape) { _shape = shape; QueueRedraw(); }

    public override void _Draw()
    {
        var pts = _shape ?? new Vector2[]
        {
            new(0, 0), new(Iso.TileW / 2f, Iso.TileH / 2f), new(0, Iso.TileH), new(-Iso.TileW / 2f, Iso.TileH / 2f),
        };
        DrawColoredPolygon(pts, Ui.Alpha(_color, 0.30f));
        DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[0] }, _color, 1.5f);
    }
}
