// src/Harbor.Core/{Iso,Heightmap,Walls}.cs 를 그대로 옮긴 것.
// **서버와 값이 어긋나면 클릭한 칸과 실제 이동이 달라진다.** 숫자를 임의로 바꾸지 말 것.

export const TILE_W = 64;
export const TILE_H = 32;
export const STACK_PX = 8;

export interface Screen { sx: number; sy: number }

export const toScreen = (x: number, y: number, z = 0): Screen => ({
  sx: (x - y) * (TILE_W / 2),
  sy: (x + y) * (TILE_H / 2) - z * STACK_PX,
});

export const toWorld = (sx: number, sy: number) => {
  const fx = (sx / (TILE_W / 2) + sy / (TILE_H / 2)) / 2;
  const fy = (sy / (TILE_H / 2) - sx / (TILE_W / 2)) / 2;
  return { x: Math.floor(fx), y: Math.floor(fy) };
};

/** 렌더 정렬 키. 클수록 앞. Pixi 의 zIndex 에 그대로 넣는다. */
export const depthKey = (x: number, y: number, z = 0, layer = 0) =>
  (x + y) * 1000 + Math.trunc(z * 10) + layer;

/** from→to 인접 이동의 방향 (0=북, 시계방향 0~7). */
export function directionBetween(fx: number, fy: number, tx: number, ty: number): number {
  const dx = Math.sign(tx - fx), dy = Math.sign(ty - fy);
  if (dx === 0 && dy === -1) return 0;
  if (dx === 1 && dy === -1) return 1;
  if (dx === 1 && dy === 0) return 2;
  if (dx === 1 && dy === 1) return 3;
  if (dx === 0 && dy === 1) return 4;
  if (dx === -1 && dy === 1) return 5;
  if (dx === -1 && dy === 0) return 6;
  if (dx === -1 && dy === -1) return 7;
  return 4;
}

/** 문자열 행 기반 높이맵. 'x' = 이동불가, '0'~'9'·'a'~'z' = 높이. */
export class Heightmap {
  readonly w: number;
  readonly h: number;
  private readonly height: Float32Array;
  private readonly walk: Uint8Array;

  constructor(rows: readonly string[]) {
    if (rows.length === 0) throw new Error("빈 높이맵");
    this.h = rows.length;
    this.w = rows[0].length;
    this.height = new Float32Array(this.w * this.h);
    this.walk = new Uint8Array(this.w * this.h);

    for (let y = 0; y < this.h; y++) {
      if (rows[y].length !== this.w) throw new Error(`${y}행 길이가 다릅니다`);
      for (let x = 0; x < this.w; x++) {
        const c = rows[y][x].toLowerCase();
        const i = y * this.w + x;
        this.walk[i] = c === "x" ? 0 : 1;
        if (c === "x") this.height[i] = 0;
        else if (c >= "0" && c <= "9") this.height[i] = c.charCodeAt(0) - 48;
        else if (c >= "a" && c <= "z") this.height[i] = c.charCodeAt(0) - 97 + 10;
        else throw new Error(`알 수 없는 타일 문자 '${c}' (${x},${y})`);
      }
    }
  }

  inBounds = (x: number, y: number) => x >= 0 && y >= 0 && x < this.w && y < this.h;
  walkable = (x: number, y: number) => this.inBounds(x, y) && this.walk[y * this.w + x] === 1;
  heightAt = (x: number, y: number) => (this.inBounds(x, y) ? this.height[y * this.w + x] : 0);
}

/**
 * 어느 타일에 벽을 그릴지. 2:1 아이소에서 **화면 뒤쪽 두 면만** 그린다.
 *
 * ⚠ 직관적으로 "가장자리면 벽"이라고 쓰면 틀린다. 정확한 규칙은
 * **그 방향의 사각형 영역 전체에 걸을 수 있는 타일이 하나도 없을 때만** 벽이다.
 * 그래서 2D 누적 접두(prefix) 배열을 쓴다 — 문처럼 앞으로 튀어나온 타일에 벽이 생겨
 * 뒤를 가리는 일을 막는다.
 */
export class Walls {
  private readonly any: Uint8Array;   // (0..x, 0..y) 안에 걸을 수 있는 타일이 있는가
  private readonly stride: number;

  constructor(private readonly map: Heightmap) {
    this.stride = map.w + 1;
    this.any = new Uint8Array(this.stride * (map.h + 1));
    for (let y = 0; y < map.h; y++)
      for (let x = 0; x < map.w; x++)
        this.any[(y + 1) * this.stride + (x + 1)] =
          map.walkable(x, y) || this.any[(y + 1) * this.stride + x] || this.any[y * this.stride + (x + 1)] ? 1 : 0;
  }

  private anyWalkable(x: number, y: number): boolean {
    if (x < 0 || y < 0) return false;
    const cx = Math.min(x, this.map.w - 1) + 1;
    const cy = Math.min(y, this.map.h - 1) + 1;
    return this.any[cy * this.stride + cx] === 1;
  }

  /** 타일의 좌상단(-x) 모서리에 벽. 벽걸이 가구 dir 2. */
  west = (x: number, y: number) => this.map.walkable(x, y) && !this.anyWalkable(x - 1, y);
  /** 타일의 우상단(-y) 모서리에 벽. 벽걸이 가구 dir 4. */
  north = (x: number, y: number) => this.map.walkable(x, y) && !this.anyWalkable(x, y - 1);
}

export const WALL_SLOT_COLS = 2;
