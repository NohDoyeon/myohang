using Harbor.Core;
using Harbor.Protocol;
using Xunit;

public class IsoTests
{
    [Theory]
    [InlineData(0, 0)] [InlineData(3, 1)] [InlineData(5, 7)]
    public void ScreenWorld_RoundTrip(int x, int y)
    {
        var (sx, sy) = Iso.ToScreen(x + 0.5f, y + 0.5f, 0);   // 타일 중심
        Assert.Equal((x, y), Iso.ToWorld(sx, sy));
    }

    [Fact]
    public void DepthKey_FrontTilesAreLarger()
        => Assert.True(Iso.DepthKey(2, 2, 0) > Iso.DepthKey(1, 1, 0));
}

public class AStarTests
{
    private static readonly Heightmap Map = new(new[]
    {
        "xxxxxxx",
        "x00000x",
        "x0xxx0x",
        "x00000x",
        "xxxxxxx",
    });

    [Fact]
    public void FindsPathAroundWall()
    {
        var path = AStar.Find(Map, (_, _) => false, (1, 1), (1, 3));
        Assert.NotEmpty(path);
        Assert.Equal((1, 1), path[0]);
        Assert.Equal((1, 3), path[^1]);
        Assert.DoesNotContain((2, 2), path);
    }

    [Fact]
    public void BlockedTargetReturnsEmpty()
        => Assert.Empty(AStar.Find(Map, (x, y) => x == 1 && y == 3, (1, 1), (1, 3)));

    [Fact]
    public void NoCornerCutting()
    {
        var m = new Heightmap(new[] { "00", "x0" });
        // (0,0)→(1,1): 대각선은 (0,1)이 벽이라 금지 → (1,0) 경유
        var path = AStar.Find(m, (_, _) => false, (0, 0), (1, 1));
        Assert.Equal(new[] { (0, 0), (1, 0), (1, 1) }, path);
    }
}

public class WallsTests
{
    // data/rooms/cabin_default_01 과 같은 모양: 문(6,7)이 아래로 튀어나온 12x8 방
    private static readonly Walls W = new(new Heightmap(new[]
    {
        "xxxxxxxxxxxx",
        "x0000000000x",
        "x0000000000x",
        "x0000111000x",
        "x0000111000x",
        "x0000000000x",
        "x0000000000x",
        "xxxxxx0xxxxx",
    }));

    [Fact]
    public void BackEdgesHaveWalls()
    {
        Assert.True(W.West(1, 1)); Assert.True(W.North(1, 1));     // 뒤쪽 모서리
        Assert.True(W.West(1, 6)); Assert.False(W.North(1, 6));    // 왼쪽 벽만
        Assert.True(W.North(10, 1)); Assert.False(W.West(10, 1));  // 위쪽 벽만
    }

    [Fact]
    public void DoorAndInteriorHaveNoWalls()
    {
        Assert.False(W.West(6, 7)); Assert.False(W.North(6, 7));   // 문: 뒤에 걸을 수 있는 타일(5,6)이 있어 벽을 만들면 가림
        Assert.False(W.West(5, 3)); Assert.False(W.North(5, 3));   // 단상(높이 1) 내부
        Assert.False(W.West(0, 0));                                // 걸을 수 없는 타일
    }

    [Fact]
    public void CanHang_MatchesDirection()
    {
        Assert.True(W.CanHang(1, 1, 4)); Assert.True(W.CanHang(1, 1, 2));
        Assert.True(W.CanHang(1, 6, 2)); Assert.False(W.CanHang(1, 6, 4));
        Assert.False(W.CanHang(1, 1, 0));                          // 벽걸이는 2/4 만
    }

