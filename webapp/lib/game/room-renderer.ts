// 단계 2~3 — 방을 그리고 사람을 움직인다. 아직 도트 대신 도형으로 그린다.
//
// 아트가 없어도 **배치와 움직임이 맞는지**는 여기서 다 판정된다. 스프라이트는 이 구조를 바꾸지 않고 얹는다.
//
// 두 가지가 이 파일의 핵심이다:
//  1. 깊이 정렬은 `depthKey` — 아이소메트릭은 화면 y 가 아니라 **타일 좌표 합(x+y)** 이 앞뒤를 정한다.
//  2. 이동은 **서버가 준 경로를 클라가 보간**한다. 서버는 칸 단위로만 말하고(S_UserPath),
//     칸당 시간은 `room.moveMs` 다. 이 값을 안 쓰고 임의 속도로 움직이면 서버 위치와 조금씩 어긋난다.

import { Application, Container, Graphics, Sprite, Text, TilingSprite } from "pixi.js";
import type { ItemDto, RoomSnapshot, TilePos, UserDto } from "@/lib/protocol/packets";
import { Heightmap, TILE_H, TILE_W, Walls, depthKey, directionBetween, toScreen, toWorld } from "./iso";
import { AvatarAtlas } from "./atlas";
import { FloorTiles, WallTiles } from "./tiles";

const COLOR = {
  floorA: 0x8c7a63, floorB: 0x7d6c57, floorEdge: 0x5f5344,
  // 그림을 쓸 때는 **tint** 로 곱해지므로 흰색에 가까워야 원색이 산다.
  wallNorth: 0xffffff, wallWest: 0xd6cec4,
  door: 0xc98c4b,
  item: 0x6f8f6a, itemWall: 0x7a7f9b,
  body: 0xf0e2cf, bodyEdge: 0x3a2f28, face: 0x3a2f28,
  hover: 0xffffff,
} as const;

export interface RoomRendererEvents {
  onTileClick?: (x: number, y: number) => void;
  /** 그림 로딩 결과. 0 이면 그 부분은 도형으로 그린다. */
  onAtlas?: (frames: number, floorTiles: number, wallTiles: number, error?: string) => void;
}

/** 화면에 보이는 사람 하나. 서버 좌표(정수 칸)와 별개로 **떠 있는 위치**(fx,fy)를 들고 움직인다. */
interface Avatar {
  dto: UserDto;
  node: Container;
  /** 도트가 있으면 스프라이트, 없으면 도형. 아트 로딩 실패로 사람이 사라지면 안 된다. */
  sprite: Sprite | null;
  /** 걷기 프레임 전환용 누적 시간. */
  animMs: number;
  /** 머리 위 말풍선. 새 말을 하면 이전 것을 지운다. */
  bubble: Container | null;
  bubbleMs: number;
  fx: number; fy: number;          // 지금 화면상 위치 (칸 단위, 소수)
  path: TilePos[];                 // 남은 경로
  legMs: number;                   // 지금 칸으로 건너간 지 얼마나 됐나
  /** 이 다리(leg)의 출발 칸. **보간 중 fx 를 반올림해 쓰면 안 된다** — 중간에 다음 칸으로 넘어가 순간이동한다. */
  legFromX: number; legFromY: number;
  dir: number;
  action: string;
}

export class RoomRenderer {
  private app: Application | null = null;
  private world = new Container();
  private floor = new Container();
  private walls = new Container();
  private objects = new Container();   // 가구 + 사람. 같은 층에서 깊이 정렬돼야 하므로 한 컨테이너.
  private hover = new Graphics();

  private map: Heightmap | null = null;
  private moveMs = 240;
  private avatars = new Map<number, Avatar>();
  private atlas: AvatarAtlas | null = null;
  private tiles: FloorTiles | null = null;
  private wallTiles: WallTiles | null = null;

  constructor(private readonly events: RoomRendererEvents = {}) {}

