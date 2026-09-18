using MessagePack;

namespace Harbor.Protocol;

// 주의: [MessagePackObject] 타입에서 한 줄 다중 필드 선언 금지 (같은 [Key] 공유됨).

// ----- DTO -----
[MessagePackObject]
public sealed class RoomDto
{
    [Key(0)] public long Id;
    [Key(1)] public string TemplateId = "";
    [Key(2)] public string Name = "";
    [Key(3)] public string[] Heightmap = Array.Empty<string>();
    [Key(4)] public short DoorX;
    [Key(5)] public short DoorY;
    [Key(6)] public string WallStyle = "";
    [Key(7)] public string FloorStyle = "";
    [Key(8)] public byte WallHeight = 3;      // 벽 높이(단위: 층). 클라 렌더용
    [Key(9)] public long OwnerId;            // 0 = 공용 방
    [Key(10)] public string OwnerNick = "";
    [Key(11)] public string Kind = "";       // "public" | "private"
    [Key(12)] public long UpgradePrice;      // 0 = 더 넓힐 수 없음
    [Key(13)] public string UpgradeName = ""; // 다음 단계 방 이름 (표시용)
    [Key(14)] public int Width;              // 타일 수 (표시용)
    [Key(15)] public int Height;
}

[MessagePackObject]
public sealed class ItemDto
{
    [Key(0)] public long Id;
    [Key(1)] public string FurniId = "";
    [Key(2)] public short X;
    [Key(3)] public short Y;
    [Key(4)] public float Z;
    [Key(5)] public byte Dir;
    [Key(6)] public string State = "";
    [Key(7)] public string? Extra;
    [Key(8)] public string Name = "";          // 표시 이름 (서버 정의 displayName)
    [Key(9)] public bool Solid;               // 통과 불가 여부 (클라 클릭 판정용)
    [Key(10)] public string Interaction = ""; // "fsm" | "seat" | "none"
    [Key(11)] public bool Wall;               // 벽걸이 가구 (dir 4 = 북쪽 벽, 2 = 서쪽 벽)
    [Key(12)] public string Author = "";      // 놓은 사람 닉 (포스트잇: 글쓴이. Extra = 본문)
    [Key(13)] public byte WallU;              // 벽 슬롯 가로 칸 (타일 모서리를 Walls.SlotCols 등분)
    [Key(14)] public byte WallV;              // 벽 슬롯 세로 칸 (0 = 바닥 쪽, 벽 높이 단위)
    [Key(15)] public sbyte Tilt;              // 기울기(도). 포스트잇 등 벽에 삐뚜름하게 붙이기
    [Key(16)] public bool Usable;             // 지금 상태에서 '사용'이 먹히는가 (자라는 중인 식물은 false)
}

[MessagePackObject]
public sealed class UserDto
{
    [Key(0)] public long Id;
    [Key(1)] public string Nick = "";
    [Key(2)] public string Figure = "";
    [Key(3)] public short X;
    [Key(4)] public short Y;
    [Key(5)] public float Z;
    [Key(6)] public byte Dir;
    [Key(7)] public string Action = "stand";
}

[MessagePackObject]
public sealed class RoomInfo
{
    [Key(0)] public long Id;
    [Key(1)] public string Name = "";
    [Key(2)] public string Kind = "";
    [Key(3)] public long OwnerId;
    [Key(4)] public string OwnerNick = "";
    [Key(5)] public int Users;
    [Key(6)] public int MaxUsers;
}

[MessagePackObject]
public sealed class CatalogEntry
{
    [Key(0)] public string FurniId = "";
    [Key(1)] public string Name = "";
    [Key(2)] public string Category = "";
    [Key(3)] public long Price;
    [Key(4)] public string Currency = "rupee";
    [Key(5)] public bool Wall;
    [Key(6)] public string Interaction = "";   // "postit" 이면 남의 방에도 붙일 수 있음
}

