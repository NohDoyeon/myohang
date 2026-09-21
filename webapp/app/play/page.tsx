"use client";

// 단계 1~2 — 접속해서 방을 그린다.
//   접속 → C_Login → S_LoginResult → C_EnterRoom → S_RoomSnapshot → PixiJS 로 렌더
// 이동(C_Move)과 캐릭터 애니메이션은 단계 3.

import { useCallback, useEffect, useRef, useState } from "react";
import { GameClient, gameUrl } from "@/lib/protocol/client";
import { Op, opName } from "@/lib/protocol/opcode";
import {
  chatPacket, enterRoomPacket, loginPacket, movePacket, readChat, readError, readLoginResult,
  readNotice, readRoomSnapshot, readUserAction, readUserEnter, readUserLeave, readUserPath,
  readWallet, type RoomSnapshot,
} from "@/lib/protocol/packets";
import { RoomRenderer } from "@/lib/game/room-renderer";
import { attachWalkControl } from "@/lib/game/keyboard";
import { takeTicket } from "@/lib/ticket";

interface LogLine { at: string; text: string; kind: "in" | "out" | "info" | "bad" }
interface ChatLine { nick: string; text: string; mine: boolean }

const CHAT_MAX = 120;      // 서버가 120자에서 자른다 (RoomInstance.cs)

