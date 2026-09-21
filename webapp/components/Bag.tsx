"use client";

// 가방. 가지고 있는 물건을 보여주고, **팔 수 있는 것**(수확물)은 여기서 판다.
//
// 서버가 진실이다 — 팔고 나서 화면을 직접 고치지 않는다. `S_InventoryUpdate` 와 `S_WalletUpdate` 가
// 돌아오면 그때 바뀐다. 직접 고치면 서버가 거절했을 때 화면만 틀린 값을 갖게 된다.

import { useMemo, useState } from "react";
import type { InvEntry } from "@/lib/protocol/packets";
import { C, S, btn, btnGhost, panel } from "@/lib/ui/tokens";

export interface BagProps {
  items: InvEntry[];
  rupee: number | null;
  onSell: (furniId: string, qty: number) => void;
  /** 놓기를 시작한다. 실제 배치는 방을 클릭할 때 일어난다. */
  onPlace: (furniId: string) => void;
  placing: string | null;
  onClose: () => void;
}

export default function Bag({ items, rupee, onSell, onPlace, placing, onClose }: BagProps) {
  const [tab, setTab] = useState<"all" | "sellable">("all");

  const shown = useMemo(
    () => (tab === "sellable" ? items.filter((i) => i.sellPrice > 0) : items),
    [items, tab],
  );

  return (
    <section style={{ ...panel, marginTop: 10 }}>
      <header style={st.head}>
        <strong style={st.title}>가방</strong>
        <button onClick={() => setTab("all")} style={tab === "all" ? st.tabOn : st.tab}>전체</button>
        <button onClick={() => setTab("sellable")} style={tab === "sellable" ? st.tabOn : st.tab}>팔 수 있는 것</button>
        <span style={st.rupee}>{rupee === null ? "" : `${rupee.toLocaleString()} 루피`}</span>
        <button onClick={onClose} style={st.close} aria-label="가방 닫기">✕</button>
      </header>

      {shown.length === 0 ? (
        <p style={st.empty}>
          {tab === "sellable" ? "팔 수 있는 물건이 없어요. 화분을 키워 수확해 보세요." : "가방이 비어 있어요."}
        </p>
      ) : (
        <ul style={st.list}>
          {shown.map((it) => (
            <Row
              key={it.furniId}
              item={it}
              placing={placing === it.furniId}
              onSell={onSell}
              onPlace={onPlace}
            />
          ))}
        </ul>
      )}
    </section>
  );
}

function Row({ item, placing, onSell, onPlace }: {
  item: InvEntry; placing: boolean;
  onSell: BagProps["onSell"]; onPlace: BagProps["onPlace"];
}) {
  // 묶음이 낱개보다 이득인지 계산해서 보여준다. 서버가 묶음을 먼저 채워 계산하므로
  // "전부 팔기"를 누르면 자동으로 유리한 쪽이 적용된다.
  const bundle = item.sellBundleQty > 0 && item.sellBundlePrice > 0;
  const perBundle = bundle ? item.sellBundlePrice / item.sellBundleQty : 0;
  const worthBundling = bundle && perBundle > item.sellPrice;

  return (
    <li style={{ ...st.row, ...(placing ? st.rowPlacing : null) }}>
      <span style={st.name}>
        {item.name}
        {item.wall && <em style={st.tag}>벽걸이</em>}
      </span>
      <span style={st.qty}>{item.qty}개</span>

      {item.sellPrice > 0 && (
        <span style={st.price}>
          1개 {item.sellPrice}
          {worthBundling && (
            <em style={st.bundle}>
              {item.sellBundleQty}개 묶음 {item.sellBundlePrice}
            </em>
          )}
        </span>
      )}

      <span style={st.actions}>
        <button onClick={() => onPlace(item.furniId)} style={placing ? btn : btnGhost}>
          {placing ? "놓는 중…" : "놓기"}
        </button>
        {item.sellPrice > 0 && (
          <>
            <button onClick={() => onSell(item.furniId, 1)} style={btnGhost}>1개 팔기</button>
            <button onClick={() => onSell(item.furniId, item.qty)} style={btn}>전부 팔기</button>
          </>
        )}
      </span>
    </li>
  );
}

const st: Record<string, React.CSSProperties> = {
  head: { display: "flex", alignItems: "center", gap: S.gap, marginBottom: 10, flexWrap: "wrap" },
  title: { fontSize: 14 },
  tab: { ...btnGhost, padding: "5px 10px", fontSize: 12.5, color: C.textDim },
  tabOn: { ...btnGhost, padding: "5px 10px", fontSize: 12.5, borderColor: C.accent, color: C.accent },
  rupee: { marginLeft: "auto", fontSize: 13, color: C.accent, fontWeight: 600 },
  close: { ...btnGhost, padding: "4px 9px", fontSize: 12 },
  empty: { margin: "14px 4px", fontSize: 13, color: C.textDim },
  list: { listStyle: "none", margin: 0, padding: 0, display: "flex", flexDirection: "column", gap: 6, maxHeight: 260, overflowY: "auto" },
  row: {
    display: "flex", alignItems: "center", gap: S.gap, flexWrap: "wrap",
    padding: "8px 10px", background: C.inputBg, borderRadius: S.radius,
    border: "1px solid transparent",
  },
  rowPlacing: { borderColor: C.accent },
  name: { fontSize: 13.5, minWidth: 120 },
  tag: { marginLeft: 6, fontSize: 11, color: C.info, fontStyle: "normal" },
  qty: { fontSize: 13, color: C.textDim, minWidth: 44 },
  price: { fontSize: 12, color: C.textFaint },
  bundle: { marginLeft: 8, fontStyle: "normal", color: C.good },
  actions: { marginLeft: "auto", display: "flex", gap: 6, flexWrap: "wrap" },
};
