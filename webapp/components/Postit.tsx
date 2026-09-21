"use client";

// 포스트잇 읽기·쓰기.
//
// **남의 방에 물건을 놓는 권한 예외는 포스트잇과 캣닢뿐이다** — 이게 사교의 한 축이라
// 남의 방에 들렀을 때 한마디 남기고 가는 흐름이 매끄러워야 한다.
//
// 모바일에서 쓸 일이 많다(놀러 가서 글 남기기). 그래서 화면 아래에 붙는 시트 형태로,
// 입력칸과 버튼을 손가락으로 누를 만한 크기로 둔다.

import { useEffect, useRef, useState } from "react";
import type { ItemDto } from "@/lib/protocol/packets";
import { C, S, btn, btnGhost } from "@/lib/ui/tokens";

const BODY_MAX = 200;      // 서버가 이 길이에서 자른다 (RoomInstance.OnPostitWrite)

export interface PostitProps {
  item: ItemDto;
  /** 내가 붙인 것인가. 쓰기는 **글쓴이 본인만** 된다(서버도 같은 판정을 한다). */
  mine: boolean;
  onWrite: (itemId: number, body: string) => void;
  onPick: (itemId: number) => void;
  onClose: () => void;
}

export default function Postit({ item, mine, onWrite, onPick, onClose }: PostitProps) {
  const [editing, setEditing] = useState(mine && (item.extra ?? "").length === 0);
  const [body, setBody] = useState(item.extra ?? "");
  const area = useRef<HTMLTextAreaElement | null>(null);

  useEffect(() => { if (editing) area.current?.focus(); }, [editing]);

  // 내용이 서버에서 갱신되면(다른 기기에서 고쳤거나 방금 쓴 것이 돌아옴) 따라간다.
  useEffect(() => { if (!editing) setBody(item.extra ?? ""); }, [item.extra, editing]);

  const save = () => {
    onWrite(item.id, body.trim());
    setEditing(false);
  };

  return (
    <div style={st.backdrop} onClick={onClose}>
      <section style={st.sheet} onClick={(e) => e.stopPropagation()}>
        <header style={st.head}>
          <span style={st.pin} />
          <strong style={st.title}>{item.name || "포스트잇"}</strong>
          {item.author && <span style={st.author}>{item.author}</span>}
          <button onClick={onClose} style={st.close} aria-label="닫기">✕</button>
        </header>

        {editing ? (
          <>
            <textarea
              ref={area}
              value={body}
              onChange={(e) => setBody(e.target.value.slice(0, BODY_MAX))}
              placeholder="여기에 남길 말을 적어 주세요"
              style={st.input}
              rows={4}
            />
            <div style={st.row}>
              <span style={st.count}>{body.length}/{BODY_MAX}</span>
              <button onClick={() => setEditing(false)} style={btnGhost}>취소</button>
              <button onClick={save} style={btn}>남기기</button>
            </div>
          </>
        ) : (
          <>
            <p style={st.body}>
              {/* React 기본 텍스트 렌더링 — HTML 이 섞여 와도 태그로 해석되지 않는다.
                  dangerouslySetInnerHTML 을 쓰면 그 순간 뚫린다. */}
              {(item.extra ?? "").length > 0 ? item.extra : <em style={st.blank}>아직 비어 있어요.</em>}
            </p>
            {mine && (
              <div style={st.row}>
                <button onClick={() => onPick(item.id)} style={st.remove}>떼기</button>
                <button onClick={() => setEditing(true)} style={btn}>고쳐 쓰기</button>
              </div>
            )}
          </>
        )}
      </section>
    </div>
  );
}

const st: Record<string, React.CSSProperties> = {
  backdrop: {
    position: "fixed", inset: 0, background: "rgba(0,0,0,0.45)",
    display: "flex", alignItems: "flex-end", justifyContent: "center", zIndex: 50,
  },
  // 모바일에서는 아래에 붙는 시트, 넓은 화면에서는 가운데 카드처럼 보인다.
  sheet: {
    width: "100%", maxWidth: 420,
    background: C.panel, border: `1px solid ${C.panelEdge}`,
    borderRadius: `${S.radiusLg}px ${S.radiusLg}px 0 0`,
    padding: 16, paddingBottom: "max(16px, env(safe-area-inset-bottom))",
    margin: "0 auto",
  },
  head: { display: "flex", alignItems: "center", gap: S.gap, marginBottom: 10 },
  pin: { width: 9, height: 9, borderRadius: "50%", background: C.accent, flexShrink: 0 },
  title: { fontSize: 14 },
  author: { fontSize: 12.5, color: C.info },
  close: { ...btnGhost, marginLeft: "auto", padding: "6px 12px", fontSize: 13 },
  body: { margin: "4px 2px 12px", fontSize: 14.5, lineHeight: 1.75, whiteSpace: "pre-wrap", wordBreak: "break-word" },
  blank: { color: C.textFaint, fontStyle: "normal" },
  input: {
    width: "100%", padding: 12, borderRadius: S.radius,
    border: `1px solid ${C.inputEdge}`, background: C.inputBg, color: "inherit",
    fontSize: 15, lineHeight: 1.7, resize: "vertical", fontFamily: "inherit",
  },
  row: { display: "flex", alignItems: "center", gap: S.gap, marginTop: 10 },
  count: { fontSize: 12, color: C.textFaint, marginRight: "auto" },
  remove: { ...btnGhost, color: C.bad, marginRight: "auto" },
};
