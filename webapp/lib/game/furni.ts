// 가구 그림. `public/art/furni/{furniId}_{방향}_{상태}.png` 를 **있는 것만** 불러온다.
//
// 목록을 코드에 적어 두는 이유: 브라우저는 폴더를 훑을 수 없다. 대신 **없으면 조용히 건너뛰므로**
// 여기 이름을 먼저 적어 두고 그림은 나중에 넣어도 된다(그때까지는 도형으로 그려진다).
//
// 키 규칙은 서버·Godot 과 같다. 방향이 없는 그림은 `_0_` 이고, 벽걸이는 `_4_`(북쪽 벽) 이다.

import { Assets, Texture } from "pixi.js";

/** 지금 가진 그림. 새 그림을 넣으면 여기 한 줄 추가한다. */
const FILES = [
  "bookshelf_0_default",
  "chair_wood_0_default",
  "table_round_0_default",
  "rug_round_0_default",
  "plant_pot_0_default",
  "lamp_floor_0_off",
  "fridge_red_0_closed",
  "frame_photo_4_default",
  "flower_pot_0_seed",
  "flower_pot_0_sprout",
  "flower_pot_0_bud",
  "flower_pot_0_bloom",
  "flower_cut_0_default",
];

export class FurniArt {
  private constructor(private readonly byKey: Map<string, Texture>) {}

  static async load(base = "/art/furni"): Promise<FurniArt> {
    const map = new Map<string, Texture>();
    // 한 장이 없다고 전체가 실패하면 안 된다 — 아직 안 그린 가구가 많다.
    const loaded = await Promise.allSettled(
      FILES.map(async (name) => {
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
