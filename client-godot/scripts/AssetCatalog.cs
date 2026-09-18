using Godot;
using FileAccess = Godot.FileAccess;   // ImplicitUsings 의 System.IO.FileAccess 와 충돌 방지

namespace HarborClient;

/// <summary>
/// 아틀라스 키 → Texture2D. `res://art/atlas.json` + `atlas.png` 를 읽는다. 없는 키는 null.
///
/// **PNG 는 Godot 이 '임포트'해 둔 것만 `GD.Load` 로 읽힌다.** 프로젝트 폴더에 파일을 넣는 것만으로는 부족하고
/// 에디터를 한 번 거쳐야 `.import` 와 `.godot/imported/*.ctex` 가 생긴다 (`tools/import-assets.ps1`).
///
/// 어느 단계에서든 실패하면 **캐시를 비운 채 돌아간다.** 그래야 `TryGet` 이 null 을 주고
/// 아바타·가구가 코드로 그린 placeholder 로 되돌아간다. 예전엔 시트가 null 이어도 AtlasTexture 를 만들어
/// 캐시에 넣는 바람에 "스프라이트가 있다"고 오판해 **아무것도 안 그려졌다.**
/// </summary>
public static class AssetCatalog
{
    private const string MetaPath = "res://art/atlas.json";
    private const string SheetPath = "res://art/atlas.png";

    private static readonly Dictionary<string, Texture2D> Cache = new();
    private static bool _loaded;

    /// <summary>스프라이트를 하나라도 읽었는가. false 면 전부 placeholder 로 그린다.</summary>
    public static bool Ready { get; private set; }

    public static Texture2D? TryGet(string key)
    {
        if (!_loaded) Load();
        return Cache.GetValueOrDefault(key);
    }

    private static void Load()
    {
        _loaded = true;

        if (!FileAccess.FileExists(MetaPath))
        {
            GD.Print($"[AssetCatalog] {MetaPath} 없음 — placeholder 모드");
            return;
        }

        // 임포트 안 된 PNG 에 GD.Load 를 걸면 엔진이 긴 오류를 뱉는다. 미리 확인해 조용히 넘어간다.
        if (!ResourceLoader.Exists(SheetPath))
        {
            GD.Print($"[AssetCatalog] {SheetPath} 가 아직 임포트되지 않음 — placeholder 모드. " +
                     "그림을 새로 넣었다면 tools/import-assets.ps1 을 한 번 실행하세요.");
            return;
        }

        var sheet = GD.Load<Texture2D>(SheetPath);
        if (sheet is null)
        {
            GD.PrintErr($"[AssetCatalog] {SheetPath} 로드 실패 — placeholder 모드");
            return;
        }

        var parsed = Json.ParseString(FileAccess.GetFileAsString(MetaPath));
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            GD.PrintErr("[AssetCatalog] atlas.json 을 읽지 못함(형식 오류) — placeholder 모드");
            return;
        }
        var json = parsed.AsGodotDictionary();
        if (!json.ContainsKey("frames"))
        {
            GD.PrintErr("[AssetCatalog] atlas.json 에 frames 가 없음 — placeholder 모드");
            return;
        }

        int count = 0;
        foreach (var kv in json["frames"].AsGodotDictionary())
        {
            var entry = kv.Value.AsGodotDictionary();
            if (!entry.ContainsKey("frame")) continue;
            var f = entry["frame"].AsGodotDictionary();
            Cache[kv.Key.AsString()] = new AtlasTexture
            {
                Atlas = sheet,
                Region = new Rect2((float)f["x"], (float)f["y"], (float)f["w"], (float)f["h"]),
            };
            count++;
        }

        Ready = count > 0;
        if (Ready) GD.Print($"[AssetCatalog] 스프라이트 {count}개 로드 완료");
        else GD.Print("[AssetCatalog] frames 가 비어 있음 — placeholder 모드");
    }
}
