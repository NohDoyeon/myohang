// 프레임: [uint32 length][uint16 opcode][MessagePack body]  — 리틀엔디언
// length = opcode(2) + body 바이트 수.  src/Harbor.Protocol/Framing.cs 와 같은 규칙.
//
// WebSocket 은 이미 메시지 경계를 주므로 length 4바이트는 남는다. 그래도 **그대로 쓴다** —
// 서버의 Framing.Encode 를 손대지 않아도 되고, 규칙이 한 벌로 끝난다. (docs/web-client-plan.md §3)
//
// 다만 "메시지 = 프레임"이라고 **가정하지는 않는다.** 아래 Decoder 는 바이트를 쌓아 두고
// 완전한 프레임만 잘라낸다 — 서버가 나눠 보내든 붙여 보내든 동작한다.

import { encode as msgpackEncode, decode as msgpackDecode } from "@msgpack/msgpack";

export const HEADER_SIZE = 6;
export const MAX_FRAME = 64 * 1024;

export function encodeFrame(op: number, body: unknown): Uint8Array {
  const payload = msgpackEncode(body);
  const buf = new Uint8Array(HEADER_SIZE + payload.length);
  const view = new DataView(buf.buffer);
  view.setUint32(0, 2 + payload.length, true);
  view.setUint16(4, op, true);
  buf.set(payload, HEADER_SIZE);
  return buf;
}

export interface Frame {
  op: number;
  body: unknown;
}

/** 수신 바이트를 쌓아 두고 완전한 프레임만 꺼낸다. 소켓 하나당 하나씩 쓴다. */
export class FrameDecoder {
  private buf = new Uint8Array(0);

  push(chunk: Uint8Array): Frame[] {
    const merged = new Uint8Array(this.buf.length + chunk.length);
    merged.set(this.buf);
    merged.set(chunk, this.buf.length);
    this.buf = merged;

    const out: Frame[] = [];
    for (;;) {
      if (this.buf.length < 4) break;
      const view = new DataView(this.buf.buffer, this.buf.byteOffset, this.buf.byteLength);
      const len = view.getUint32(0, true);
      // 길이가 말이 안 되면 그 뒤는 전부 못 믿는다. 조용히 어긋난 채 계속 읽는 것보다 끊는 게 낫다.
      if (len < 2 || len > MAX_FRAME) throw new Error(`잘못된 프레임 길이 ${len}`);
      if (this.buf.length < 4 + len) break;

      const op = view.getUint16(4, true);
      const body = msgpackDecode(this.buf.subarray(HEADER_SIZE, 4 + len));
      out.push({ op, body });
      this.buf = this.buf.slice(4 + len);
    }
    return out;
  }

  reset() {
    this.buf = new Uint8Array(0);
  }
}
