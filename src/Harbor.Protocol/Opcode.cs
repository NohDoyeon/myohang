namespace Harbor.Protocol;

/// <summary>C→S &lt; 0x1000, S→C ≥ 0x1000.</summary>
public enum Opcode : ushort
{
    // Client → Server
    C_Login = 0x0001, C_Ping = 0x0002,
    C_EnterRoom = 0x0010, C_LeaveRoom = 0x0011, C_RoomList = 0x0012, C_UpgradeRoom = 0x0013,
    C_HouseList = 0x0014, C_RemodelRoom = 0x0015,
    C_Move = 0x0020, C_Action = 0x0021, C_SetFigure = 0x0022,
    C_Chat = 0x0030, C_Whisper = 0x0031,
    C_PlaceItem = 0x0040, C_PickItem = 0x0041, C_MoveItem = 0x0042, C_UseItem = 0x0043,
    C_PostitWrite = 0x0050, C_PostitRead = 0x0051,
    C_BuyCatalog = 0x0060, C_Catalog = 0x0061, C_Inventory = 0x0062, C_SellItem = 0x0063, C_LotteryDraw = 0x0070,
    C_MinigameJoin = 0x0080, C_MinigameInput = 0x0081,
    C_Propose = 0x0090, C_ProposeAnswer = 0x0091,

    // Server → Client
    S_LoginResult = 0x1001, S_Pong = 0x1002, S_Notice = 0x1003,
    S_RoomSnapshot = 0x1010, S_UserEnter = 0x1011, S_UserLeave = 0x1012, S_RoomList = 0x1013, S_HouseList = 0x1014,
    S_UserPath = 0x1020, S_UserAction = 0x1021, S_UserFigure = 0x1022,
    S_ChatBubble = 0x1030,
    S_ItemAdd = 0x1040, S_ItemRemove = 0x1041, S_ItemUpdate = 0x1042, S_ItemState = 0x1043,
    S_PostitList = 0x1050,
    S_InventoryUpdate = 0x1060, S_WalletUpdate = 0x1061, S_Catalog = 0x1062, S_Inventory = 0x1063, S_LotteryResult = 0x1070,
    S_MinigameEvent = 0x1080, S_RelationUpdate = 0x1090,
    S_Error = 0x1FFF,
}
