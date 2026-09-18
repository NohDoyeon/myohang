using Harbor.Server.Data;
using Harbor.Server.Net;

namespace Harbor.Server.Rooms;

/// <summary>룸 루프에 투입되는 명령. 룸 상태는 루프 스레드에서만 변경.</summary>
public abstract record RoomCommand
{
    public sealed record Enter(Session S) : RoomCommand;
    public sealed record Leave(Session S) : RoomCommand;
    public sealed record Move(Session S, int X, int Y) : RoomCommand;
    public sealed record Action(Session S, string Name) : RoomCommand;
    public sealed record Figure(Session S) : RoomCommand;
    public sealed record Chat(Session S, string Text, byte Kind) : RoomCommand;
    public sealed record PlaceItem(Session S, string FurniId, int X, int Y, byte Dir, byte WallU = 0, byte WallV = 0, sbyte Tilt = 0) : RoomCommand;
    public sealed record PickItem(Session S, long ItemId) : RoomCommand;
    public sealed record UseItem(Session S, long ItemId) : RoomCommand;
    /// <summary>남의 방 화분에 캣닢을 꽂는다(선물). 배치가 아니라 이미 놓인 가구에 대한 상호작용이다.</summary>
    public sealed record OfferItem(Session S, long ItemId) : RoomCommand;
    public sealed record PostitWrite(Session S, long ItemId, string Body) : RoomCommand;
    /// <summary>방을 넓혀 새 인스턴스로 갈아탈 때 — 안에 있던 사람을 전부 Target 으로 옮긴다.</summary>
    public sealed record Evacuate(RoomInstance Target, string Reason) : RoomCommand;
    /// <summary>
    /// 방 갈아타기 — 넓히기(가구를 그대로 넘김)와 이사(가구를 주인 가방으로 회수)가 같은 경로를 쓴다.
    /// 룸 루프에서 실행해야 가구 목록이 안전하고, 옛 방이 새 방의 저장을 덮어쓰지 않는다.
    /// 새 방을 못 만들면 Refund 만큼 되돌려 준다(요청이 겹쳤을 때 루피만 사라지지 않게).
    /// </summary>
    /// <param name="ReturnItems">true = 이사. 놓여 있던 가구를 전부 S 의 가방으로 돌려주고 새 방은 빈 채로 시작한다.</param>
    public sealed record Upgrade(Session S, string Reason, long Refund, Func<IReadOnlyList<ItemSave>, RoomInstance?> Build,
                                 bool ReturnItems = false) : RoomCommand;
    public sealed record ItemTimer(long ItemId, string ExpectedState) : RoomCommand;
    public sealed record Tick : RoomCommand;
}