export default function PlayPage() {
  const [login, setLogin] = useState("");
  const [token, setToken] = useState("");
  const [status, setStatus] = useState("연결 안 됨");
  const [snapshot, setSnapshot] = useState<RoomSnapshot | null>(null);
  const [rupee, setRupee] = useState<number | null>(null);
  const [log, setLog] = useState<LogLine[]>([]);
  const [chat, setChat] = useState<ChatLine[]>([]);
  const [draft, setDraft] = useState("");

  const client = useRef<GameClient | null>(null);
  const renderer = useRef<RoomRenderer | null>(null);
  const canvasHost = useRef<HTMLDivElement | null>(null);
  const snapRef = useRef<RoomSnapshot | null>(null);
  const myId = useRef<number>(0);

  const add = useCallback((text: string, kind: LogLine["kind"] = "info") => {
    setLog((prev) => [...prev.slice(-200), { at: new Date().toLocaleTimeString(), text, kind }]);
  }, []);

  /** 방 상태가 바뀌면 통째로 다시 그린다. 단계 2에서는 이걸로 충분하다. */
  const paint = useCallback((s: RoomSnapshot) => {
    snapRef.current = s;
    setSnapshot(s);
    renderer.current?.render(s);
  }, []);

  const onFrame = useCallback((op: number, body: unknown) => {
    switch (op) {
      case Op.S_LoginResult: {
        const r = readLoginResult(body);
        if (!r.ok) { setStatus(`로그인 거부: ${r.reason ?? "이유 없음"}`); add(r.reason ?? "거부", "bad"); return; }
        myId.current = r.userId;            // 키보드 이동이 "누구를" 옮길지 알아야 한다
        setStatus(`${r.nick} 님 접속 중`);
        if (r.notice) add(`공지: ${r.notice}`);
        client.current?.post(enterRoomPacket(r.homeRoomId));
        add(`→ C_EnterRoom(${r.homeRoomId})`, "out");
        return;
      }
      case Op.S_RoomSnapshot: {
        const s = readRoomSnapshot(body);
        paint(s);
        add(`방 "${s.room.name}" ${s.room.width}×${s.room.height} · 가구 ${s.items.length} · 사람 ${s.users.length}`, "in");
        return;
      }
      case Op.S_UserEnter: {
        const { user } = readUserEnter(body);
        const s = snapRef.current;
        if (s && !s.users.some((u) => u.id === user.id)) snapRef.current = { ...s, users: [...s.users, user] };
        renderer.current?.upsertUser(user);          // 방 전체를 다시 그리지 않는다 — 걷던 사람이 제자리로 튄다
        add(`${user.nick} 님이 들어왔습니다`, "in");
        return;
      }
      case Op.S_UserLeave: {
        const { userId } = readUserLeave(body);
        const s = snapRef.current;
        if (s) snapRef.current = { ...s, users: s.users.filter((u) => u.id !== userId) };
        renderer.current?.removeUser(userId);
        return;
      }
      // 서버는 칸 단위 경로만 준다. 칸당 시간(room.moveMs)으로 보간하는 건 클라 몫이다.
      case Op.S_UserPath: {
        const { userId, path } = readUserPath(body);
        renderer.current?.setPath(userId, path);
        return;
      }
      case Op.S_UserAction: {
        const a = readUserAction(body);
        renderer.current?.setAction(a.userId, a.action, a.dir);
        return;
      }
      case Op.S_ChatBubble: {
        const c = readChat(body);
        const who = snapRef.current?.users.find((u) => u.id === c.userId);
        renderer.current?.say(c.userId, c.text);
        setChat((prev) => [...prev.slice(-80), { nick: who?.nick ?? "?", text: c.text, mine: c.userId === myId.current }]);
        return;
      }
      case Op.S_WalletUpdate: setRupee(readWallet(body).rupee); return;
      case Op.S_Notice: add(`공지: ${readNotice(body).text}`, "in"); return;
      case Op.S_Error: { const e = readError(body); add(`오류 ${e.code}: ${e.message}`, "bad"); return; }
      default: add(`← ${opName(op)}`, "in");
    }
  }, [add, paint]);

  // 캔버스는 화면에 붙을 때 한 번만 만든다(접속과 무관하게 살아 있어야 다시 그릴 수 있다).
  useEffect(() => {
    let dead = false;
    const r = new RoomRenderer({
      onTileClick: (x, y) => {
        if (!client.current?.connected) return;
        client.current.post(movePacket(x, y));
        add(`→ C_Move(${x}, ${y})`, "out");
      },
      onAtlas: (frames, floorTiles, wallTiles, error) => {
        add(`도트 ${frames}프레임 · 바닥 ${floorTiles}종 · 벽 ${wallTiles}종 로드`);
        if (error) add(`일부 그림 없음(도형으로 그립니다): ${error}`, "bad");
      },
    });
    (async () => {
      if (canvasHost.current) await r.mount(canvasHost.current);
      if (dead) { r.destroy(); return; }
      renderer.current = r;
      if (snapRef.current) r.render(snapRef.current);
    })();
    return () => { dead = true; r.destroy(); renderer.current = null; };
  }, [add]);

  const connect = useCallback((withLogin?: string, withToken?: string) => {
    const nick = (withLogin ?? login).trim();
    const secret = (withToken ?? token).trim();
    const c = new GameClient({
      onFrame,
      onOpen: () => {
        setStatus("연결됨 — 로그인 중");
        add(`연결: ${gameUrl()}`);
        // 입장권으로 들어오면 닉은 서버가 서명에서 읽으므로 빈 값으로 보낸다.
        c.post(loginPacket(nick, secret));
        add("→ C_Login", "out");
      },
      onClose: (why) => { setStatus(`끊김 (${why})`); add(`끊김: ${why}`, "bad"); },
      onError: (m) => add(`오류: ${m}`, "bad"),
    });
    client.current = c;
    try { c.connect(); setStatus("연결 중…"); }
    catch (e) { setStatus(e instanceof Error ? e.message : String(e)); }
  }, [add, login, onFrame, token]);

  // 화살표 / WASD. 창이 살아 있는 동안 계속 붙어 있고, 실제 이동은 접속 중일 때만 나간다.
  useEffect(() => attachWalkControl({
    origin: () => (myId.current ? renderer.current?.tileOf(myId.current) ?? null : null),
    walkable: (x, y) => renderer.current?.isWalkable(x, y) ?? false,
    stepMs: () => snapRef.current?.room.moveMs ?? 240,
    move: (x, y) => {
      if (!client.current?.connected) return;
      client.current.post(movePacket(x, y));
    },
  }), []);

  const send = useCallback(() => {
    const text = draft.trim();
    if (text.length === 0 || !client.current?.connected) return;
    client.current.post(chatPacket(text));
    setDraft("");
    // 여기서 화면에 바로 넣지 않는다. 서버가 S_ChatBubble 로 되돌려 주므로,
    // 그걸 기다려야 **남에게 보이는 것과 같은 내용**(길이 제한 적용 후)이 보인다.
  }, [draft]);

  // 첫 화면에서 로그인해 왔으면 입장권이 sessionStorage 에 있다 → 바로 접속한다.
  // 없으면(주소를 직접 친 경우) 아래 폼이 나온다.
  useEffect(() => {
    const t = takeTicket();
    if (t) connect("", t);
    // connect 는 입력값에 의존하지만, 여기서는 **처음 한 번만** 자동 접속하면 된다.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => () => client.current?.close(), []);

  return (
    <main style={S.page}>
      <header style={S.header}>
        <h1 style={S.h1}>묘항 <small style={S.small}>단계 3 — 이동</small></h1>
        <span style={S.status}>
          {status}{rupee !== null && ` · ${rupee.toLocaleString()} 루피`}
        </span>
      </header>

      {!snapshot && (
        <section style={S.card}>
          <div style={S.row}>
            <label style={S.label}>
              닉네임 <span style={S.hint}>(입장권만 쓸 땐 비워 둠)</span>
              <input value={login} onChange={(e) => setLogin(e.target.value)} style={S.input} placeholder="doyeon.all" />
            </label>
            <label style={S.label}>
              비밀번호 또는 입장권
              <input type="password" value={token} onChange={(e) => setToken(e.target.value)} style={S.input} placeholder="t1.… 또는 비밀번호" />
            </label>
          </div>
          <button onClick={() => connect()} style={S.button}>접속</button>
          <p style={S.hint}>서버: {gameUrl() || "(NEXT_PUBLIC_GAME_WS 없음)"}</p>
        </section>
      )}

      <div ref={canvasHost} style={S.canvas} />

      {snapshot && (
        <>
          <form
            style={S.chatBar}
            onSubmit={(e) => { e.preventDefault(); send(); }}
          >
            <input
              value={draft}
              onChange={(e) => setDraft(e.target.value.slice(0, CHAT_MAX))}
              style={S.chatInput}
              placeholder="여기에 입력하고 Enter"
              maxLength={CHAT_MAX}
              autoComplete="off"
            />
            <button type="submit" style={S.chatSend} disabled={draft.trim().length === 0}>보내기</button>
          </form>

          {chat.length > 0 && (
            <div style={S.chatLog}>
              {/* React 의 기본 텍스트 렌더링이라 HTML 이 섞여 와도 태그로 해석되지 않는다.
                  dangerouslySetInnerHTML 을 쓰면 그 순간 뚫린다 — 쓰지 말 것. */}
              {chat.map((c, i) => (
                <div key={i} style={S.chatLine}>
                  <b style={{ ...S.chatNick, color: c.mine ? "#c98c4b" : "#9ab0c6" }}>{c.nick}</b>
                  {c.text}
                </div>
              ))}
            </div>
          )}

          <p style={S.caption}>
            <strong>{snapshot.room.name}</strong> · {snapshot.room.templateId} · {snapshot.room.width}×{snapshot.room.height}
            {" · "}가구 {snapshot.items.length}
            <span style={S.keys}>클릭 또는 화살표·WASD 로 이동</span>
          </p>
        </>
      )}

      <details style={S.card}>
        <summary style={S.summary}>주고받은 것 ({log.length})</summary>
        <div style={S.log}>
          {log.map((l, i) => (
            <div key={i} style={{ color: LOG_COLOR[l.kind] }}>
              <span style={S.time}>{l.at}</span> {l.text}
            </div>
          ))}
        </div>
      </details>
    </main>
  );
}

const LOG_COLOR: Record<LogLine["kind"], string> = {
  in: "#7fd1ff", out: "#9ae6a0", info: "#d8d2c8", bad: "#ff9b9b",
};

const S: Record<string, React.CSSProperties> = {
  page: { maxWidth: 940, margin: "0 auto", padding: "16px", fontFamily: "system-ui, sans-serif" },
  header: { display: "flex", alignItems: "baseline", gap: 12, flexWrap: "wrap", marginBottom: 12 },
  h1: { fontSize: 20, margin: 0 },
  small: { fontSize: 12, opacity: 0.5, fontWeight: 400 },
  status: { fontSize: 13, opacity: 0.75, marginLeft: "auto" },
  card: { background: "#1b1a18", border: "1px solid #2f2d2a", borderRadius: 10, padding: 16, marginBottom: 12 },
  row: { display: "flex", gap: 12, flexWrap: "wrap" },
  label: { display: "flex", flexDirection: "column", gap: 6, fontSize: 13, flex: "1 1 220px" },
  input: { padding: "9px 10px", borderRadius: 6, border: "1px solid #3a3733", background: "#121110", color: "inherit", fontSize: 14 },
  button: { marginTop: 12, padding: "9px 18px", borderRadius: 6, border: 0, background: "#c98c4b", color: "#1b1a18", fontWeight: 600, fontSize: 14, cursor: "pointer" },
  hint: { fontSize: 12, opacity: 0.55, margin: "6px 0 0" },
  canvas: { width: "100%", height: "min(62vh, 560px)", borderRadius: 10, overflow: "hidden", background: "#171614", border: "1px solid #2f2d2a" },
  caption: { fontSize: 13, opacity: 0.75, margin: "10px 0 12px", display: "flex", gap: 8, flexWrap: "wrap" },
  keys: { marginLeft: "auto", opacity: 0.7 },
  chatBar: { display: "flex", gap: 8, marginTop: 10 },
  chatInput: { flex: 1, padding: "10px 12px", borderRadius: 8, border: "1px solid #3a3733", background: "#1b1a18", color: "inherit", fontSize: 14 },
  chatSend: { padding: "10px 16px", borderRadius: 8, border: 0, background: "#c98c4b", color: "#1b1a18", fontWeight: 600, fontSize: 14, cursor: "pointer" },
  chatLog: { marginTop: 10, maxHeight: 132, overflowY: "auto", fontSize: 13.5, lineHeight: 1.75, background: "#1b1a18", border: "1px solid #2f2d2a", borderRadius: 8, padding: "8px 12px" },
  chatLine: { wordBreak: "break-word" },
  chatNick: { marginRight: 7 },
  summary: { cursor: "pointer", fontSize: 13, opacity: 0.8 },
  log: { maxHeight: 220, overflowY: "auto", fontSize: 12.5, fontFamily: "ui-monospace, monospace", lineHeight: 1.7, marginTop: 10 },
  time: { opacity: 0.4, marginRight: 8 },
};
