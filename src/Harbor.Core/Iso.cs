namespace Harbor.Core;

/// <summary>2:1 아이소메트릭 투영. 엔진 비종속.</summary>
public static class Iso
{
    public const int TileW = 64;
    public const int TileH = 32;
    public const int StackPx = 8;

    public static (float sx, float sy) ToScreen(float x, float y, float z)
        => ((x - y) * (TileW / 2f), (x + y) * (TileH / 2f) - z * StackPx);

    public static (int x, int y) ToWorld(float sx, float sy)
    {
        float fx = (sx / (TileW / 2f) + sy / (TileH / 2f)) / 2f;
        float fy = (sy / (TileH / 2f) - sx / (TileW / 2f)) / 2f;
        return ((int)MathF.Floor(fx), (int)MathF.Floor(fy));
    }

    /// <summary>렌더 정렬 키. 클수록 앞.</summary>
    public static int DepthKey(int x, int y, float z, int layer = 0)
        => (x + y) * 1000 + (int)(z * 10) + layer;

    /// <summary>from→to 인접 이동의 방향(0=북, 시계방향 0~7).</summary>
    public static int DirectionBetween((int x, int y) from, (int x, int y) to)
    {
        int dx = Math.Sign(to.x - from.x), dy = Math.Sign(to.y - from.y);
        return (dx, dy) switch
        {
            (0, -1) => 0, (1, -1) => 1, (1, 0) => 2, (1, 1) => 3,
            (0, 1) => 4, (-1, 1) => 5, (-1, 0) => 6, (-1, -1) => 7,
            _ => 4
        };
    }
}