  async mount(el: HTMLElement): Promise<void> {
    const app = new Application();
    await app.init({ background: 0x171614, resizeTo: el, antialias: false });
    el.appendChild(app.canvas);

    // 도트는 **있으면 쓰고 없으면 도형으로 간다.** 여기서 실패해도 방은 그려져야 한다.
    // 캐릭터와 바닥은 따로 잡는다 — 한쪽이 없다고 다른 쪽까지 도형이 될 이유가 없다.
    const [atlas, tiles, walls] = await Promise.allSettled([
      AvatarAtlas.load(), FloorTiles.load(), WallTiles.load(),
    ]);
    if (atlas.status === "fulfilled") this.atlas = atlas.value;
    if (tiles.status === "fulfilled") this.tiles = tiles.value;
    if (walls.status === "fulfilled") this.wallTiles = walls.value;

    const failed = [atlas, tiles, walls].find((r) => r.status === "rejected");
    this.events.onAtlas?.(
      this.atlas?.size ?? 0,
      this.tiles?.size ?? 0,
      this.wallTiles?.size ?? 0,
      failed?.status === "rejected" ? String(failed.reason) : undefined,
    );

    this.objects.sortableChildren = true;
    this.world.addChild(this.floor, this.walls, this.objects, this.hover);
    app.stage.addChild(this.world);

    app.stage.eventMode = "static";
    app.stage.hitArea = app.screen;
    app.stage.on("pointermove", (e) => this.drawHover(this.pick(e.global.x, e.global.y)));
    app.stage.on("pointertap", (e) => {
      const t = this.pick(e.global.x, e.global.y);
      if (this.map?.walkable(t.x, t.y)) this.events.onTileClick?.(t.x, t.y);
    });

    app.ticker.add((tick) => this.step(tick.deltaMS));
    this.app = app;
  }

  private pick(gx: number, gy: number) {
    const p = this.world.toLocal({ x: gx, y: gy });
    return toWorld(p.x, p.y);
  }

  // ---------- 방 통째로 ----------
  render(snap: RoomSnapshot): void {
    if (!this.app) return;
    this.map = new Heightmap(snap.room.heightmap);
    this.moveMs = Math.max(1, snap.room.moveMs);

    this.drawFloor(snap);
    this.drawWalls(Math.max(1, snap.room.wallHeight), snap.room.wallStyle);
    this.drawItems(snap.items);

    // 사람은 지우고 다시 만들지 않는다 — 그러면 걷던 사람이 매번 제자리로 튄다.
    const seen = new Set<number>();
    for (const u of snap.users) { this.upsertUser(u); seen.add(u.id); }
    for (const id of [...this.avatars.keys()]) if (!seen.has(id)) this.removeUser(id);

    this.center();
  }

  private drawFloor(snap: RoomSnapshot) {
    const m = this.map!;
    this.floor.removeChildren();

    // 그림이 있으면 타일 한 장씩 깐다. 텍스처가 정확히 64×32 라 앵커 가운데면 좌표가 그대로 맞는다.
    const texture = this.tiles?.get(snap.room.floorStyle);
    if (texture) {
      for (let y = 0; y < m.h; y++) {
        for (let x = 0; x < m.w; x++) {
          if (!m.walkable(x, y)) continue;
          const { sx, sy } = toScreen(x, y, m.heightAt(x, y));
          const s = new Sprite(texture);
          s.anchor.set(0.5);
          s.position.set(sx, sy);
          // 문 칸만 살짝 물들여 표시한다. 별도 그림을 만들 필요가 없다.
          if (x === snap.room.doorX && y === snap.room.doorY) s.tint = COLOR.door;
          this.floor.addChild(s);
        }
      }
      return;
    }

    // 그림이 없을 때 — 도형으로라도 그린다.
    const g = new Graphics();
    for (let y = 0; y < m.h; y++) {
      for (let x = 0; x < m.w; x++) {
        if (!m.walkable(x, y)) continue;
        const { sx, sy } = toScreen(x, y, m.heightAt(x, y));
        const isDoor = x === snap.room.doorX && y === snap.room.doorY;
        g.poly(diamond(sx, sy))
          .fill({ color: isDoor ? COLOR.door : (x + y) % 2 === 0 ? COLOR.floorA : COLOR.floorB })
          .stroke({ color: COLOR.floorEdge, width: 1, alpha: 0.5 });
      }
    }
    this.floor.addChild(g);
  }