    [Fact]
    public void SlotGrid_BoundsAndTilt()
    {
        Assert.True(Walls.SlotValid(0, 0, 3));
        Assert.True(Walls.SlotValid(Walls.SlotCols - 1, 2, 3));
        Assert.False(Walls.SlotValid(Walls.SlotCols, 0, 3));       // 가로 칸 초과
        Assert.False(Walls.SlotValid(0, 3, 3));                    // 벽 높이 초과
        Assert.False(Walls.SlotValid(0, -1, 3));

        Assert.Equal(0, Walls.ClampTilt(0));
        Assert.Equal(Walls.MaxTilt, Walls.ClampTilt(90));
        Assert.Equal(-Walls.MaxTilt, Walls.ClampTilt(-90));
    }
}

public class FsmTests
{
    [Fact]
    public void FridgeCycle()
    {
        var fsm = new ItemFsm("closed", new[]
        {
            new FsmTransition("closed", "use", "open", "give_item:cola_can"),
            new FsmTransition("open", "use", "closed", null),
            new FsmTransition("open", "timer:5000", "closed", null),
        });
        var t = fsm.Apply("closed", "use");
        Assert.Equal("open", t!.To);
        Assert.Equal(5000, fsm.TimerFor("open"));
        Assert.Null(fsm.TimerFor("closed"));
    }
}

public class LotteryTests
{
    private const long Jackpot = 1000, Price = 10;

    [Theory]
    [InlineData(0.0, 1, 1000)]      // 1등
    [InlineData(0.0019, 1, 1000)]
    [InlineData(0.002, 2, 200)]     // 구간 경계는 다음 등수로
    [InlineData(0.0119, 2, 200)]
    [InlineData(0.012, 3, 50)]
    [InlineData(0.0619, 3, 50)]
    [InlineData(0.062, 4, 10)]      // 본전
    [InlineData(0.2619, 4, 10)]
    [InlineData(0.262, 0, 0)]       // 꽝
    [InlineData(0.999, 0, 0)]
    public void Draw_MatchesTable(double roll, int rank, long amount)
    {
        var p = Lottery.Draw(roll, Jackpot, Price);
        Assert.Equal(rank, p.Rank);
        Assert.Equal(amount, p.Amount);
    }

    [Fact]
    public void ExpectedValue_IsASink()
    {
        // 기대값이 가격보다 낮아야 루피가 빠져나간다 (수입은 일일 용돈 쪽)
        double ev = Lottery.ExpectedValue(Jackpot, Price);
        Assert.True(ev < Price, $"기대값 {ev} 이 가격 {Price} 이상 — 돈이 무한 생성됨");
        Assert.True(ev > Price * 0.5, $"기대값 {ev} 이 너무 낮아 재미없음");
    }
}

/// <summary>
/// data/rooms/dancehall.json 의 기본 배치가 기하학적으로 맞는지 지킨다.
/// 서버 Seed 는 어긋난 벽 위치를 버리지 않고 보정만 하므로(사용자 가구 유실 방지), 템플릿 실수는 여기서 잡는다.
/// 방 JSON 을 고치면 이 표도 같이 고쳐야 한다.
/// </summary>
public class DancehallTemplateTests
{
    private static readonly string[] Rows =
    {
        "xxxxxxxxxxxxxxxx",
        "x00000000000000x",
        "x01111111111100x",
        "x01111111111100x",
        "x01111111111100x",
        "x01111111111100x",
        "x00000000000000x",
        "x00000000000000x",
        "xxxxxxx00xxxxxxx",
    };
    private const int WallHeight = 3;
    private static readonly Heightmap Map = new(Rows);
    private static readonly Walls W = new(Map);

    [Theory]
    [InlineData(1, 1, 2, 0, 2)]    // clock_wall — 서쪽 벽, 위 칸
    [InlineData(5, 1, 4, 0, 1)]    // window_round — 북쪽 벽
    [InlineData(9, 1, 4, 1, 1)]    // window_round — 북쪽 벽, 오른쪽 칸
    public void WallItems_HaveRealWallAndValidSlot(int x, int y, int dir, int u, int v)
    {
        Assert.True(W.CanHang(x, y, dir), $"({x},{y}) dir {dir} 에 벽이 없다");
        Assert.True(Walls.SlotValid(u, v, WallHeight), $"슬롯 u{u} v{v} 가 범위를 벗어난다");
    }

