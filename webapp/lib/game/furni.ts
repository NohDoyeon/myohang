// 가구 그림을 Pixi 텍스처로 불러온다. 파일 목록은 `furni-files.ts` 에 있다
// (상점 화면도 같은 목록을 보므로 한 곳에만 둔다).

import { Assets, Texture } from "pixi.js";
import { FURNI_BASE, FURNI_FILES } from "./furni-files";

export class FurniArt {
  private constructor(private readonly byKey: Map<string, Texture>) {}

  static async load(base = FURNI_BASE): Promise<FurniArt> {
    const map = new Map<string, Texture>();
    // 한 장이 없다고 전체가 실패하면 안 된다 — 아직 안 그린 가구가 많다.
    const loaded = await Promise.allSettled(
      FURNI_FILES.map(async (name) => {
        const tex = await Assets.load<Texture>(`${base}/${name}.png`);
        tex.source.scaleMode = "nearest";
        return [name, tex] as const;
      }),
    );
    for (const r of loaded) if (r.status === "fulfilled") map.set(r.value[0], r.value[1]);
    return new FurniArt(map);
  }

  get size() {
    return this.byKey.size;
  }

  /**
   * 가구 한 점의 그림. **없으면 단계적으로 물러난다** —
   * 그 방향+상태 → 방향 0 + 상태 → 방향 0 + `default`. 전부 없으면 null(도형으로 그린다).
   *
   * 상태가 있는 가구(화분 4단계, 조명 on/off)는 상태별 그림이 따로 있어야 바뀌는 게 보인다.
   */
  get(furniId: string, dir: number, state: string): Texture | null {
    return (
      this.byKey.get(`${furniId}_${dir}_${state}`) ??
      this.byKey.get(`${furniId}_0_${state}`) ??
      this.byKey.get(`${furniId}_${dir}_default`) ??
      this.byKey.get(`${furniId}_0_default`) ??
      null
    );
  }
}