  private drawWalls(wallHeight: number, wallStyle: string) {
    const m = this.map!;
    const w = new Walls(m);
    const wallPx = wallHeight * TILE_H;
    const texture = this.wallTiles?.get(wallStyle);
    const g = texture ? null : new Graphics();

    this.walls.removeChildren();

    // 뒤(x+y 가 작은 쪽)부터 그려야 벽끼리 겹칠 때 앞엣것이 위로 온다.
    for (let sum = 0; sum <= m.w + m.h; sum++) {
      for (let y = 0; y < m.h; y++) {
        const x = sum - y;
        if (x < 0 || x >= m.w || !m.walkable(x, y)) continue;
        const { sx, sy } = toScreen(x, y, m.heightAt(x, y));

        // 북쪽 벽 — 타일의 우상단 모서리. 왼쪽보다 오른쪽이 16px 내려간다.
        if (w.north(x, y)) {
          if (texture) this.walls.addChild(wallPanel(texture, sx, sy - TILE_H / 2 - wallPx, wallPx, +1, COLOR.wallNorth));
          else g!.poly([sx, sy - TILE_H / 2, sx + TILE_W / 2, sy, sx + TILE_W / 2, sy - wallPx, sx, sy - TILE_H / 2 - wallPx])
            .fill({ color: COLOR.wallNorth });
        }
        // 서쪽 벽 — 좌상단 모서리. 오른쪽이 16px **올라간다**.
        if (w.west(x, y)) {
          if (texture) this.walls.addChild(wallPanel(texture, sx - TILE_W / 2, sy - wallPx, wallPx, -1, COLOR.wallWest));
          else g!.poly([sx - TILE_W / 2, sy, sx, sy - TILE_H / 2, sx, sy - TILE_H / 2 - wallPx, sx - TILE_W / 2, sy - wallPx])
            .fill({ color: COLOR.wallWest });
        }
      }
    }
    if (g) this.walls.addChild(g);
  }

  private drawItems(items: ItemDto[]) {
    for (const c of [...this.objects.children]) if (c.label?.startsWith("item:")) c.destroy();
    for (const it of items) {
      const g = new Graphics();
      g.label = `item:${it.id}`;
      const { sx, sy } = toScreen(it.x, it.y, it.z);
      if (it.wall) {
        const up = (it.wallV + 0.5) * TILE_H;
        g.rect(sx - 10, sy - up - 10, 20, 20).fill({ color: COLOR.itemWall });
      } else {
        g.poly(diamond(sx, sy, 0.7)).fill({ color: COLOR.item });
        g.rect(sx - 8, sy - 18, 16, 18).fill({ color: COLOR.item, alpha: 0.85 });
      }
      g.zIndex = depthKey(it.x, it.y, it.z, it.wall ? -1 : 0);
      this.objects.addChild(g);
    }
  }

  // ---------- 사람 ----------
  upsertUser(u: UserDto): void {
    const found = this.avatars.get(u.id);
    if (found) { found.dto = u; return; }

    const node = new Container();
    node.label = `user:${u.id}`;
    node.addChild(drawShadow());

    let sprite: Sprite | null = null;
    if (this.atlas) {
      sprite = new Sprite();
      sprite.anchor.set(0.5, 1);      // **발 기준.** 아틀라스가 발을 아래-가운데로 맞춰 놨다
      node.addChild(sprite);
    } else {
      node.addChild(drawBody());
    }
    this.objects.addChild(node);

    const a: Avatar = {
      dto: u, node, sprite, animMs: 0, bubble: null, bubbleMs: 0,
      fx: u.x, fy: u.y, path: [], legMs: 0,
      legFromX: u.x, legFromY: u.y, dir: u.dir, action: u.action,
    };
    this.avatars.set(u.id, a);
    this.applyFrame(a);
    this.place(a);
  }

  removeUser(id: number): void {
    const a = this.avatars.get(id);
    if (!a) return;
    a.node.destroy({ children: true });
    this.avatars.delete(id);
  }

  /** 서버가 준 경로. 지금 위치에서 이어 걷는다. */
  setPath(id: number, path: TilePos[]): void {
    const a = this.avatars.get(id);
    if (!a) return;
    // **이미 서 있는 칸은 버린다.** 서버는 자기 틱에 맞춰 움직이므로, 클라가 먼저 도착해 있으면
    // 경로의 첫 칸이 지금 칸과 같아진다. 그대로 두면 dx=dy=0 이라 방향이 기본값(정면)으로 떨어져
    // **한 칸마다 정면을 한 번씩 보는** 현상이 생긴다("도리도리").
    const here = { x: Math.round(a.fx), y: Math.round(a.fy) };
    let i = 0;
    while (i < path.length && path[i].x === here.x && path[i].y === here.y) i++;

    a.path = path.slice(i);
    a.legMs = 0;
    a.legFromX = a.fx; a.legFromY = a.fy;     // 걷던 중이면 그 자리에서 이어 간다
  }