    [Theory]
    [InlineData(2, 6)]             // cola_machine
    [InlineData(13, 6)]            // cola_machine
    [InlineData(1, 1)]             // lamp_floor
    [InlineData(14, 1)]            // lamp_floor
    public void FloorItems_AreOnWalkableTiles(int x, int y)
        => Assert.True(Map.Walkable(x, y), $"({x},{y}) 는 걸을 수 없는 타일이다");

    [Fact]
    public void SpawnAndDoor_AreWalkable()
    {
        Assert.True(Map.Walkable(7, 7));   // spawn
        Assert.True(Map.Walkable(8, 7));   // spawn 2
        Assert.True(Map.Walkable(7, 8));   // door
    }
}

/// <summary>
/// 방 넓히기 세 계열(다락방 5단계 · 모퉁이집 3단계 · 복층방 3단계)의 전제를 지킨다.
/// 넓힌 방은 이전 방의 걸을 수 있는 칸·높이·벽을 **같은 좌표로** 품어야 한다.
/// 그렇지 않으면 넓히는 순간 놓아 둔 가구가 걷지 못하는 칸에 놓이거나, 벽걸이가 벽 없는 자리로 밀린다
/// (서버 Seed 는 버리지 않고 보정하므로, 어긋나도 조용히 자리만 틀어진다 — 그래서 여기서 잡는다).
/// data/rooms/*.json 을 고치면 이 표도 같이 고쳐야 한다.
/// </summary>
public class RoomUpgradeTests
{
    private static readonly string[] Small =      // cabin_default_01
    {
        "xxxxxxxxxxxx",
        "x0000000000x",
        "x0000000000x",
        "x0000111000x",
        "x0000111000x",
        "x0000000000x",
        "x0000000000x",
        "xxxxxx0xxxxx",
    };
    private static readonly string[] Wide =       // cabin_wide_02
    {
        "xxxxxxxxxxxxxxxx",
        "x00000000000000x",
        "x00000000000000x",
        "x00001110000000x",
        "x00001110000000x",
        "x00000000000000x",
        "x00000000000000x",
        "x00000000000000x",
        "x00000000000000x",
        "xxxxxxxx00xxxxxx",
    };
    private static readonly string[] Grand =      // cabin_grand_03
    {
        "xxxxxxxxxxxxxxxxxxxx",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000011100000000000x",
        "x000011100000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "xxxxxxxxxx00xxxxxxxx",
    };
    private static readonly string[] Manor =      // cabin_manor_04
    {
        "xxxxxxxxxxxxxxxxxxxxxxxx",
        "x0000000000000000000000x",
        "x0000000000000000000000x",
        "x0000111000000000000000x",
        "x0000111000000000000000x",
        "x0000000000000000000000x",
        "x0000000000000000000000x",
        "x0000000000000000000000x",
        "x0000000000000000000000x",
        "x0000000000000000000000x",
        "x0000000000000000000000x",
        "x0000000000000000000000x",
        "x0000000000000000000000x",
        "xxxxxxxxxxx00xxxxxxxxxxx",
    };
    private static readonly string[] Harbor =     // cabin_harbor_05 (최종)
    {
        "xxxxxxxxxxxxxxxxxxxxxxxxxx",
        "x000000000000000000000000x",
        "x000000000000000000000000x",
        "x000011100000000000000000x",
        "x000011100000000000000000x",
        "x000000000000000000000000x",
        "x000000000000000000000000x",
        "x000000000000000000000000x",
        "x000000000000000000000000x",
        "x000000000000000000000000x",
        "x000000000000000000000000x",
        "x000000000000000000000000x",
        "x000000000000000000000000x",
        "x000000000000000000000000x",
        "x000000000000000000000000x",
        "xxxxxxxxxxxx00xxxxxxxxxxxx",
    };

