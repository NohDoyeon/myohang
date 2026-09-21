// 게임 서버 WebSocket 접속.
//
// 주소는 **코드에 박지 않는다** — `NEXT_PUBLIC_GAME_WS` 로 받는다. 개발은 로컬, 배포는 터널을 가리킨다.
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
