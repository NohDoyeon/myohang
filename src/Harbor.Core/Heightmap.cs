namespace Harbor.Core;

/// <summary>문자열 행 기반 룸 높이맵. 'x' = 이동불가, '0'~'9','a'~'z' = 높이.</summary>
public sealed class Heightmap
{
    public int W { get; }
    public int H { get; }
    private readonly float[,] _h;
    private readonly bool[,] _walk;

    public Heightmap(IReadOnlyList<string> rows)
    {
        if (rows.Count == 0) throw new ArgumentException("empty heightmap");
        H = rows.Count;
        W = rows[0].Length;
        _h = new float[W, H];
        _walk = new bool[W, H];
        for (int y = 0; y < H; y++)
        {
            if (rows[y].Length != W) throw new ArgumentException($"row {y} width mismatch");
            for (int x = 0; x < W; x++)
            {
                char c = char.ToLowerInvariant(rows[y][x]);
                _walk[x, y] = c != 'x';
                _h[x, y] = c switch
                {
                    'x' => 0f,
                    >= '0' and <= '9' => c - '0',
                    >= 'a' and <= 'z' => c - 'a' + 10,
                    _ => throw new ArgumentException($"bad tile char '{c}' at {x},{y}")
                };
            }
        }
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
    public bool Walkable(int x, int y) => InBounds(x, y) && _walk[x, y];
    public float Height(int x, int y) => InBounds(x, y) ? _h[x, y] : 0f;
}