    // ㄱ자 계열 — 홈은 '먼 구석'(x 작고 y 작은 쪽)에만 팔 수 있다. 반대로 파면 날개 윗변에 벽이 안 생긴다.
    private static readonly string[] Corner1 =    // corner_01
    {
        "xxxxxxxxxxxx",
        "xxxx0000000x",
        "xxxx0000000x",
        "x0000000000x",
        "x0000000000x",
        "x0000000000x",
        "x0000000000x",
        "x0000000000x",
        "xxxxx0xxxxxx",
    };
    private static readonly string[] Corner2 =    // corner_02
    {
        "xxxxxxxxxxxxxxxx",
        "xxxx00000000000x",
        "xxxx00000000000x",
        "x00000000000000x",
        "x00000000000000x",
        "x00000000000000x",
        "x00000000000000x",
        "x00000000000000x",
        "x00000000000000x",
        "x00000000000000x",
        "xxxxxxx0xxxxxxxx",
    };
    private static readonly string[] Corner3 =    // corner_03
    {
        "xxxxxxxxxxxxxxxxxxxx",
        "xxxx000000000000000x",
        "xxxx000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "xxxxxxxxx0xxxxxxxxxx",
    };

    // 복층 계열 — 위층(y1~3)은 옆으로만 넓어진다. 줄 수가 바뀌면 예전 위층 가구가 아래층이 된다.
    private static readonly string[] Loft1 =      // loft_01
    {
        "xxxxxxxxxxxx",
        "x1111111111x",
        "x1111111111x",
        "x1111111111x",
        "x0000000000x",
        "x0000000000x",
        "x0000000000x",
        "xxxxx0xxxxxx",
    };
    private static readonly string[] Loft2 =      // loft_02
    {
        "xxxxxxxxxxxxxxxx",
        "x11111111111111x",
        "x11111111111111x",
        "x11111111111111x",
        "x00000000000000x",
        "x00000000000000x",
        "x00000000000000x",
        "x00000000000000x",
        "x00000000000000x",
        "x00000000000000x",
        "xxxxxxx0xxxxxxxx",
    };
    private static readonly string[] Loft3 =      // loft_03
    {
        "xxxxxxxxxxxxxxxxxxxx",
        "x111111111111111111x",
        "x111111111111111111x",
        "x111111111111111111x",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "x000000000000000000x",
        "xxxxxxxxx0xxxxxxxxxx",
    };

    public static TheoryData<string[], string[]> Steps => new()
    {
        { Small, Wide }, { Wide, Grand }, { Grand, Manor }, { Manor, Harbor },   // 다락방 계열
        { Corner1, Corner2 }, { Corner2, Corner3 },                              // 모퉁이집 계열
        { Loft1, Loft2 }, { Loft2, Loft3 },                                      // 복층 계열
    };

    /// <summary>
    /// 걸을 수 있는 영역이 24×14 를 넘으면 줌 1배(가장 넓게 보는 단계)에서도 한 화면(1280×720)에 안 들어온다.
    /// 아이소 2:1 이라 화면 폭은 (가로+세로)×32 로 늘어난다 — 가로만 늘려도 세로가 같이 커진다.
    /// </summary>
    [Theory]
    [MemberData(nameof(All))]
    public void Rooms_FitOnScreen(string[] rows)
    {
        var map = new Heightmap(rows);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int y = 0; y < map.H; y++)
            for (int x = 0; x < map.W; x++)
                if (map.Walkable(x, y))
                { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }

