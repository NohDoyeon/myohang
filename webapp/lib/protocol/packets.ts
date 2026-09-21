// ⚠ **필드 이름은 전선에 실리지 않는다.**
//
// 서버의 `[MessagePackObject]` + `[Key(n)]` 은 객체를 **맵이 아니라 배열**로 직렬화한다.
// `C_Login { Login, Token }` 은 `{"Login":…}` 이 아니라 `[login, token]` 으로 나간다.
// 그래서 여기서는 **자리(index)** 로 읽고 쓴다. 이름으로 맞추려 들면 전부 undefined 가 되는데,
// 오류가 나지 않고 **조용히 빈 화면**이 된다.
//
// 따라서 `Packets.cs` 의 `[Key]` 번호는 계약이다. 중간에 필드를 끼워 넣으면 웹 클라가 통째로 어긋난다.
// 새 필드는 **반드시 맨 뒤 번호**로 붙인다.

import { Op } from "./opcode";

// ---------- 읽기 도우미 ----------
const arr = (v: unknown): unknown[] => (Array.isArray(v) ? v : []);
const num = (v: unknown, d = 0): number => (typeof v === "number" ? v : d);
const str = (v: unknown, d = ""): string => (typeof v === "string" ? v : d);
const bool = (v: unknown): boolean => v === true;
const strs = (v: unknown): string[] => arr(v).map((x) => str(x));

// ---------- DTO ----------
export interface RoomDto {
  id: number; templateId: string; name: string; heightmap: string[];
  doorX: number; doorY: number; wallStyle: string; floorStyle: string; wallHeight: number;
  ownerId: number; ownerNick: string; kind: string;
  upgradePrice: number; upgradeName: string;
  width: number; height: number;
  /** 한 칸 걷는 데 걸리는 ms. **이 값으로 보간해야** 서버 걸음과 어긋나지 않는다. */
  moveMs: number;
}

export function readRoom(v: unknown): RoomDto {
  const a = arr(v);
  return {
    id: num(a[0]), templateId: str(a[1]), name: str(a[2]), heightmap: strs(a[3]),
    doorX: num(a[4]), doorY: num(a[5]), wallStyle: str(a[6]), floorStyle: str(a[7]),
    wallHeight: num(a[8], 3), ownerId: num(a[9]), ownerNick: str(a[10]), kind: str(a[11]),
    upgradePrice: num(a[12]), upgradeName: str(a[13]),
    width: num(a[14]), height: num(a[15]), moveMs: num(a[16], 240),
  };
}

export interface ItemDto {
  id: number; furniId: string; x: number; y: number; z: number; dir: number;
  state: string; extra: string | null; name: string; solid: boolean;
  interaction: string; wall: boolean; author: string;
  wallU: number; wallV: number; tilt: number; usable: boolean;
}

export function readItem(v: unknown): ItemDto {
  const a = arr(v);
  return {
    id: num(a[0]), furniId: str(a[1]), x: num(a[2]), y: num(a[3]), z: num(a[4]), dir: num(a[5]),
    state: str(a[6]), extra: typeof a[7] === "string" ? a[7] : null, name: str(a[8]), solid: bool(a[9]),
    interaction: str(a[10]), wall: bool(a[11]), author: str(a[12]),
    wallU: num(a[13]), wallV: num(a[14]), tilt: num(a[15]), usable: bool(a[16]),
  };
}

export interface UserDto {
  id: number; nick: string; figure: string;
  x: number; y: number; z: number; dir: number; action: string;
  /** 인기도 — 내 화분에 남들이 꽂아 준 캣닢의 누적 개수. */
  fame: number;
}

export function readUser(v: unknown): UserDto {
  const a = arr(v);
  return {
    id: num(a[0]), nick: str(a[1]), figure: str(a[2]),
    x: num(a[3]), y: num(a[4]), z: num(a[5]), dir: num(a[6]), action: str(a[7], "stand"),
    fame: num(a[8]),
  };
}

export interface TilePos { x: number; y: number }
const readTile = (v: unknown): TilePos => { const a = arr(v); return { x: num(a[0]), y: num(a[1]) }; };

// ---------- S → C ----------
export interface LoginResult {
  ok: boolean; userId: number; reason: string | null; homeRoomId: number; notice: string;
  /** 서버가 확정한 닉. 입장권으로 들어오면 입력값과 다를 수 있으니 **이것을 따른다.** */
  nick: string;
}

export const readLoginResult = (v: unknown): LoginResult => {
  const a = arr(v);
  return {
    ok: bool(a[0]), userId: num(a[1]), reason: typeof a[2] === "string" ? a[2] : null,
    homeRoomId: num(a[3]), notice: str(a[4]), nick: str(a[5]),
  };
};

export interface RoomSnapshot { room: RoomDto; items: ItemDto[]; users: UserDto[] }

export const readRoomSnapshot = (v: unknown): RoomSnapshot => {
  const a = arr(v);
  return { room: readRoom(a[0]), items: arr(a[1]).map(readItem), users: arr(a[2]).map(readUser) };
};