  /**
   * 머리 위에 말풍선을 띄운다. 잠시 뒤 스스로 사라진다.
   *
   * 글자는 **Pixi 의 Text 로 캔버스에 그린다** — DOM 이 아니므로 HTML 이 섞여 들어와도
   * 태그로 해석될 여지가 없다. 채팅 로그(React)도 기본 텍스트 렌더링이라 자동 이스케이프된다.
   */
  say(id: number, text: string): void {
    const a = this.avatars.get(id);
    if (!a || text.length === 0) return;

    a.bubble?.destroy({ children: true });
    a.bubble = makeBubble(text);
    a.bubbleMs = 0;
    a.node.addChild(a.bubble);
  }

  /** 그 사람이 지금 서 있는(또는 향하는) 칸. 키보드 이동의 기준점이다. */
  tileOf(id: number): { x: number; y: number } | null {
    const a = this.avatars.get(id);
    if (!a) return null;
    // 걷는 중이면 **목표 칸**을 기준으로 삼는다. 보간 중인 소수 위치를 반올림하면
    // 키를 연타할 때 같은 칸으로 되돌아가는 명령이 섞인다.
    const last = a.path.at(-1);
    return last ? { x: last.x, y: last.y } : { x: Math.round(a.fx), y: Math.round(a.fy) };
  }

  isWalkable(x: number, y: number): boolean {
    return this.map?.walkable(x, y) ?? false;
  }

  setAction(id: number, action: string, dir: number): void {
    const a = this.avatars.get(id);
    if (!a) return;
    a.action = action;
    a.dir = dir;
    a.animMs = 0;
    this.applyFrame(a);
    this.place(a);
  }

  /** 매 프레임 — 경로를 따라 보간한다. */
  private step(deltaMs: number): void {
    for (const a of this.avatars.values()) {
      // 말풍선은 걷든 서 있든 사라져야 한다 — 이동 처리보다 먼저 본다.
      if (a.bubble) {
        a.bubbleMs += deltaMs;
        if (a.bubbleMs > BUBBLE_MS) { a.bubble.destroy({ children: true }); a.bubble = null; }
        else if (a.bubbleMs > BUBBLE_MS - 400) a.bubble.alpha = (BUBBLE_MS - a.bubbleMs) / 400;
        // 캐릭터가 뒤집혀도 글자는 뒤집히면 안 된다.
        if (a.bubble && a.sprite) a.bubble.scale.x = 1;
      }

      if (a.path.length === 0) continue;

      a.legMs += deltaMs;
      const next = a.path[0];
      const t = Math.min(1, a.legMs / this.moveMs);

      // 제자리(dx=dy=0)면 방향을 **바꾸지 않는다.** directionBetween 은 그럴 때 4(정면)로 떨어지는데,
      // 그걸 그대로 쓰면 멈칫할 때마다 고개가 앞으로 돌아간다.
      const moved = Math.round(a.legFromX) !== next.x || Math.round(a.legFromY) !== next.y;
      if (moved) a.dir = directionBetween(a.legFromX, a.legFromY, next.x, next.y);
      a.fx = a.legFromX + (next.x - a.legFromX) * t;
      a.fy = a.legFromY + (next.y - a.legFromY) * t;

      if (t >= 1) {
        a.fx = next.x; a.fy = next.y;
        a.legFromX = next.x; a.legFromY = next.y;    // 다음 다리의 출발점
        a.path.shift();
        a.legMs = 0;
      }
      a.action = a.path.length > 0 ? "walk" : "stand";
      a.animMs += deltaMs;
      this.applyFrame(a);
      this.place(a);
    }
  }

  /** 방향·동작에 맞는 프레임을 고른다. 걷기는 두 장을 한 칸 가는 동안 번갈아 쓴다. */
  private applyFrame(a: Avatar): void {
    if (!a.sprite || !this.atlas) return;
    const walking = a.action === "walk";
    const frame = walking ? (Math.floor(a.animMs / (this.moveMs / 2)) % 2) : 0;

    const got = this.atlas.pick(a.dir, a.action, frame);
    if (!got) return;
    a.sprite.texture = got.texture;
    a.sprite.scale.x = got.flip ? -1 : 1;     // 방향 5~7 은 시트에 없어서 좌우로 뒤집는다
  }

  private place(a: Avatar): void {
    const m = this.map;
    const z = m ? m.heightAt(Math.round(a.fx), Math.round(a.fy)) : 0;
    const { sx, sy } = toScreen(a.fx, a.fy, z);
    a.node.position.set(sx, sy);
    a.node.zIndex = depthKey(Math.round(a.fx), Math.round(a.fy), z, 1);
    // 반전은 스프라이트가 스스로 한다(applyFrame). 노드를 뒤집으면 그림자까지 같이 뒤집힌다.
    if (!a.sprite) a.node.scale.x = a.dir >= 5 ? -1 : 1;
  }

