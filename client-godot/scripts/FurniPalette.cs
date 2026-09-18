using Godot;

namespace HarborClient;

/// <summary>
/// 클라이언트 placeholder 렌더용 가구 크기/색 표 (아틀라스 전까지). 표시 이름·가격·목록은 서버(카탈로그/가방)가 준다.
/// 표에 없는 가구는 id 해시 색 + 기본 크기로 그린다.
/// HeightPx: 바닥 가구는 높이, 벽걸이는 세로 크기.
/// </summary>
public static class FurniPalette
{
    /// <summary>placeholder 로 어떤 모양을 그릴지. 아틀라스가 들어오면 전부 스프라이트로 대체된다.</summary>
    public enum Shape { Box, Chair, Table, Rug, Plant, Flower, Lamp, Can, Panel, Window, Frame, Clock, Postit, Planter }

    public sealed record Entry(string Id, string Name, int HeightPx, Color Tint, Shape Shape = Shape.Box);

    public static readonly Entry[] All =
    {
        // 바닥
        new("fridge_red",    "빨간 냉장고",   40, new("a03030")),
        new("bookshelf",     "책장",          46, new("6e4a34")),
        new("tv_crt",        "브라운관 TV",   22, new("3a2e3e")),
        new("chair_wood",    "나무 의자",     20, new("ac7850"), Shape.Chair),
        new("table_round",   "원형 탁자",     22, new("c89a6a"), Shape.Table),
        new("rug_round",     "동그란 러그",    3, new("58789f"), Shape.Rug),
        new("plant_pot",     "화분",          30, new("649658"), Shape.Plant),
        new("flower_pot",    "캣닢 화분",     30, new("649658"), Shape.Plant),
        new("flower_cut",    "캣닢 잎",       18, new("7fbf5c"), Shape.Flower),
        new("gift_planter",  "선물 화분",     34, new("6fae52"), Shape.Planter),
        new("lamp_floor",    "스탠드 조명",   38, new("e6b432"), Shape.Lamp),
        new("cola_can",      "우유팩",        10, new("f6f2e8"), Shape.Can),   // id 는 저장 호환 때문에 유지
        new("cola_machine",  "우유 자판기",   44, new("6f93b8")),
        // 벽
        new("aircon_wall",   "벽걸이 에어컨", 14, new("b0b0b0"), Shape.Panel),
        new("window_round",  "둥근 창문",     22, new("8ec6d8"), Shape.Window),
        new("frame_photo",   "사진 액자",     18, new("c86e8c"), Shape.Frame),
        new("clock_wall",    "벽시계",        16, new("e4e4e4"), Shape.Clock),
        new("postit_yellow", "노란 포스트잇", 16, new("f5e26a"), Shape.Postit),
        new("postit_pink",   "분홍 포스트잇", 16, new("f4a6c0"), Shape.Postit),
        new("postit_mint",   "민트 포스트잇", 16, new("a6e6c8"), Shape.Postit),
    };

    public static Entry? Find(string id) => Array.Find(All, e => e.Id == id);
    public static string NameOf(string id) => Find(id)?.Name ?? id;

    /// <summary>눈에 띄어야 하는 상태만 한글 배지로. 기본 상태(closed/off/full/default/blank)는 빈 문자열.</summary>
    public static string StateLabel(string state) => state switch
    {
        "open" => "열림",
        "on" => "켜짐",
        "empty" => "빈 팩",
        "serving" => "덜컹!",
        "seed" => "씨앗",
        "sprout" => "새싹",
        "bud" => "꽃봉오리",
        "bloom" => "활짝!",
        // 선물 화분 — 시간이 아니라 사람이 채운다. ("empty" 는 빈 우유팩이 이미 쓰고 있어 "bare" 를 쓴다)
        "bare" => "빈 화분",
        "few" => "조금 모임",
        "half" => "자라는 중",
        "almost" => "거의 다!",
        "full" => "가득! 수확 가능",
        _ => "",
    };

    public static string ActionBadge(string action) => action switch
    {
        "dance" => "♪ ♪",
        "wave" => "안녕!",
        "laugh" => "ㅋㅋㅋ",
        "cry" => "ㅠㅠ",
        "angry" => "!!",
        "sleep" => "z z Z",
        "drink" => "꿀꺽",
        _ => "",
    };
}
