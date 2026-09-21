"use client";

// 친구 목록. **"지금 누가 있나"를 한눈에 보는 것**이 이 화면의 존재 이유다.
// 그래서 접속 중인 친구를 맨 위로 올리고, 방 이름과 [놀러 가기]를 같은 줄에 둔다.
//
// 받은 신청은 더 위에 둔다 — 답하지 않으면 상대가 기다리고 있는 상태라서.

import { useMemo, useState } from "react";
import type { Friend } from "@/lib/protocol/packets";
import { C, S, btn, btnGhost, panel } from "@/lib/ui/tokens";

export interface FriendsProps {
  friends: Friend[];
  onAdd: (nick: string) => void;
  onAnswer: (nick: string, accept: boolean) => void;
  onRemove: (nick: string) => void;
  onVisit: (roomId: number, nick: string) => void;
  onClose: () => void;
}

export default function Friends({ friends, onAdd, onAnswer, onRemove, onVisit, onClose }: FriendsProps) {
  const [nick, setNick] = useState("");

  const { incoming, accepted, sent } = useMemo(() => ({
    incoming: friends.filter((f) => f.state === "pending"),
    // 접속 중인 사람 먼저, 그다음 이름순.
    accepted: friends.filter((f) => f.state === "accepted")
      .sort((a, b) => Number(b.online) - Number(a.online) || a.nick.localeCompare(b.nick, "ko")),
    sent: friends.filter((f) => f.state === "sent"),
  }), [friends]);

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    const n = nick.trim();
    if (n.length === 0) return;
    onAdd(n);
    setNick("");
  };

  const onlineCount = accepted.filter((f) => f.online).length;

  return (
    <section style={{ ...panel, marginTop: 10 }}>
      <header style={st.head}>
        <strong style={st.title}>친구</strong>
        {accepted.length > 0 && (
          <span style={st.count}>
            {onlineCount > 0 ? <b style={{ color: C.good }}>{onlineCount}명 접속 중</b> : "아무도 없어요"}
            {` · 전체 ${accepted.length}명`}
          </span>
        )}
        <button onClick={onClose} style={{ ...st.small, marginLeft: "auto" }} aria-label="닫기">✕</button>
      </header>

      <form onSubmit={submit} style={st.addRow}>
        <input
          value={nick}
          onChange={(e) => setNick(e.target.value.slice(0, 16))}
          placeholder="닉네임으로 친구 신청"
          style={st.input}
          autoComplete="off"
        />
        <button type="submit" style={btn} disabled={nick.trim().length === 0}>신청</button>
      </form>

      {incoming.length > 0 && (
        <>
          <h3 style={st.group}>받은 신청 {incoming.length}</h3>
          <ul style={st.list}>
            {incoming.map((f) => (
              <li key={f.nick} style={{ ...st.row, borderColor: C.accent }}>
                <span style={st.name}>{f.nick}</span>
                <span style={st.actions}>
                  <button onClick={() => onAnswer(f.nick, true)} style={btn}>수락</button>
                  <button onClick={() => onAnswer(f.nick, false)} style={btnGhost}>거절</button>
                </span>
              </li>
            ))}
          </ul>
        </>
      )}

      <h3 style={st.group}>친구</h3>
      {accepted.length === 0 ? (
        <p style={st.empty}>아직 친구가 없어요. 위에 닉네임을 넣어 신청해 보세요.</p>
      ) : (
        <ul style={st.list}>
          {accepted.map((f) => (
            <li key={f.nick} style={st.row}>
              <span style={{ ...st.dot, background: f.online ? C.good : C.textFaint }} />
              <span style={st.name}>
                {f.nick}
                {f.fame > 0 && <em style={st.fame}>🌿 {f.fame}</em>}
              </span>
              <span style={st.where}>
                {f.online ? (f.roomName || "접속 중") : "오프라인"}
              </span>
              <span style={st.actions}>
                {f.roomId > 0 && (
                  <button onClick={() => onVisit(f.roomId, f.nick)} style={f.online ? btn : btnGhost}>
                    놀러 가기
                  </button>
                )}
                <button onClick={() => onRemove(f.nick)} style={st.small} title="친구 끊기">✕</button>
              </span>
            </li>
          ))}
        </ul>
      )}

      {sent.length > 0 && (
        <>
          <h3 style={st.group}>보낸 신청</h3>
          <ul style={st.list}>
            {sent.map((f) => (
              <li key={f.nick} style={{ ...st.row, opacity: 0.7 }}>
                <span style={st.name}>{f.nick}</span>
                <span style={st.where}>수락 기다리는 중</span>
                <span style={st.actions}>
                  <button onClick={() => onRemove(f.nick)} style={st.small}>취소</button>
                </span>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}

const st: Record<string, React.CSSProperties> = {
  head: { display: "flex", alignItems: "center", gap: S.gap, marginBottom: 10 },
  title: { fontSize: 14 },
  count: { fontSize: 12.5, color: C.textDim },
  small: { ...btnGhost, padding: "4px 10px", fontSize: 12 },
  addRow: { display: "flex", gap: 6, marginBottom: 4 },
  input: {
    flex: 1, padding: "8px 10px", borderRadius: S.radius,
    border: `1px solid ${C.inputEdge}`, background: C.inputBg, color: "inherit", fontSize: 13.5,
  },
  group: { fontSize: 12, color: C.textFaint, margin: "14px 2px 6px", fontWeight: 600 },
  list: { listStyle: "none", margin: 0, padding: 0, display: "flex", flexDirection: "column", gap: 6 },
  empty: { fontSize: 13, color: C.textDim, margin: "6px 2px" },
  row: {
    display: "flex", alignItems: "center", gap: S.gap,
    padding: "8px 10px", background: C.inputBg, borderRadius: S.radius,
    border: "1px solid transparent",
  },
  dot: { width: 7, height: 7, borderRadius: "50%", flexShrink: 0 },
  name: { fontSize: 13.5 },
  fame: { marginLeft: 7, fontSize: 11.5, color: C.good, fontStyle: "normal" },
  where: { fontSize: 12, color: C.textFaint, marginLeft: "auto" },
  actions: { display: "flex", gap: 6, marginLeft: 10 },
};
