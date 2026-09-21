"use client";

// 아틀라스 원본 확인용. **방향 매핑을 추측으로 정하지 않기 위해** 만든 페이지다.
//
// 시트에는 방향 0~4만 있고, 그 각각이 "어느 쪽을 보는 그림"인지는 그려 본 사람만 안다.
// 여기서 눈으로 확인한 뒤 `lib/game/atlas.ts` 의 artDirection 을 맞춘다.

import { useEffect, useState } from "react";
import { AvatarAtlas, artDirection } from "@/lib/game/atlas";

const ACTIONS = ["stand", "walk", "sit", "sleep", "dance"] as const;
const ART_DIRS = [0, 1, 2, 3, 4];

/** 서버 방향 0~7 의 뜻. 화면 기준으로 어디로 가는 중인지 함께 적는다. */
const SERVER_DIRS = [
  { dir: 0, label: "0 북", screen: "화면 위-오른쪽" },
  { dir: 1, label: "1 북동", screen: "화면 오른쪽" },
  { dir: 2, label: "2 동", screen: "화면 아래-오른쪽" },
  { dir: 3, label: "3 남동", screen: "화면 아래" },
  { dir: 4, label: "4 남", screen: "화면 아래-왼쪽" },
  { dir: 5, label: "5 남서", screen: "화면 왼쪽" },
  { dir: 6, label: "6 서", screen: "화면 위-왼쪽" },
  { dir: 7, label: "7 북서", screen: "화면 위" },
];

export default function SpritesPage() {
  const [atlas, setAtlas] = useState<AvatarAtlas | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    AvatarAtlas.load().then(setAtlas).catch((e) => setError(String(e)));
  }, []);

  if (error) return <main style={S.page}><p style={S.bad}>{error}</p></main>;
  if (!atlas) return <main style={S.page}><p>불러오는 중…</p></main>;

  return (
    <main style={S.page}>
      <h1 style={S.h1}>아틀라스 확인 <small style={S.small}>{atlas.size}프레임</small></h1>

      <section style={S.card}>
        <h2 style={S.h2}>1. 시트 원본 — 이 그림들이 각각 어느 쪽을 보고 있나요?</h2>
        <p style={S.note}>
          방향 번호는 <b>그림 파일에 붙어 있던 번호</b>일 뿐입니다. 0이 뒷모습이고 4가 앞모습인지,
          옆모습이 오른쪽을 보는지 왼쪽을 보는지를 확인해 주세요.
        </p>
        {ACTIONS.map((action) => (
          <div key={action} style={S.row}>
            <span style={S.rowLabel}>{action}</span>
            {ART_DIRS.map((d) => (
              <Frame key={d} atlas={atlas} artDir={d} action={action} caption={`art ${d}`} />
            ))}
          </div>
        ))}
      </section>

      <section style={S.card}>
        <h2 style={S.h2}>2. 지금 매핑 — 서버 방향으로 걸을 때 이렇게 보입니다</h2>
        <p style={S.note}>
          위에서 확인한 것과 <b>&ldquo;화면 기준&rdquo; 설명이 맞는지</b> 비교해 주세요.
          예: <b>7 북서 = 화면 위</b> 로 걸어갈 때는 <b>뒷모습</b>이어야 합니다.
        </p>
        <div style={S.rowWrap}>
          {SERVER_DIRS.map(({ dir, label, screen }) => {
            const m = artDirection(dir);
            return (
              <div key={dir} style={S.cell}>
                <Frame atlas={atlas} artDir={m.artDir} action="stand" flip={m.flip} caption="" />
                <div style={S.cellLabel}>
                  <b>{label}</b>
                  <div style={S.sub}>{screen}</div>
                  <div style={S.sub}>art {m.artDir}{m.flip ? " · 반전" : ""}</div>
                </div>
              </div>
            );
          })}
        </div>
      </section>
    </main>
  );
}

function Frame({ atlas, artDir, action, flip = false, caption }: {
  atlas: AvatarAtlas; artDir: number; action: string; flip?: boolean; caption: string;
}) {
  // 시트 좌표를 그대로 CSS 배경으로 잘라 쓴다 — Pixi 없이도 정확히 같은 픽셀이 나온다.
  // **`raw`** 로 꺼낸다 — `pick` 은 서버 방향을 받으므로 여기서 쓰면 원본이 아니라 변환된 그림이 나온다.
  const tex = atlas.raw(artDir, action, 0);
  if (!tex) return <div style={{ ...S.frame, opacity: 0.25, width: 60, height: 60 }} />;
  const got = { texture: tex };
  const f = got.texture.frame;
  return (
    <figure style={S.figure}>
      <div
        style={{
          ...S.frame,
          width: f.width * 2, height: f.height * 2,
          backgroundImage: "url(/art/atlas.png)",
          backgroundPosition: `${-f.x * 2}px ${-f.y * 2}px`,
          backgroundSize: `${got.texture.source.width * 2}px ${got.texture.source.height * 2}px`,
          transform: flip ? "scaleX(-1)" : undefined,
        }}
      />
      {caption && <figcaption style={S.cap}>{caption}</figcaption>}
    </figure>
  );
}

const S: Record<string, React.CSSProperties> = {
  page: { maxWidth: 960, margin: "0 auto", padding: 20, fontFamily: "system-ui, sans-serif" },
  h1: { fontSize: 20, margin: "0 0 14px" },
  h2: { fontSize: 15, margin: "0 0 8px" },
  small: { fontSize: 12, opacity: 0.5, fontWeight: 400 },
  card: { background: "#1b1a18", border: "1px solid #2f2d2a", borderRadius: 10, padding: 16, marginBottom: 14 },
  note: { fontSize: 13, opacity: 0.7, margin: "0 0 14px", lineHeight: 1.6 },
  row: { display: "flex", alignItems: "flex-end", gap: 10, marginBottom: 12 },
  rowWrap: { display: "flex", flexWrap: "wrap", gap: 14 },
  rowLabel: { width: 54, fontSize: 12, opacity: 0.6 },
  figure: { margin: 0, textAlign: "center" },
  frame: { imageRendering: "pixelated", background: "#121110", borderRadius: 4 },
  cap: { fontSize: 11, opacity: 0.55, marginTop: 4 },
  cell: { display: "flex", gap: 10, alignItems: "center", background: "#121110", padding: 8, borderRadius: 8 },
  cellLabel: { fontSize: 12 },
  sub: { opacity: 0.55, marginTop: 2 },
  bad: { color: "#ff9b9b" },
};
