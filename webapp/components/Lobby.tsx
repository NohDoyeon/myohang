"use client";

// 로비(지도) — 마을 그림 위에서 갈 곳을 고른다.
//
// 방 목록과 **겹치지만 대신하지 않는다.** 목록은 정확하고(모든 방·인원수), 지도는 예쁘다.
// 사람이 많아지면 목록이 길어지므로 "주요 장소"만 지도에 둔다.
//
// **그림이 없어도 동작한다.** 배경이 안 불러와지면 색만 깔리고 구역은 카드로 보인다 —
// 그림을 기다리는 동안에도 쓸 수 있어야 한다(그러지 않으면 그림이 올 때까지 확인이 막힌다).
//
// 접근성: 구역은 `<button>` 이다. 마우스를 올렸을 때와 **키보드 포커스를 받았을 때가 똑같이** 커진다.
// div 에 onClick 을 달면 탭으로 못 가고 Enter 로도 못 누른다.

import { useMemo, useState } from "react";
import type { RoomInfo } from "@/lib/protocol/packets";
import { DEBUG_OUTLINE, HOME_SPOT, LOBBY_SPOTS, type LobbySpot } from "@/lib/game/lobby-spots";
import { C, S, btnGhost, panel } from "@/lib/ui/tokens";

/** 배경 그림. 없으면 조용히 색으로 대체된다(`onError`). */
const BACKGROUND = "/art/lobby/town.png";

export interface LobbyProps {
  rooms: RoomInfo[];
  currentId: number;
  myNick: string;
  onEnter: (roomId: number) => void;
  onClose: () => void;
}

/** 구역 하나가 실제로 어느 방인지 풀어 둔 것. `room` 이 null 이면 아직 없는 곳이다. */
interface Resolved {
  spot: LobbySpot;
  room: RoomInfo | null;
}

export default function Lobby({ rooms, currentId, myNick, onEnter, onClose }: LobbyProps) {
  const [bgFailed, setBgFailed] = useState(false);

  const resolved = useMemo<Resolved[]>(() => LOBBY_SPOTS.map((spot) => ({
    spot,
    room: spot.room === HOME_SPOT
      ? rooms.find((r) => r.kind !== "public" && r.ownerNick === myNick) ?? null
      : rooms.find((r) => r.name === spot.room) ?? null,
  })), [rooms, myNick]);

  const missing = resolved.filter((r) => r.room === null).length;

  return (
    <section style={{ ...panel, marginTop: 10 }}>
      <header style={st.head}>
        <strong style={st.title}>어디로 갈까요</strong>
        <button onClick={onClose} style={{ ...st.small, marginLeft: "auto" }} aria-label="닫기">✕</button>
      </header>

      <div style={{ ...st.map, ...(bgFailed ? st.mapPlain : null) }}>
        {!bgFailed && (
          // 배경은 장식이다 — 갈 곳은 아래 버튼들이 말해 주므로 alt 는 비운다.
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={BACKGROUND}
            alt=""
            style={st.bg}
            onError={() => setBgFailed(true)}
          />
        )}

        {resolved.map(({ spot, room }) => (
          <Spot
            key={spot.label}
            spot={spot}
            room={room}
            here={room?.id === currentId}
            onEnter={onEnter}
          />
        ))}
      </div>

      <p style={st.foot}>
        {bgFailed
          ? "배경 그림이 아직 없어요. 구역을 눌러 이동할 수 있습니다."
          : "가고 싶은 곳에 마우스를 올려 보세요."}
        {missing > 0 && ` · 준비 중 ${missing}곳`}
      </p>
    </section>
  );
}

function Spot({ spot, room, here, onEnter }: {
  spot: LobbySpot; room: RoomInfo | null; here: boolean; onEnter: (id: number) => void;
}) {
  const [hot, setHot] = useState(false);
  // 정원으로 막지 않는다 — 공용 장소는 사람이 몰려도 들여보낸다(모이라고 만든 곳이다).
  // 방 목록은 여전히 정원을 보여 주므로, 정말 못 들어가는 경우는 거기서 드러난다.
  const disabled = room === null || here;

  return (
    <button
      type="button"
      disabled={disabled}
      onClick={() => room && onEnter(room.id)}
      onMouseEnter={() => setHot(true)}
      onMouseLeave={() => setHot(false)}
      onFocus={() => setHot(true)}
      onBlur={() => setHot(false)}
      style={{
        ...st.spot,
        left: `${spot.x}%`, top: `${spot.y}%`, width: `${spot.w}%`, height: `${spot.h}%`,
        // 커지는 것은 **변형(transform)** 으로만 한다. 좌표를 바꾸면 옆 구역과 자리가 어긋난다.
        transform: hot && !disabled ? "scale(1.12)" : "scale(1)",
        cursor: disabled ? "default" : "pointer",
        opacity: room === null ? 0.45 : 1,
        borderColor: DEBUG_OUTLINE ? C.bad : hot && !disabled ? C.accent : "transparent",
        background: hot && !disabled ? "rgba(201,140,75,0.16)" : "transparent",
        zIndex: hot ? 2 : 1,
      }}
    >
      <span style={{ ...st.label, ...(hot || room === null ? st.labelOn : null) }}>
        <b style={st.labelName}>{spot.label}</b>
        <em style={st.labelHint}>
          {room === null ? "준비 중"
            : here ? "지금 여기"
            : `${room.users}명`}
        </em>
        {hot && room !== null && !here && <em style={st.labelHint}>{spot.hint}</em>}
      </span>
    </button>
  );
}

const st: Record<string, React.CSSProperties> = {
  head: { display: "flex", alignItems: "center", gap: S.gap, marginBottom: 8 },
  title: { fontSize: 14 },
  small: { ...btnGhost, padding: "4px 10px", fontSize: 12 },
  map: {
    position: "relative",
    width: "100%",
    // 배경 그림과 **같은 비율**로 고정한다(밤 항구 전경 1536×1024 = 3:2).
    // 비율이 다르면 `objectFit: cover` 가 그림을 잘라 내고, 구역 좌표(%)가 랜드마크에서 어긋난다.
    aspectRatio: "3 / 2",
    borderRadius: S.radius,
    overflow: "hidden",
    background: C.inputBg,
  },
  // 그림이 없을 때의 대체 배경. 항구의 밤 하늘 정도의 느낌만 낸다.
  mapPlain: { background: "linear-gradient(180deg, #1d2432 0%, #2a2724 70%, #322c25 100%)" },
  bg: { position: "absolute", inset: 0, width: "100%", height: "100%", objectFit: "cover" },
  spot: {
    position: "absolute",
    display: "grid",
    placeItems: "center",
    padding: 0,
    border: "2px solid transparent",
    borderRadius: S.radiusLg,
    color: C.text,
    transition: "transform 140ms ease, background 140ms ease, border-color 140ms ease",
  },
  label: {
    display: "flex", flexDirection: "column", alignItems: "center", gap: 2,
    padding: "5px 10px", borderRadius: 999,
    background: "rgba(18,17,16,0.72)",
    opacity: 0.75,
    transition: "opacity 140ms ease",
  },
  labelOn: { opacity: 1 },
  labelName: { fontSize: 13.5 },
  labelHint: { fontSize: 11.5, fontStyle: "normal", color: C.textDim },
};
