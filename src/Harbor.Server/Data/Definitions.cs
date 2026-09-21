using System.Text.Json;
using System.Text.Json.Serialization;
using Harbor.Core;

namespace Harbor.Server.Data;

public sealed class RoomDef
{
    public string RoomId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "private";
    public string[] Heightmap { get; set; } = Array.Empty<string>();
    public TileRef Door { get; set; } = new();
    public StyleRef Wall { get; set; } = new();
    public StyleRef Floor { get; set; } = new();
    public TileRef[] Spawn { get; set; } = Array.Empty<TileRef>();
    public int MaxUsers { get; set; } = 25;
    /// <summary>방을 처음 만들 때 기본으로 놓이는 가구. 저장된 배치가 있으면 그쪽이 이긴다.</summary>
    public FurniPlacement[] Furni { get; set; } = Array.Empty<FurniPlacement>();
    /// <summary>이 방에서 넓힐 수 있는 다음 템플릿 roomId. 비어 있으면 최종 단계.</summary>
    public string Upgrade { get; set; } = "";
    public long UpgradePrice { get; set; }
    /// <summary>집 계열 id (cabin / corner / loft…). 넓히기는 같은 계열 안에서만 이어진다. 공용 방은 비어 있다.</summary>
    public string Family { get; set; } = "";
    /// <summary>계열 안의 단계(1부터). 이사할 때 같은 단계를 찾는 기준.</summary>
    public int Tier { get; set; }
}
public sealed class FurniPlacement
{
    public string FurniId { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Dir { get; set; } = 2;
    public int WallU { get; set; }
    public int WallV { get; set; }
    public int Tilt { get; set; }
}
public sealed class TileRef { public int X { get; set; } public int Y { get; set; } public int Dir { get; set; } }
public sealed class StyleRef { public string Style { get; set; } = ""; public int Height { get; set; } = 3; }

public sealed class FurniDef
{
    public string FurniId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Category { get; set; } = "";
    public Footprint Footprint { get; set; } = new();
    public float Height { get; set; } = 1f;
    public string[] Flags { get; set; } = Array.Empty<string>();
    public int[] Rotations { get; set; } = { 0, 2, 4, 6 };
    public string Sprite { get; set; } = "";
    public string[] States { get; set; } = { "default" };
    public InteractionDef Interaction { get; set; } = new();
    public PriceDef Price { get; set; } = new();
    /// <summary>가방에서 루피로 팔 수 있는 물건(수확물 등). null 이면 못 판다.</summary>
    public SellDef? Sell { get; set; }

    public bool Wall => Flags.Contains("wall");            // 벽걸이: 바닥을 막지 않음, 벽 있는 타일에만 (dir 4 북쪽 / 2 서쪽)
    public bool Solid => Flags.Contains("solid") && !Wall;
    public ItemFsm? BuildFsm() => Interaction.Type != "fsm" ? null
        : new ItemFsm(Interaction.Initial ?? States[0],
            Interaction.Transitions.Select(t => new FsmTransition(t.From, t.On, t.To, t.Effect)));
}
public sealed class Footprint { public int W { get; set; } = 1; public int H { get; set; } = 1; }
public sealed class InteractionDef
{
    public string Type { get; set; } = "none";
    public string? Initial { get; set; }
    public TransitionDef[] Transitions { get; set; } = Array.Empty<TransitionDef>();
}
public sealed class TransitionDef { public string From { get; set; } = ""; public string On { get; set; } = ""; public string To { get; set; } = ""; public string? Effect { get; set; } }
public sealed class PriceDef { public string Currency { get; set; } = "rupee"; public long Amount { get; set; } }
/// <summary>낱개 값 + 묶음(개수·값). 묶음이 낱개×개수보다 비싸야 "모아서 파는" 의미가 있다.</summary>
public sealed class SellDef { public long Price { get; set; } public int BundleQty { get; set; } public long BundlePrice { get; set; } }

/// <summary>data/rooms, data/furni JSON 로더. RCON reload-defs 로 핫리로드.</summary>
public sealed class DefinitionStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public IReadOnlyDictionary<string, RoomDef> Rooms { get; private set; } = new Dictionary<string, RoomDef>();
    public IReadOnlyDictionary<string, FurniDef> Furni { get; private set; } = new Dictionary<string, FurniDef>();

    private string _root = "";

    public void Load(string root)
    {
        // **둘 다 읽은 뒤에** 바꾼다. 중간에 예외가 나면 기존 정의가 그대로 남는다 —
        // 깨진 JSON 하나를 올렸다고 라이브의 가구 목록이 반쯤 비면 안 된다.
        var rooms = LoadDir<RoomDef>(Path.Combine(root, "rooms"), r => r.RoomId);
        var furni = LoadDir<FurniDef>(Path.Combine(root, "furni"), f => f.FurniId);
        Rooms = rooms; Furni = furni; _root = root;
    }

    /// <summary>
    /// 디스크에서 다시 읽는다 (`/admin/reload-defs`).
    ///
    /// **한계**: 이미 만들어진 방은 생성 시점의 `RoomDef` 를 들고 있어 **모양·정원이 바뀌지 않는다**(재시작 필요).
    /// 가구 정의는 룸이 `DefinitionStore` 를 통해 찾으므로 **즉시 반영된다** — 가격·상태·판매값 수정이 주 용도다.
    /// </summary>
    public void Reload()
    {
        if (_root.Length == 0) throw new InvalidOperationException("아직 Load 한 적이 없습니다");
        Load(_root);
    }

    private static Dictionary<string, T> LoadDir<T>(string dir, Func<T, string> key)
    {
        var d = new Dictionary<string, T>();
        if (!Directory.Exists(dir)) return d;
        foreach (var f in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
        {
            var obj = JsonSerializer.Deserialize<T>(File.ReadAllText(f), Json)
                      ?? throw new InvalidDataException($"null def: {f}");
            d[key(obj)] = obj;
        }
        return d;
    }
}