        int w = maxX - minX + 1, h = maxY - minY + 1;
        int screenW = (w + h) * (Iso.TileW / 2);
        Assert.True(screenW <= 1280, $"{w}×{h} 칸 = 화면 폭 {screenW}px — 줌 1배에서도 화면(1280)을 넘는다");
    }

    public static TheoryData<string[]> All => new() { Small, Wide, Grand, Manor, Harbor, Corner1, Corner2, Corner3, Loft1, Loft2, Loft3 };

    /// <summary>
    /// 방 전체가 한 덩어리로 이어져 있어야 한다 — 끊긴 구역이 있으면 걸어서 갈 수 없는 바닥이 생긴다.
    /// ㄱ자처럼 파인 모양에서 실수하기 쉽다. 문 칸까지 닿는지도 같이 본다(문이 떨어져 있으면 입장 자체가 막힌다).
    /// </summary>
    [Theory]
    [MemberData(nameof(All))]
    public void Rooms_AreOneConnectedArea(string[] rows)
    {
        var map = new Heightmap(rows);
        var walkable = new List<(int x, int y)>();
        for (int y = 0; y < map.H; y++)
            for (int x = 0; x < map.W; x++)
                if (map.Walkable(x, y)) walkable.Add((x, y));

        var seen = new HashSet<(int x, int y)> { walkable[0] };
        var queue = new Queue<(int x, int y)>();
        queue.Enqueue(walkable[0]);
        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
            {
                var n = (x: cx + dx, y: cy + dy);
                if (!map.Walkable(n.x, n.y) || !seen.Add(n)) continue;
                queue.Enqueue(n);
            }
        }
        Assert.Equal(walkable.Count, seen.Count);
    }

    /// <summary>
    /// 뒤쪽으로 트인 면에는 반드시 벽이 있어야 한다.
    /// `Walls` 는 **누적 영역**으로 판정하므로(그 칸의 왼쪽 위 전체가 비어 있어야 벽), ㄱ자 홈을 '가까운 쪽'에 파면
    /// 그 면에 벽이 생기지 않아 방이 뚫려 보인다. 이 규칙을 어긴 템플릿을 여기서 잡는다.
    /// 문 칸만 예외 — 앞으로 튀어나온 출입구라 원래 벽이 없다.
    /// </summary>
    [Theory]
    [MemberData(nameof(Doors))]
    public void BackEdges_HaveWalls(string[] rows, int doorX, int doorY)
    {
        var map = new Heightmap(rows);
        var walls = new Walls(map);
        for (int y = 0; y < map.H; y++)
            for (int x = 0; x < map.W; x++)
            {
                if (!map.Walkable(x, y) || (x == doorX && y == doorY)) continue;
                if (!map.Walkable(x, y - 1)) Assert.True(walls.North(x, y), $"({x},{y}) 위가 비었는데 북쪽 벽이 없다 — 방이 뚫려 보인다");
                if (!map.Walkable(x - 1, y)) Assert.True(walls.West(x, y), $"({x},{y}) 왼쪽이 비었는데 서쪽 벽이 없다 — 방이 뚫려 보인다");
            }
    }

    [Theory]
    [MemberData(nameof(Steps))]
    public void BiggerRoom_ContainsSmallerOne(string[] fromRows, string[] toRows)
    {
        var from = new Heightmap(fromRows);
        var to = new Heightmap(toRows);
        var wFrom = new Walls(from);
        var wTo = new Walls(to);

        for (int y = 0; y < from.H; y++)
            for (int x = 0; x < from.W; x++)
            {
                if (!from.Walkable(x, y)) continue;
                Assert.True(to.Walkable(x, y), $"({x},{y}) 가 넓힌 방에서 걸을 수 없다 — 그 자리 가구가 갇힌다");
                Assert.Equal(from.Height(x, y), to.Height(x, y));
                if (wFrom.West(x, y)) Assert.True(wTo.West(x, y), $"({x},{y}) 서쪽 벽이 사라졌다 — 벽걸이가 밀린다");
                if (wFrom.North(x, y)) Assert.True(wTo.North(x, y), $"({x},{y}) 북쪽 벽이 사라졌다 — 벽걸이가 밀린다");
            }
    }

    [Theory]
    [MemberData(nameof(Steps))]
    public void BiggerRoom_IsActuallyBigger(string[] fromRows, string[] toRows)
    {
        int Tiles(string[] rows) => rows.Sum(r => r.Count(c => c != 'x'));
        Assert.True(Tiles(toRows) > Tiles(fromRows), "넓히기인데 칸 수가 늘지 않았다");
    }

    /// <summary>각 방의 문 칸. JSON 의 door 와 같은 값이어야 한다(스폰은 그 바로 위 칸).</summary>
    public static TheoryData<string[], int, int> Doors => new()
    {
        { Small, 6, 7 }, { Wide, 8, 9 }, { Grand, 10, 11 }, { Manor, 11, 13 }, { Harbor, 12, 15 },
        { Corner1, 5, 8 }, { Corner2, 7, 10 }, { Corner3, 9, 12 },
        { Loft1, 5, 7 }, { Loft2, 7, 10 }, { Loft3, 9, 12 },
    };

    [Theory]
    [MemberData(nameof(Doors))]
    public void Doors_AreWalkable(string[] rows, int x, int y)
    {
        var map = new Heightmap(rows);
        Assert.True(map.Walkable(x, y), $"문 ({x},{y}) 이 걸을 수 없는 칸이다");
        Assert.True(map.Walkable(x, y - 1), $"스폰 ({x},{y - 1}) 이 걸을 수 없는 칸이다");
    }
}

