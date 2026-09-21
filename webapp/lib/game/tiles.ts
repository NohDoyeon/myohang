// 바닥 타일 그림. `public/art/floor/*.png` 는 **정확히 64×32** 여야 한다(받은 6장 모두 확인됨).
//
// 서버는 스타일 **id** 만 보낸다(`room.floorStyle`, 목록은 Harbor.Core/RoomStyles.cs).
// 그림이 없는 id 는 가장 가까운 것으로 대체한다 — **없다고 바닥을 안 그리면 방이 통째로 사라진다.**

import { Assets, Texture } from "pixi.js";

/** 실제로 가진 그림 파일. */
const FILES = ["wood", "carpet", "stone", "tile", "grass", "sand"] as const;
type FileName = (typeof FILES)[number];

/**
 * 서버 스타일 id → 그림 파일.
 * 아직 전용 그림이 없는 것은 비슷한 것으로 보낸다. 나중에 그림이 생기면 이 표만 고치면 된다.
 */
const STYLE_TO_FILE: Record<string, FileName> = {
  floor_wood: "wood",
  floor_carpet_blue: "carpet",
  floor_carpet_moss: "grass",      // 전용 그림 없음 — 초록 계열로 대체
  floor_plank_warm: "wood",        // 전용 그림 없음
  floor_stone: "stone",
  floor_tile: "tile",
  floor_grass: "grass",
  floor_deck_wood: "wood",         // 전용 그림 없음 (갑판)
};

const DEFAULT: FileName = "wood";

/** 벽 무늬. **32×48** 이고 세로로 반복해 벽 높이(3칸 = 96px)를 채운다 — 정확히 2번. */
const WALL_FILES = ["plaster", "wood", "brick", "floral", "window"] as const;
type WallFile = (typeof WALL_FILES)[number];

const WALL_STYLE_TO_FILE: Record<string, WallFile> = {
  wall_wood_01: "wood",
  wall_plaster: "plaster",
  wall_brick: "brick",
  wall_flower: "floral",
  wall_ship_rail: "plaster",       // 전용 그림 없음 (실외 난간)
};

const WALL_DEFAULT: WallFile = "plaster";

export class WallTiles {
  private constructor(private readonly byFile: Map<WallFile, Texture>) {}

  static async load(base = "/art/wall"): Promise<WallTiles> {
    const entries = await Promise.all(
      WALL_FILES.map(async (name) => {
        const tex = await Assets.load<Texture>(`${base}/wall_${name}.png`);
        tex.source.scaleMode = "nearest";
        return [name, tex] as const;
      }),
    );
    return new WallTiles(new Map(entries));
  }

  get size() {
    return this.byFile.size;
  }

  get(styleId: string): Texture {
    const file = WALL_STYLE_TO_FILE[styleId] ?? WALL_DEFAULT;
    return this.byFile.get(file) ?? this.byFile.get(WALL_DEFAULT)!;
  }
}

export class FloorTiles {
  private constructor(private readonly byFile: Map<FileName, Texture>) {}

  static async load(base = "/art/floor"): Promise<FloorTiles> {
    const entries = await Promise.all(
      FILES.map(async (name) => {
        const tex = await Assets.load<Texture>(`${base}/floor_${name}.png`);
        tex.source.scaleMode = "nearest";
        return [name, tex] as const;
      }),
    );
    return new FloorTiles(new Map(entries));
  }

  get size() {
    return this.byFile.size;
  }

  /** 스타일 id 로 텍스처. 모르는 id 여도 항상 하나는 돌려준다. */
  get(styleId: string): Texture {
    const file = STYLE_TO_FILE[styleId] ?? DEFAULT;
    return this.byFile.get(file) ?? this.byFile.get(DEFAULT)!;
  }
}
