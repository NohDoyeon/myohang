// 도트 아틀라스 로더. `public/art/atlas.png` + `atlas.json` 을 읽어 프레임별 Texture 로 자른다.
//
// 웹에서는 Godot 에서 겪던 "json 이 pck 에 들어갔다 말았다" 문제가 없다 — public/ 의 파일은
// 그냥 그대로 서빙된다. 아틀라스를 새로 만들면 두 파일만 다시 복사하면 된다.
//
// 프레임 키: `avatar/hd/001_{artDir}_{action}_{frame}`   (artDir 0~4, action: stand/walk/sit/sleep/dance)

import { Assets, Rectangle, Texture } from "pixi.js";

interface AtlasJson {
  frames: Record<string, { frame: { x: number; y: number; w: number; h: number } }>;
}

/** ⚠ 시트에는 **방향 0~4만** 있다. 5~7(왼쪽을 보는 방향)은 좌우 반전으로 만든다. */
export const ART_DIRS = 5;

/**
 * 서버 방향(0=북, 시계방향 0~7) → 시트 방향 + 좌우반전 여부.
 *
 * 시트는 **0 = 뒷모습(카메라 반대), 4 = 앞모습(카메라 쪽)** 으로 그려져 있다.
 * 서버도 0 = 북(화면 안쪽) · 4 = 남(화면 앞쪽)이므로 **그대로 맞는다 → 오프셋 0**.
 *
 * (Godot 클라는 `DirOffset = 1` 로 되어 있는데, 그러면 전부 45°씩 틀어진다.
 *  "뒤로 갈 때 왼쪽을 본다"는 증상이 그 모양이었고 끝내 검증되지 않았다. 여기서는 0으로 둔다.)
 */
export function artDirection(dir: number): { artDir: number; flip: boolean } {
  const d = ((dir % 8) + 8) % 8;
  return d <= 4 ? { artDir: d, flip: false } : { artDir: 8 - d, flip: true };
}

export class AvatarAtlas {
  private readonly frames = new Map<string, Texture>();

  private constructor(sheet: Texture, json: AtlasJson) {
    for (const [key, v] of Object.entries(json.frames)) {
      const { x, y, w, h } = v.frame;
      this.frames.set(key, new Texture({ source: sheet.source, frame: new Rectangle(x, y, w, h) }));
    }
  }

  static async load(base = "/art"): Promise<AvatarAtlas> {
    const [sheet, json] = await Promise.all([
      Assets.load<Texture>(`${base}/atlas.png`),
      fetch(`${base}/atlas.json`).then((r) => {
        if (!r.ok) throw new Error(`atlas.json 을 읽지 못했습니다 (${r.status})`);
        return r.json() as Promise<AtlasJson>;
      }),
    ]);
    sheet.source.scaleMode = "nearest";        // 도트가 뭉개지지 않게
    return new AvatarAtlas(sheet, json);
  }

  get size() {
    return this.frames.size;
  }

  /**
   * 한 프레임. **없는 조합은 조용히 대체한다** — 아직 안 그린 동작이 많아서
   * 없을 때 아무것도 안 그리면 캐릭터가 통째로 사라진다.
   * 순서: 그 방향+동작 → 그 방향+stand → 정면+동작 → 정면+stand
   */
  pick(dir: number, action: string, frame = 0): { texture: Texture; flip: boolean } | null {
    const { artDir, flip } = artDirection(dir);
    const key = (d: number, a: string, f: number) => `avatar/hd/001_${d}_${a}_${f}`;

    const found =
      this.frames.get(key(artDir, action, frame)) ??
      this.frames.get(key(artDir, action, 0)) ??
      this.frames.get(key(artDir, "stand", 0)) ??
      this.frames.get(key(4, action, frame)) ??
      this.frames.get(key(4, action, 0)) ??
      this.frames.get(key(4, "stand", 0));

    return found ? { texture: found, flip } : null;
  }
}
