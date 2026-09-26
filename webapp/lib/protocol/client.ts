// 게임 서버 WebSocket 접속.
//
// 주소는 **코드에 박지 않는다.** 두 군데에서 온다:
//   ① `/api/endpoint` — 게임 서버가 켜지면서 DB 에 적어 둔 **지금 주소**. 터널을 다시 열어 주소가
//      바뀌어도 여기에 바로 반영되므로 Vercel 재배포가 필요 없다. **이게 기본이다.**
//   ② `NEXT_PUBLIC_GAME_WS` — ①이 비었을 때(로컬 개발, DB 없음)의 대비책.
// (이 값은 비밀이 아니라 공개 주소이므로 `NEXT_PUBLIC_` 접두사가 맞다. 비밀에는 절대 붙이지 않는다 — CLAUDE.md)
//
// 로그인은 **연결한 뒤 `C_Login` 패킷으로** 한다. 입장권을 주소에 싣지 않는 이유는
// URL 이 서버 로그·브라우저 히스토리·프록시에 남기 때문이다.

import { FrameDecoder, encodeFrame } from "./framing";
import { Op, opName } from "./opcode";

export type FrameHandler = (op: number, body: unknown) => void;

export interface ClientEvents {
  onFrame: FrameHandler;
  onOpen?: () => void;
  onClose?: (reason: string) => void;
  onError?: (message: string) => void;
}

export function gameUrl(): string {
  const fromEnv = process.env.NEXT_PUBLIC_GAME_WS;
  if (fromEnv && fromEnv.length > 0) return fromEnv;
  // 설정이 없으면 이 페이지를 준 곳으로 추측한다(게임 서버가 웹도 서빙하는 경우).
  if (typeof window !== "undefined") {
    const proto = window.location.protocol === "https:" ? "wss:" : "ws:";
    return `${proto}//${window.location.host}/ws`;
  }
  return "";
}

export interface Endpoint {
  ws: string;
  /** 게임 서버가 살아 있는지. `null` = 알 수 없음(DB 에 기록이 없어 빌드 타임 주소를 쓰는 중). */
  online: boolean | null;
}

/**
 * 붙을 주소를 정한다. **접속 직전에 부른다** — 페이지를 열어 둔 사이에 서버가 새 터널로
 * 다시 떴을 수 있기 때문이다.
 *
 * 실패해도 던지지 않는다. `/api/endpoint` 가 죽어도 빌드 타임 주소로 들어갈 수 있어야 한다.
 */
export async function resolveEndpoint(): Promise<Endpoint> {
  try {
    const res = await fetch("/api/endpoint", { cache: "no-store" });
    if (res.ok) {
      const data: unknown = await res.json();
      const d = data as { ws?: unknown; online?: unknown };
      if (typeof d.ws === "string" && d.ws.length > 0)
        return { ws: d.ws, online: typeof d.online === "boolean" ? d.online : null };
    }
  } catch {
    // 네트워크·JSON 오류 — 아래 대비책으로 간다.
  }
  return { ws: gameUrl(), online: null };
}

export class GameClient {
  private ws: WebSocket | null = null;
  private readonly decoder = new FrameDecoder();
  private readonly events: ClientEvents;

  constructor(events: ClientEvents) {
    this.events = events;
  }

  get connected() {
    return this.ws?.readyState === WebSocket.OPEN;
  }

  connect(url = gameUrl()): void {
    if (url.length === 0) throw new Error("게임 서버 주소가 없습니다 (NEXT_PUBLIC_GAME_WS)");
    this.close();
    this.decoder.reset();

    const ws = new WebSocket(url);
    ws.binaryType = "arraybuffer";          // 기본값 blob 이면 매 프레임마다 비동기 변환이 끼어든다
    this.ws = ws;

    ws.onopen = () => this.events.onOpen?.();

    ws.onmessage = (ev) => {
      try {
        for (const f of this.decoder.push(new Uint8Array(ev.data as ArrayBuffer)))
          this.events.onFrame(f.op, f.body);
      } catch (e) {
        // 프레임이 깨지면 그 뒤 바이트는 믿을 수 없다 → 이어 읽지 않고 끊는다.
        this.events.onError?.(e instanceof Error ? e.message : String(e));
        this.close();
      }
    };

    ws.onerror = () => this.events.onError?.("연결 오류");
    ws.onclose = (ev) => this.events.onClose?.(ev.reason || `코드 ${ev.code}`);
  }

  send(op: number, body: unknown): void {
    if (!this.connected) throw new Error(`연결되지 않음 (${opName(op)} 전송 실패)`);
    this.ws!.send(encodeFrame(op, body));
  }

  /** `packets.ts` 의 생성 함수가 돌려주는 모양 그대로 받는다. */
  post(packet: { op: number; body: unknown }): void {
    this.send(packet.op, packet.body);
  }

  ping(): void {
    this.send(Op.C_Ping, []);
  }

  close(): void {
    if (!this.ws) return;
    this.ws.onopen = this.ws.onmessage = this.ws.onerror = this.ws.onclose = null;
    if (this.ws.readyState === WebSocket.OPEN || this.ws.readyState === WebSocket.CONNECTING) this.ws.close();
    this.ws = null;
  }
}