public class FigureTests
{
    [Theory]
    [InlineData("hd-001-02.hr-002-05.ch-001-03.lg-001-01.sh-001-01")]
    [InlineData("hd-001-02.ha-001-04")]
    [InlineData("hr-999-99")]
    public void Valid(string f) => Assert.True(Figure.IsValid(f));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("zz-001-01")]              // 없는 파츠
    [InlineData("hd-001")]                 // 조각 부족
    [InlineData("hd-001-01-01")]           // 조각 초과
    [InlineData("hd-000-01")]              // 모델 0
    [InlineData("hd-001-00")]              // 팔레트 0
    [InlineData("hd-1000-01")]             // 모델 범위 초과
    [InlineData("hd-001-01.hd-002-01")]    // 같은 파츠 중복
    [InlineData("hd-00a-01")]              // 숫자 아님
    public void Invalid(string? f) => Assert.False(Figure.IsValid(f));

    [Fact]
    public void TooLong_Invalid()
        => Assert.False(Figure.IsValid(new string('a', Figure.MaxLength + 1)));

    [Fact]
    public void Sanitize_FallsBackToDefault()
    {
        Assert.Equal(Figure.Default, Figure.Sanitize("쓰레기"));
        Assert.Equal("hr-002-05", Figure.Sanitize("hr-002-05"));
        Assert.True(Figure.IsValid(Figure.Default));
    }

    [Fact]
    public void Get_ReadsModelAndPalette()
    {
        Assert.Equal((2, 5), Figure.Get("hd-001-02.hr-002-05", "hr"));
        Assert.Null(Figure.Get("hd-001-02", "ha"));
        Assert.Null(Figure.Get(null, "hr"));
    }

    [Fact]
    public void Build_OrdersPartsAndPads()
    {
        var parts = new Dictionary<string, (int model, int palette)>
        {
            ["ch"] = (1, 3), ["hd"] = (1, 2), ["ha"] = (1, 4),
        };
        var f = Figure.Build(parts);
        Assert.Equal("hd-001-02.ch-001-03.ha-001-04", f);   // Parts 순서: hd, hr, ch, lg, sh, ha
        Assert.True(Figure.IsValid(f));
    }

    [Fact]
    public void Build_EmptyGivesDefault()
        => Assert.Equal(Figure.Default, Figure.Build(new Dictionary<string, (int, int)>()));
}

public class PasswordHashTests
{
    [Fact]
    public void Verify_AcceptsCorrectAndRejectsWrong()
    {
        var salt = PasswordHash.NewSalt();
        var hash = PasswordHash.Hash("harbor1234", salt);
        Assert.True(PasswordHash.Verify("harbor1234", salt, hash));
        Assert.False(PasswordHash.Verify("harbor1235", salt, hash));
        Assert.False(PasswordHash.Verify("", salt, hash));
    }

    [Fact]
    public void SamePassword_DifferentSalt_DifferentHash()
    {
        var a = PasswordHash.NewSalt();
        var b = PasswordHash.NewSalt();
        Assert.NotEqual(a, b);
        Assert.NotEqual(PasswordHash.Hash("같은비밀번호", a), PasswordHash.Hash("같은비밀번호", b));
    }

