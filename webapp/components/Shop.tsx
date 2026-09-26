"use client";

// 상점. 루피로 가구를 사면 가방에 들어간다.
//
// 서버가 진실이다 — 사고 나서 화면을 직접 고치지 않는다. `S_WalletUpdate` 와 `S_InventoryUpdate` 가
// 돌아오면 그때 바뀐다. 카탈로그는 로그인 직후 `S_Catalog` 로 통째로 오므로 따로 요청하지 않아도 된다.
//
// **값이 0 인 정의는 애초에 오지 않는다** — 그건 수확물(파는 물건)이지 사는 물건이 아니다.
// 서버가 `Handlers.Catalog` 에서 `price.amount > 0` 만 걸러 보낸다(0루피에 사서 파는 구멍 차단).

import { useMemo, useState } from "react";
import type { CatalogEntry } from "@/lib/protocol/packets";
import { furniThumb } from "@/lib/game/furni-files";
import { C, S, btn, btnGhost, panel } from "@/lib/ui/tokens";

export interface ShopProps {
  catalog: CatalogEntry[];
  rupee: number | null;
  onBuy: (furniId: string, qty: number) => void;
  onClose: () => void;
}

/** Godot 의 `HudView.CategoryName` 과 같은 말을 쓴다 — 두 클라가 다른 이름을 쓰면 안 된다. */
const CATEGORY: Record<string, string> = {
  appliance: "가전",
  seating: "의자",
  table: "탁자",
  consumable: "소모품",
  deco: "장식",
  wallart: "벽 장식",
  plant: "식물 (키우기)",
  postit: "포스트잇 (방명록)",
  harvest: "수확물 (팔기)",
};

/** 서버가 한 번에 파는 최대 수량 (`C_BuyCatalog` 가 1~10 으로 자른다). */
const QTY_MAX = 10;

export default function Shop({ catalog, rupee, onBuy, onClose }: ShopProps) {
  const [only, setOnly] = useState<string | null>(null);

  // 카탈로그는 서버가 이미 분류·가격순으로 정렬해 보낸다. 그 순서를 지키면서 묶기만 한다.
  const groups = useMemo(() => {
    const out: { category: string; items: CatalogEntry[] }[] = [];
    for (const c of catalog) {
      if (only !== null && c.category !== only) continue;
      const last = out[out.length - 1];
      if (last && last.category === c.category) last.items.push(c);
      else out.push({ category: c.category, items: [c] });
    }
    return out;
  }, [catalog, only]);

  const categories = useMemo(
    () => [...new Set(catalog.map((c) => c.category))],
    [catalog],
  );

  return (
    <section style={{ ...panel, marginTop: 10 }}>
      <header style={st.head}>
        <strong style={st.title}>상점</strong>
        <span style={st.rupee}>{rupee === null ? "" : `${rupee.toLocaleString()} 루피`}</span>
        <button onClick={onClose} style={st.close} aria-label="상점 닫기">✕</button>
      </header>

      <p style={st.hint}>
        산 물건은 <b>가방</b>으로 들어가요. 놓으려면 가방에서 [놓기]를 누르고 방을 클릭하세요.
        포스트잇은 <b>다른 사람 방 벽에도</b> 붙일 수 있어요.
      </p>

      {catalog.length === 0 ? (
        <p style={st.empty}>카탈로그를 불러오는 중…</p>
      ) : (
        <>
          <div style={st.tabs}>
            <button onClick={() => setOnly(null)} style={only === null ? st.tabOn : st.tab}>전체</button>
            {categories.map((cat) => (
              <button key={cat} onClick={() => setOnly(cat)} style={only === cat ? st.tabOn : st.tab}>
                {CATEGORY[cat] ?? cat}
              </button>
            ))}
          </div>

          <div style={st.scroll}>
            {groups.map((g) => (
              <div key={g.category}>
                <h3 style={st.group}>{CATEGORY[g.category] ?? g.category}</h3>
                <ul style={st.list}>
                  {g.items.map((c) => (
                    <Row key={c.furniId} entry={c} rupee={rupee} onBuy={onBuy} />
                  ))}
                </ul>
              </div>
            ))}
          </div>
        </>
      )}
    </section>
  );
}

