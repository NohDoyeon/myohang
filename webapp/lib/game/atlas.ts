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
 * **여기서 두 번 틀렸다. 이유를 적어 둔다.**
 *
 * 1) 시트는 `0 = 뒤통수 … 4 = 정면` 이고, 사이 세 장은 **왼쪽을 보며** 돈다
 *    (얼굴이 왼쪽에서부터 드러난다 — atlas.png 를 확대해 확인).
 *
 * 2) 틀렸던 것: "서버 0 = 북이니 art 0 = 뒷모습"이라고 뒀다. **카메라가 45° 돌아가 있다.**
 *    투영이 `sx = (x-y)·32, sy = (x+y)·16` 이므로
 *      dir 7 (-1,-1) → sx 0, sy -32  = 화면에서 곧게 위   → **카메라에서 가장 멀다 = 뒷모습**
 *      dir 3 (+1,+1) → sx 0, sy +32  = 화면에서 곧게 아래 → **카메라와 가장 가깝다 = 정면**
 *    즉 art 0 ↔ dir 7, art 4 ↔ dir 3. 시트 번호는 **`7 - dir`** 로 읽어야 한다.
 *
 * 3) 좌우 반전의 기준도 월드 축이 아니라 **화면 좌우**다. 화면을 좌우로 뒤집으면
 *    dir d 는 `6 - d` 가 된다(위·아래는 제자리). 그래서 오른쪽 절반만 반전해 만든다.
 *
 * 결과:
 * | 화면 | dir | art | 반전 |
 * |---|---|---|---|
 * | 위   | 7 | 0 | — |
 * | 아래 | 3 | 4 | — |
 * | 왼쪽 | 5 | 2 | — |
 * | 오른쪽 | 1 | 2 | 반전 |
 */
export function artDirection(dir: number): { artDir: number; flip: boolean } {
  const d = ((dir % 8) + 8) % 8;
  const s = (7 - d + 8) % 8;                 // 시트 번호 기준으로 옮긴 방향
  return s <= 4 ? { artDir: s, flip: false } : { artDir: 8 - s, flip: true };
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

  /** 시트 번호로 **그대로** 꺼낸다(대체 없음). 원본 확인용 — 게임에서는 `pick` 을 쓴다. */
  raw(artDir: number, action: string, frame = 0): Texture | null {
    return this.frames.get(`avatar/hd/001_${artDir}_${action}_${frame}`) ?? null;
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
