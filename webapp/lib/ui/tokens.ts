// 화면 기준값. **여기 없는 색·간격을 화면에서 직접 쓰지 않는다** — 그래야 나중에 한 번에 바꿀 수 있다.
//
// 색은 Godot 클라의 `Ui.cs` 에서 가져왔고, 그건 `art/palette48.json` 에서 뽑은 것이다.
// 아트(고양이 셔츠 #A0AFCE, 크림 #FCF0E1)와 같은 계열이라 화면과 캐릭터가 따로 놀지 않는다.

export const C = {
  /** 화면 바탕 — 캔버스보다 조금 밝게 해서 게임 화면이 도드라지게 한다. */
  bg: "#121110",
  panel: "#1b1a18",
  panelEdge: "#2f2d2a",
  inputBg: "#0e0d0c",
  inputEdge: "#3a3733",

  text: "#e8e1d6",
  textDim: "rgba(232,225,214,0.62)",
  textFaint: "rgba(232,225,214,0.38)",

  /** 강조 — 루피·확인 버튼. 항구의 나무·황동 느낌. */
  accent: "#c98c4b",
  accentText: "#1b1a18",
  /** 남의 이름·정보 */
  info: "#9ab0c6",
  good: "#9ae6a0",
  bad: "#ff9b9b",
} as const;

export const S = {
  radius: 8,
  radiusLg: 12,
  gap: 8,
  pad: 12,
} as const;

/** 자주 쓰는 조합. 화면마다 같은 버튼을 다시 그리지 않도록. */
export const btn: React.CSSProperties = {
  padding: "8px 14px",
  borderRadius: S.radius,
  border: 0,
  background: C.accent,
  color: C.accentText,
  fontWeight: 600,
  fontSize: 13.5,
  cursor: "pointer",
};

export const btnGhost: React.CSSProperties = {
  ...btn,
  background: "transparent",
  color: C.text,
  border: `1px solid ${C.inputEdge}`,
  fontWeight: 500,
};

export const panel: React.CSSProperties = {
  background: C.panel,
  border: `1px solid ${C.panelEdge}`,
  borderRadius: S.radiusLg,
  padding: S.pad,
};