  private drawHover(t: { x: number; y: number }) {
    this.hover.clear();
    if (!this.map?.walkable(t.x, t.y)) return;
    const { sx, sy } = toScreen(t.x, t.y, this.map.heightAt(t.x, t.y));
    this.hover.poly(diamond(sx, sy)).stroke({ color: COLOR.hover, width: 2, alpha: 0.7 });
  }

  private center() {
    const app = this.app!;
    const bounds = this.floor.getLocalBounds();
    const pad = 64;
    const scale = Math.min(1.6, Math.max(0.35,
      Math.min((app.screen.width - pad) / Math.max(1, bounds.width),
               (app.screen.height - pad) / Math.max(1, bounds.height))));
    this.world.scale.set(scale);
    this.world.position.set(
      app.screen.width / 2 - (bounds.x + bounds.width / 2) * scale,
      app.screen.height / 2 - (bounds.y + bounds.height / 2) * scale,
    );
  }

  destroy(): void {
    this.app?.destroy(true, { children: true });
    this.app = null;
    this.avatars.clear();
  }
}

/**
 * 벽 한 칸. 타일 무늬(32×48)를 세로로 반복해 벽 높이를 채우고, **기울여** 타일 모서리에 맞춘다.
 *
 * 기울기는 `atan(0.5)` 다 — 2:1 아이소메트릭에서 가로 32를 가면 세로가 정확히 16 움직이기 때문이다.
 * 이 값을 눈대중으로 넣으면 벽과 바닥 사이에 틈이 생기거나 겹친다.
 *
 * @param lean +1 = 오른쪽으로 갈수록 내려감(북쪽 벽), -1 = 올라감(서쪽 벽)
 */
function wallPanel(texture: Texture, x: number, y: number, height: number, lean: 1 | -1, tint: number): TilingSprite {
  const s = new TilingSprite({ texture, width: TILE_W / 2, height });
  s.position.set(x, y);
  s.skew.y = lean * Math.atan(TILE_H / TILE_W);
  s.tint = tint;                 // 두 면의 명암을 달리해야 입체로 읽힌다
  return s;
}

const BUBBLE_MS = 4500;

/** 머리 위 말풍선. 꼬리는 생략하고 둥근 상자만 — 작은 화면에서 꼬리는 잘 안 보인다. */
function makeBubble(text: string): Container {
  const c = new Container();
  const label = new Text({
    text: text.length > 60 ? `${text.slice(0, 60)}…` : text,
    style: {
      fontFamily: "system-ui, -apple-system, 'Malgun Gothic', sans-serif",
      fontSize: 13,
      fill: 0x2a2520,
      wordWrap: true,
      wordWrapWidth: 180,
      align: "center",
    },
  });
  label.anchor.set(0.5);

  const padX = 9, padY = 6;
  const w = label.width + padX * 2, h = label.height + padY * 2;
  const bg = new Graphics()
    .roundRect(-w / 2, -h / 2, w, h, 7)
    .fill({ color: 0xfdf3e8 })
    .stroke({ color: 0x503433, width: 1.5, alpha: 0.8 });

  c.addChild(bg, label);
  c.position.set(0, -66);          // 머리 위. 스프라이트가 54px 이라 여유를 둔다
  return c;
}

/** 발밑 그림자. 이게 없으면 캐릭터가 바닥에서 떠 보인다 — 도형이든 스프라이트든 항상 깐다. */
function drawShadow(): Graphics {
  return new Graphics().ellipse(0, 0, 12, 6).fill({ color: 0x000000, alpha: 0.25 });
}

/** 도트가 없을 때의 임시 몸통. 발이 (0,0) 에 오도록 그린다 — 스프라이트와 같은 기준이다. */
function drawBody(): Graphics {
  const g = new Graphics();
  g.roundRect(-9, -40, 18, 40, 6).fill({ color: COLOR.body }).stroke({ color: COLOR.bodyEdge, width: 1.5 });
  g.circle(4, -31, 1.6).fill({ color: COLOR.face });
  return g;
}

function diamond(sx: number, sy: number, scale = 1): number[] {
  const w = (TILE_W / 2) * scale, h = (TILE_H / 2) * scale;
  return [sx, sy - h, sx + w, sy, sx, sy + h, sx - w, sy];
}
