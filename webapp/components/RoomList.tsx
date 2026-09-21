"use client";

// 방 목록. **사람을 만나려면 이게 있어야 한다** — 로그인하면 각자 자기 집으로 들어가기 때문에,
// 공용 방(부둣가 광장·댄스홀)으로 옮기지 않으면 둘이 접속해 있어도 서로 안 보인다.

import { useMemo } from "react";
import type { RoomInfo } from "@/lib/protocol/packets";
import { C, S, btn, btnGhost, panel } from "@/lib/ui/tokens";

export interface RoomListProps {
  rooms: RoomInfo[];
  currentId: number;
  myNick: string;
  onEnter: (roomId: number) => void;
  onRefresh: () => void;
  onClose: () => void;
}

export default function RoomList({ rooms, currentId, myNick, onEnter, onRefresh, onClose }: RoomListProps) {
  // 공용 방을 맨 위에. 사람이 많은 순으로 — 어디 가야 누가 있는지가 가장 궁금한 정보다.
  const { publics, homes } = useMemo(() => {
    const byBusy = (a: RoomInfo, b: RoomInfo) => b.users - a.users || a.name.localeCompare(b.name, "ko");
    return {
      publics: rooms.filter((r) => r.kind === "public").sort(byBusy),
      homes: rooms.filter((r) => r.kind !== "public").sort(byBusy),
    };
  }, [rooms]);

  return (
    <section style={{ ...panel, marginTop: 10 }}>
      <header style={st.head}>
        <strong style={st.title}>방 목록</strong>
        <button onClick={onRefresh} style={st.small}>새로고침</button>
        <button onClick={onClose} style={{ ...st.small, marginLeft: "auto" }} aria-label="닫기">✕</button>
      </header>

      <p style={st.hint}>
        친구와 만나려면 <b>같은 방</b>에 들어가야 해요. 로그인하면 각자 자기 방으로 들어갑니다.
      </p>

      <h3 style={st.group}>모이는 곳</h3>
      <ul style={st.list}>
        {publics.length === 0 && <li style={st.empty}>불러오는 중…</li>}
        {publics.map((r) => (
          <Row key={r.id} room={r} current={r.id === currentId} onEnter={onEnter} />
        ))}
      </ul>

      {homes.length > 0 && (
        <>
          <h3 style={st.group}>집</h3>
          <ul style={st.list}>
            {homes.map((r) => (
              <Row key={r.id} room={r} current={r.id === currentId} mine={r.ownerNick === myNick} onEnter={onEnter} />
            ))}
          </ul>
        </>
      )}
    </section>
  );
}

function Row({ room, current, mine, onEnter }: {
  room: RoomInfo; current: boolean; mine?: boolean; onEnter: (id: number) => void;
}) {
  const full = room.users >= room.maxUsers;
  return (
    <li style={{ ...st.row, ...(current ? st.rowCurrent : null) }}>
      <span style={st.name}>
        {room.name}
        {mine && <em style={st.tag}>내 방</em>}
      </span>
      <span style={{ ...st.count, color: room.users > 0 ? C.good : C.textFaint }}>
        {room.users}/{room.maxUsers}명
      </span>
      {current ? (
        <span style={st.here}>지금 여기</span>
      ) : (
        <button onClick={() => onEnter(room.id)} style={full ? st.disabled : btn} disabled={full}>
          {full ? "가득 참" : "들어가기"}
        </button>
      )}
    </li>
  );
}

const st: Record<string, React.CSSProperties> = {
  head: { display: "flex", alignItems: "center", gap: S.gap, marginBottom: 8 },
  title: { fontSize: 14 },
  small: { ...btnGhost, padding: "4px 10px", fontSize: 12 },
  hint: { margin: "0 0 12px", fontSize: 12.5, color: C.textDim, lineHeight: 1.6 },
  group: { fontSize: 12, color: C.textFaint, margin: "12px 2px 6px", fontWeight: 600 },
  list: { listStyle: "none", margin: 0, padding: 0, display: "flex", flexDirection: "column", gap: 6 },
  empty: { fontSize: 13, color: C.textFaint, padding: "6px 2px" },
  row: {
    display: "flex", alignItems: "center", gap: S.gap,
    padding: "8px 10px", background: C.inputBg, borderRadius: S.radius,
    border: "1px solid transparent",
  },
  rowCurrent: { borderColor: C.accent },
  name: { fontSize: 13.5 },
  tag: { marginLeft: 6, fontSize: 11, color: C.info, fontStyle: "normal" },
  count: { fontSize: 12.5, marginLeft: "auto" },
  here: { fontSize: 12.5, color: C.accent, padding: "8px 4px" },
  disabled: { ...btnGhost, opacity: 0.45, cursor: "default" },
};