    [Fact]
    public void Verify_EmptyOrCorruptStored_IsFalse()
    {
        Assert.False(PasswordHash.Verify("x", "", ""));
        Assert.False(PasswordHash.Verify("x", "not base64!!", "also not"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("abc", false)]
    [InlineData("abcd", true)]
    public void IsAcceptable_ChecksLength(string? pw, bool ok)
        => Assert.Equal(ok, PasswordHash.IsAcceptable(pw));

    [Fact]
    public void IsAcceptable_RejectsTooLong()
        => Assert.False(PasswordHash.IsAcceptable(new string('a', PasswordHash.MaxLength + 1)));
}

public class AttendanceTests
{
    private static readonly DateOnly Today = new(2026, 9, 17);

    [Fact]
    public void FirstVisit_IsDayOne() => Assert.Equal(1, Attendance.NextStreak(null, Today, 0));

    [Fact]
    public void CameYesterday_Increments() => Assert.Equal(4, Attendance.NextStreak(Today.AddDays(-1), Today, 3));

    [Fact]
    public void AlreadyToday_Unchanged() => Assert.Equal(3, Attendance.NextStreak(Today, Today, 3));

    [Fact]
    public void SkippedADay_ResetsToOne() => Assert.Equal(1, Attendance.NextStreak(Today.AddDays(-2), Today, 9));

    [Theory]
    [InlineData(1, 100)]    // 1일차 = 기본
    [InlineData(2, 150)]
    [InlineData(7, 400)]    // 7일차 = 최대
    [InlineData(30, 400)]   // 그 뒤로는 계속 최대
    [InlineData(0, 100)]    // 방어: 0 이하도 기본
    public void Reward_GrowsThenCaps(int streak, long expected)
        => Assert.Equal(expected, Attendance.Reward(100, 50, streak));
}

public class TradeTests
{
    [Fact]
    public void Singles_Only()
    {
        var q = Trade.QuoteSell(5, 40, 0, 0);
        Assert.Equal((5, 0, 200L), (q.Qty, q.Bundles, q.Total));
    }

    [Fact]
    public void Bundles_First_Then_Singles()
    {
        // 30장 묶음 1500, 낱개 40. 65장 = 묶음 2(3000) + 낱개 5(200)
        var q = Trade.QuoteSell(65, 40, 30, 1500);
        Assert.Equal(2, q.Bundles);
        Assert.Equal(3200L, q.Total);
    }

    [Fact]
    public void ExactBundle_NoSingles()
    {
        var q = Trade.QuoteSell(30, 40, 30, 1500);
        Assert.Equal((1, 1500L), (q.Bundles, q.Total));
    }

    [Fact]
    public void Zero_Or_Negative_IsNothing()
    {
        Assert.Equal(0L, Trade.QuoteSell(0, 40, 30, 1500).Total);
        Assert.Equal(0L, Trade.QuoteSell(-3, 40, 30, 1500).Total);
    }

    [Fact]
    public void CatnipBundle_IsActuallyWorthIt()
        => Assert.True(Trade.BundleIsWorthIt(40, 30, 1500), "30장 묶음(1500)이 낱개 30장(1200)보다 비싸야 모을 이유가 생긴다");
}

public class FramingTests
{
    [Fact]
    public void EncodeDecode_RoundTrip_And_PartialBuffer()
    {
        var frame = Framing.Encode(Opcode.C_Move, new C_Move { X = 3, Y = 4 });
        ReadOnlyMemory<byte> partial = frame.AsMemory(0, frame.Length - 1);
        Assert.False(Framing.TryDecode(ref partial, out _, out _));

        ReadOnlyMemory<byte> full = frame;
        Assert.True(Framing.TryDecode(ref full, out var op, out var body));
        Assert.Equal(Opcode.C_Move, op);
        var p = Framing.Deserialize<C_Move>(body);
        Assert.Equal((3, 4), (p.X, p.Y));
        Assert.Equal(0, full.Length);
    }
}
