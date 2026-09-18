namespace Harbor.Core;

/// <summary>
/// 방의 바닥·벽 스타일 id 목록. **서버가 검증하고 클라가 그린다** — 그래서 목록은 한 곳에 둔다.
///
/// 지금은 id 하나가 색 조합 하나를 가리키지만(클라의 `FloorColors`·`WallColors`),
/// 나중에 타일 그림이 들어오면 **같은 id 가 스프라이트를 가리키게** 바뀐다. 저장된 방은 그대로 살아남는다.
/// 방 템플릿(`data/rooms/*.json`)이 쓰는 값도 여기 들어 있어야 한다.
/// </summary>
public static class RoomStyles
{
    public static readonly string[] Floors =
    {
        "floor_wood",          // 나무 마루
        "floor_carpet_blue",   // 파란 카펫 (다락방 기본)
        "floor_carpet_moss",   // 초록 카펫 (복층방 기본)
        "floor_plank_warm",    // 따뜻한 널판 (모퉁이집 기본)
        "floor_stone",         // 돌바닥
        "floor_tile",          // 체크 타일
        "floor_grass",         // 잔디
        "floor_deck_wood",     // 갑판 (부둣가 기본)
    };

    public static readonly string[] Walls =
    {
        "wall_wood_01",        // 나무 판자 (실내 기본)
        "wall_plaster",        // 회벽
        "wall_brick",          // 벽돌
        "wall_flower",         // 꽃무늬 벽지
        "wall_ship_rail",      // 난간 (실외)
    };

    public static bool IsFloor(string? id) => id is not null && Array.IndexOf(Floors, id) >= 0;
    public static bool IsWall(string? id) => id is not null && Array.IndexOf(Walls, id) >= 0;
}