function Row({ entry, rupee, onBuy }: {
  entry: CatalogEntry; rupee: number | null; onBuy: ShopProps["onBuy"];
}) {
  const [qty, setQty] = useState(1);
  const thumb = furniThumb(entry.furniId);
  // 루피가 모자라면 눌러 봐야 서버가 거절할 뿐이다. 버튼에서 미리 막고 이유를 보여준다.
  const total = entry.price * qty;
  const buyable = entry.currency === "rupee" && rupee !== null && rupee >= total;

  return (
    <li style={st.row}>
      <span style={st.thumb}>
        {/* 그림이 아직 없는 가구가 많다. 없으면 빈 칸으로 두고 이름만 보여준다.
            next/image 를 쓰지 않는 이유: 28px 도트 그림이라 최적화할 것이 없고,
            리사이즈가 끼면 오히려 뭉개진다. */}
        {/* eslint-disable-next-line @next/next/no-img-element */}
        {thumb && <img src={thumb} alt="" width={28} height={28} style={st.thumbImg} />}
      </span>

      <span style={st.name}>
        {entry.name}
        {entry.wall && <em style={st.tag}>벽걸이</em>}
      </span>

      <span style={st.price}>
        {entry.price.toLocaleString()} 루피
        {qty > 1 && <em style={st.total}>×{qty} = {total.toLocaleString()}</em>}
      </span>

      <span style={st.actions}>
        <label style={st.qtyLabel}>
          <span style={st.srOnly}>{entry.name} 수량</span>
          <select
            value={qty}
            onChange={(e) => setQty(Number(e.target.value))}
            style={st.qty}
          >
            {Array.from({ length: QTY_MAX }, (_, i) => i + 1).map((n) => (
              <option key={n} value={n}>{n}개</option>
            ))}
          </select>
        </label>
        <button
          onClick={() => onBuy(entry.furniId, qty)}
          style={buyable ? btn : st.disabled}
          disabled={!buyable}
        >
          {entry.currency !== "rupee" ? "살 수 없음" : buyable ? "구매" : "루피 부족"}
        </button>
      </span>
    </li>
  );
}

const st: Record<string, React.CSSProperties> = {
  head: { display: "flex", alignItems: "center", gap: S.gap, marginBottom: 8 },
  title: { fontSize: 14 },
  rupee: { marginLeft: "auto", fontSize: 13, color: C.accent, fontWeight: 600 },
  close: { ...btnGhost, padding: "4px 9px", fontSize: 12 },
  hint: { margin: "0 0 12px", fontSize: 12.5, color: C.textDim, lineHeight: 1.6 },
  empty: { margin: "14px 4px", fontSize: 13, color: C.textDim },
  tabs: { display: "flex", gap: 6, flexWrap: "wrap", marginBottom: 10 },
  tab: { ...btnGhost, padding: "5px 10px", fontSize: 12.5, color: C.textDim },
  tabOn: { ...btnGhost, padding: "5px 10px", fontSize: 12.5, borderColor: C.accent, color: C.accent },
  scroll: { maxHeight: 300, overflowY: "auto" },
  group: { fontSize: 12, color: C.textFaint, margin: "10px 2px 6px", fontWeight: 600 },
  list: { listStyle: "none", margin: 0, padding: 0, display: "flex", flexDirection: "column", gap: 6 },
  row: {
    display: "flex", alignItems: "center", gap: S.gap, flexWrap: "wrap",
    padding: "7px 10px", background: C.inputBg, borderRadius: S.radius,
  },
  thumb: { width: 28, height: 28, display: "grid", placeItems: "center", flex: "0 0 auto" },
  // 도트 그림이다 — 부드럽게 늘리면 뭉개진다.
  thumbImg: { imageRendering: "pixelated", objectFit: "contain" },
  name: { fontSize: 13.5, minWidth: 110 },
  tag: { marginLeft: 6, fontSize: 11, color: C.info, fontStyle: "normal" },
  price: { fontSize: 12.5, color: C.textFaint, marginLeft: "auto" },
  total: { marginLeft: 6, fontStyle: "normal", color: C.accent },
  actions: { display: "flex", gap: 6, alignItems: "center" },
  qtyLabel: { display: "flex" },
  qty: {
    padding: "7px 6px", borderRadius: S.radius, fontSize: 12.5,
    background: C.panel, color: C.text, border: `1px solid ${C.inputEdge}`,
  },
  disabled: { ...btnGhost, opacity: 0.45, cursor: "default" },
  srOnly: { position: "absolute", width: 1, height: 1, overflow: "hidden", clip: "rect(0 0 0 0)", whiteSpace: "nowrap" },
};
