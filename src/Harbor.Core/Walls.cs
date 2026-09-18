namespace Harbor.Core;

/// <summary>
/// 룸의 벽 위치(서버 배치 판정·클라 렌더 공용).
/// 2:1 아이소에서 화면 위쪽(뒤쪽)에 보이는 벽만 만든다:
///  - 서쪽 벽(West): 타일의 좌상단 모서리(-x 방향). 벽이 가리는 영역(x' ≤ x-1, y' ≤ y)에 걸을 수 있는 타일이 하나도 없을 때.
///  - 북쪽 벽(North): 타일의 우상단 모서리(-y 방향). 영역(x' ≤ x, y' ≤ y-1)에 걸을 수 있는 타일이 없을 때.
/// 문 타일처럼 앞쪽에 튀어나온 타일은 자동으로 벽이 생기지 않는다(다른 타일을 가리게 되므로).
/// 벽걸이 가구: dir 4 = 북쪽 벽, dir 2 = 서쪽 벽.
/// </summary>
public sealed class Walls
{
    private readonly Heightmap _m;
    private readonly bool[,] _any;   // [x+1, y+1] = (0..x, 0..y) 사각형 안에 걸을 수 있는 타일이 있는가 (2D prefix)

    public Walls(Heightmap m)
    {
        _m = m;
        _any = new bool[m.W + 1, m.H + 1];
        for (int y = 0; y < m.H; y++)
            for (int x = 0; x < m.W; x++)
                _any[x + 1, y + 1] = m.Walkable(x, y) || _any[x, y + 1] || _any[x + 1, y];
    }

    private bool AnyWalkable(int x, int y) => x >= 0 && y >= 0 && _any[Math.Min(x, _m.W - 1) + 1, Math.Min(y, _m.H - 1) + 1];

    public bool West(int x, int y) => _m.Walkable(x, y) && !AnyWalkable(x - 1, y);
    public bool North(int x, int y) => _m.Walkable(x, y) && !AnyWalkable(x, y - 1);

    /// <summary>벽걸이 가구를 (x,y)에 dir 방향으로 붙일 수 있는가.</summary>
    public bool CanHang(int x, int y, int dir) => dir switch { 4 => North(x, y), 2 => West(x, y), _ => false };

    /// <summary>벽 슬롯 그리드: 타일 모서리 하나를 가로 SlotCols 칸, 세로는 벽 높이(층) 칸으로 나눈다.</summary>
    public const int SlotCols = 2;
    public static bool SlotValid(int u, int v, int wallHeight) => u >= 0 && u < SlotCols && v >= 0 && v < Math.Max(1, wallHeight);

    /// <summary>기울기 허용 범위(도). 벽에 붙이는 물건이 삐뚜름해 보이는 정도.</summary>
    public const int MaxTilt = 20;
    public static sbyte ClampTilt(int deg) => (sbyte)Math.Clamp(deg, -MaxTilt, MaxTilt);
}