export const readError = (v: unknown) => { const a = arr(v); return { code: num(a[0]), message: str(a[1]) }; };
export const readNotice = (v: unknown) => ({ text: str(arr(v)[0]) });
export const readUserEnter = (v: unknown) => ({ user: readUser(arr(v)[0]) });
export const readUserLeave = (v: unknown) => ({ userId: num(arr(v)[0]) });
export const readUserPath = (v: unknown) => {
  const a = arr(v);
  return { userId: num(a[0]), path: arr(a[1]).map(readTile) };
};
export const readUserAction = (v: unknown) => {
  const a = arr(v);
  return { userId: num(a[0]), action: str(a[1]), dir: num(a[2]) };
};
export const readChat = (v: unknown) => {
  const a = arr(v);
  return { userId: num(a[0]), text: str(a[1]), kind: num(a[2]) };
};
export const readWallet = (v: unknown) => { const a = arr(v); return { rupee: num(a[0]), cash: num(a[1]) }; };

// ---------- 방 목록 ----------
export interface RoomInfo {
  id: number;
  name: string;
  /** "public" = 누구나 오는 광장·댄스홀. "private" = 개인 집. */
  kind: string;
  ownerId: number;
  ownerNick: string;
  users: number;
  maxUsers: number;
}

export const readRoomInfo = (v: unknown): RoomInfo => {
  const a = arr(v);
  return {
    id: num(a[0]), name: str(a[1]), kind: str(a[2]),
    ownerId: num(a[3]), ownerNick: str(a[4]),
    users: num(a[5]), maxUsers: num(a[6], 25),
  };
};

export const readRoomList = (v: unknown): RoomInfo[] => arr(arr(v)[0]).map(readRoomInfo);

export const roomListPacket = () => ({ op: Op.C_RoomList, body: [] });

// ---------- 가방 ----------
export interface InvEntry {
  furniId: string;
  name: string;
  qty: number;
  wall: boolean;
  interaction: string;
  /** 0 이면 팔 수 없다(수확물만 팔린다). */
  sellPrice: number;
  /** 묶음 단위. 0 이면 묶음 없음. 묶음이 낱개×개수보다 비싸야 "모아서 파는" 의미가 있다. */
  sellBundleQty: number;
  sellBundlePrice: number;
}

/**
 * ⚠ **`S_Inventory` 의 항목과 `S_InventoryUpdate` 는 자리 순서가 다르다.**
 *   InventoryEntry    : [furniId, **name**, **qty**, wall, …]
 *   S_InventoryUpdate : [furniId, **qty**,  **name**, wall, …]
 * 1번과 2번이 뒤바뀌어 있다. 같은 읽기 함수를 쓰면 이름 자리에 숫자가 들어간다.
 */
const readInvEntryTail = (a: unknown[], furniId: string, name: string, qty: number): InvEntry => ({
  furniId, name, qty,
  wall: bool(a[3]), interaction: str(a[4]),
  sellPrice: num(a[5]), sellBundleQty: num(a[6]), sellBundlePrice: num(a[7]),
});

export const readInvEntry = (v: unknown): InvEntry => {
  const a = arr(v);
  return readInvEntryTail(a, str(a[0]), str(a[1]), num(a[2]));
};

/** S_InventoryUpdate — **Qty 는 변화량이 아니라 현재 보유 수량(절대값)**. 0 이면 목록에서 뺀다. */
export const readInvUpdate = (v: unknown): InvEntry => {
  const a = arr(v);
  return readInvEntryTail(a, str(a[0]), str(a[2]), num(a[1]));
};

export const readInventory = (v: unknown): InvEntry[] => arr(arr(v)[0]).map(readInvEntry);

// ---------- C → S (가방) ----------
export const inventoryPacket = () => ({ op: Op.C_Inventory, body: [] });
export const sellPacket = (furniId: string, qty = 1) => ({ op: Op.C_SellItem, body: [furniId, qty] });
export const placeItemPacket = (furniId: string, x: number, y: number, dir = 2, wallU = 0, wallV = 0, tilt = 0) =>
  ({ op: Op.C_PlaceItem, body: [furniId, x, y, dir, wallU, wallV, tilt] });

// ---------- C → S ----------
// 배열의 **자리**가 [Key] 번호다. 빠뜨리면 그 자리가 null 로 가서 서버가 기본값을 본다.
export const loginPacket = (login: string, token: string) => ({ op: Op.C_Login, body: [login, token] });
export const enterRoomPacket = (roomId: number) => ({ op: Op.C_EnterRoom, body: [roomId] });
export const movePacket = (x: number, y: number) => ({ op: Op.C_Move, body: [x, y] });
export const actionPacket = (action: string) => ({ op: Op.C_Action, body: [action] });
export const chatPacket = (text: string, kind = 0) => ({ op: Op.C_Chat, body: [text, kind] });
export const pingPacket = () => ({ op: Op.C_Ping, body: [] });
