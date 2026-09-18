namespace Harbor.Core;

/// <summary>8방향 A*. 대각선 코너컷 금지, 높이차 &gt; 1.1 이동 불가.</summary>
public static class AStar
{
    private static readonly (int dx, int dy)[] Dirs =
        { (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1) };

    public static List<(int x, int y)> Find(
        Heightmap map,
        Func<int, int, bool> blocked,
        (int x, int y) from,
        (int x, int y) to,
        int maxNodes = 4096)
    {
        if (!map.Walkable(to.x, to.y) || blocked(to.x, to.y)) return new();
        if (from == to) return new() { from };

        var open = new PriorityQueue<(int x, int y), float>();
        var came = new Dictionary<(int, int), (int, int)>();
        var g = new Dictionary<(int, int), float> { [from] = 0f };
        var closed = new HashSet<(int, int)>();
        open.Enqueue(from, 0f);
        int expanded = 0;

        while (open.TryDequeue(out var cur, out _))
        {
            if (cur == to) return Rebuild(came, cur);
            if (!closed.Add(cur)) continue;
            if (++expanded > maxNodes) break;

            foreach (var (dx, dy) in Dirs)
            {
                var n = (x: cur.x + dx, y: cur.y + dy);
                if (closed.Contains(n)) continue;
                if (!map.Walkable(n.x, n.y) || blocked(n.x, n.y)) continue;
                if (dx != 0 && dy != 0 &&
                    (!map.Walkable(cur.x + dx, cur.y) || !map.Walkable(cur.x, cur.y + dy)))
                    continue;
                if (MathF.Abs(map.Height(n.x, n.y) - map.Height(cur.x, cur.y)) > 1.1f) continue;

                float ng = g[cur] + ((dx != 0 && dy != 0) ? 1.414f : 1f);
                if (g.TryGetValue(n, out var og) && ng >= og) continue;
                g[n] = ng;
                came[n] = cur;
                float h = MathF.Max(MathF.Abs(n.x - to.x), MathF.Abs(n.y - to.y));
                open.Enqueue(n, ng + h);
            }
        }
        return new();
    }

    private static List<(int x, int y)> Rebuild(Dictionary<(int, int), (int, int)> came, (int x, int y) cur)
    {
        var path = new List<(int x, int y)> { cur };
        while (came.TryGetValue(cur, out var prev))
        {
            cur = prev;
            path.Add(cur);
        }
        path.Reverse();
        return path;
    }
}