[MessagePackObject]
public sealed class InventoryEntry
{
    [Key(0)] public string FurniId = "";
    [Key(1)] public string Name = "";
    [Key(2)] public int Qty;
    [Key(3)] public bool Wall;
    [Key(4)] public string Interaction = "";
    [Key(5)] public long SellPrice;          // 0 = 팔 수 없음
    [Key(6)] public int SellBundleQty;       // 묶음 단위 (0 = 묶음 없음)
    [Key(7)] public long SellBundlePrice;
}

/// <summary>
/// 이사할 수 있는 집 한 채. 계열(family)마다 한 줄이고, 지금 단계와 같은 단계(없으면 그 계열의 마지막 단계)를 가리킨다.
/// </summary>
[MessagePackObject]
public sealed class HouseStyle
{
    [Key(0)] public string TemplateId = "";
    [Key(1)] public string Name = "";         // 그 단계 방 이름 ("넓은 모퉁이집")
    [Key(2)] public string Family = "";
    [Key(3)] public int Tier;
    [Key(4)] public int Tiles;                // 걸을 수 있는 칸 수 (얼마나 넓은지 비교용)
    [Key(5)] public long Price;               // 이사 비용
    [Key(6)] public bool Current;             // 지금 살고 있는 계열
}

[MessagePackObject] public sealed class TilePos { [Key(0)] public short X; [Key(1)] public short Y; }

// ----- C → S -----
[MessagePackObject] public sealed class C_Login { [Key(0)] public string Login = ""; [Key(1)] public string Token = ""; }
[MessagePackObject] public sealed class C_EnterRoom { [Key(0)] public long RoomId; }
[MessagePackObject] public sealed class C_Move { [Key(0)] public short X; [Key(1)] public short Y; }
[MessagePackObject] public sealed class C_Action { [Key(0)] public string Action = ""; }   // sit|stand|dance|wave|...
/// <summary>외모 변경. "hd-001-02.hr-002-05.ch-001-03.lg-001-01.sh-001-01.ha-001-04" 꼴 (파츠-모델-팔레트).</summary>
[MessagePackObject] public sealed class C_SetFigure { [Key(0)] public string Figure = ""; }
[MessagePackObject] public sealed class C_Chat { [Key(0)] public string Text = ""; [Key(1)] public byte Kind; }
[MessagePackObject] public sealed class C_Whisper { [Key(0)] public long TargetId; [Key(1)] public string Text = ""; }
[MessagePackObject] public sealed class C_PlaceItem { [Key(0)] public string FurniId = ""; [Key(1)] public short X; [Key(2)] public short Y; [Key(3)] public byte Dir; [Key(4)] public byte WallU; [Key(5)] public byte WallV; [Key(6)] public sbyte Tilt; }
[MessagePackObject] public sealed class C_PickItem { [Key(0)] public long ItemId; }
[MessagePackObject] public sealed class C_MoveItem { [Key(0)] public long ItemId; [Key(1)] public short X; [Key(2)] public short Y; [Key(3)] public byte Dir; }
[MessagePackObject] public sealed class C_UseItem { [Key(0)] public long ItemId; }
/// <summary>내가 붙인 포스트잇(ItemId)에 본문 쓰기. 서버가 S_ItemState(State="written", Extra=본문) 로 방송.</summary>
[MessagePackObject] public sealed class C_PostitWrite { [Key(0)] public long ItemId; [Key(1)] public string Body = ""; }
[MessagePackObject] public sealed class C_BuyCatalog { [Key(0)] public string FurniId = ""; [Key(1)] public int Qty = 1; }
/// <summary>가방의 수확물을 루피로. 묶음 단위가 있으면 묶음을 먼저 채운다(서버가 계산).</summary>
[MessagePackObject] public sealed class C_SellItem { [Key(0)] public string FurniId = ""; [Key(1)] public int Qty = 1; }
/// <summary>다른 계열의 집으로 이사. 방 모양이 달라지므로 **놓여 있던 가구는 전부 주인 가방으로 돌아온다.**</summary>
[MessagePackObject] public sealed class C_RemodelRoom { [Key(0)] public string TemplateId = ""; }

// ----- S → C -----
/// <summary>Nick 은 서버가 확정한 닉 — 입장권으로 들어오면 클라가 입력한 값과 다를 수 있으므로 이걸 따른다.</summary>
[MessagePackObject] public sealed class S_LoginResult { [Key(0)] public bool Ok; [Key(1)] public long UserId; [Key(2)] public string? Reason; [Key(3)] public long HomeRoomId; [Key(4)] public string Notice = ""; [Key(5)] public string Nick = ""; }
/// <summary>Rank 0 = 꽝. Prize 는 받은 루피(4등은 본전).</summary>
[MessagePackObject] public sealed class S_LotteryResult { [Key(0)] public int Rank; [Key(1)] public long Prize; [Key(2)] public long Price; [Key(3)] public string Message = ""; }
[MessagePackObject] public sealed class S_RoomSnapshot { [Key(0)] public RoomDto Room = new(); [Key(1)] public List<ItemDto> Items = new(); [Key(2)] public List<UserDto> Users = new(); }
[MessagePackObject] public sealed class S_RoomList { [Key(0)] public List<RoomInfo> Rooms = new(); }
[MessagePackObject] public sealed class S_HouseList { [Key(0)] public List<HouseStyle> Houses = new(); }
[MessagePackObject] public sealed class S_UserEnter { [Key(0)] public UserDto User = new(); }
[MessagePackObject] public sealed class S_UserLeave { [Key(0)] public long UserId; }
[MessagePackObject] public sealed class S_UserPath { [Key(0)] public long UserId; [Key(1)] public List<TilePos> Path = new(); }
[MessagePackObject] public sealed class S_UserAction { [Key(0)] public long UserId; [Key(1)] public string Action = ""; [Key(2)] public byte Dir; }
[MessagePackObject] public sealed class S_UserFigure { [Key(0)] public long UserId; [Key(1)] public string Figure = ""; }
[MessagePackObject] public sealed class S_ChatBubble { [Key(0)] public long UserId; [Key(1)] public string Text = ""; [Key(2)] public byte Kind; }
[MessagePackObject] public sealed class S_ItemAdd { [Key(0)] public ItemDto Item = new(); }
[MessagePackObject] public sealed class S_ItemRemove { [Key(0)] public long ItemId; }
[MessagePackObject] public sealed class S_ItemUpdate { [Key(0)] public ItemDto Item = new(); }
[MessagePackObject] public sealed class S_ItemState { [Key(0)] public long ItemId; [Key(1)] public string State = ""; [Key(2)] public string? Extra; [Key(3)] public bool Usable; }
/// <summary>Qty 는 변화량이 아니라 현재 보유 수량(절대값).</summary>
[MessagePackObject] public sealed class S_InventoryUpdate { [Key(0)] public string FurniId = ""; [Key(1)] public int Qty; [Key(2)] public string Name = ""; [Key(3)] public bool Wall; [Key(4)] public string Interaction = ""; [Key(5)] public long SellPrice; [Key(6)] public int SellBundleQty; [Key(7)] public long SellBundlePrice; }
/// <summary>서버가 보내는 일반 안내(토스트+채팅 로그). 오류가 아니다.</summary>
[MessagePackObject] public sealed class S_Notice { [Key(0)] public string Text = ""; }
[MessagePackObject] public sealed class S_Inventory { [Key(0)] public List<InventoryEntry> Items = new(); }
[MessagePackObject] public sealed class S_Catalog { [Key(0)] public List<CatalogEntry> Items = new(); }
[MessagePackObject] public sealed class S_WalletUpdate { [Key(0)] public long Rupee; [Key(1)] public long Cash; }
[MessagePackObject] public sealed class S_Error { [Key(0)] public ushort Code; [Key(1)] public string Message = ""; }

[MessagePackObject] public sealed class Empty { }
