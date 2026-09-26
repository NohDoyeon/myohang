"use client";

// 첫 화면 — 로그인해서 바로 게임으로 들어간다.
//
// 입장권은 **sessionStorage** 로 넘긴다. 주소창(`/play?t=…`)에 실으면 서버 접근 로그·브라우저 히스토리·
// 프록시에 그대로 남는다. 입장권은 5분간 그 계정으로 입장할 수 있는 열쇠다.

import { useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { TICKET_KEY } from "@/lib/ticket";

export default function Home() {
  const router = useRouter();
  const [nick, setNick] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  /** 게임 서버가 켜져 있는지. `null` = 아직 모름 — 그때는 아무 말도 하지 않는다. */
  const [open, setOpen] = useState<boolean | null>(null);

  // 로그인부터 시키고 나서 "서버가 꺼져 있어요" 하면 허탕이다. 미리 알려 준다.
  useEffect(() => {
    let dead = false;
    fetch("/api/endpoint", { cache: "no-store" })
      .then((r) => r.json())
      .then((d: { online?: unknown }) => { if (!dead && typeof d.online === "boolean") setOpen(d.online); })
      .catch(() => { /* 몰라도 로그인은 막지 않는다 */ });
    return () => { dead = true; };
  }, []);

  const submit = useCallback(async (e: React.FormEvent) => {
    e.preventDefault();
    if (busy) return;
    setBusy(true);
    setError("");
    try {
      const res = await fetch("/api/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ nick: nick.trim(), password }),
      });
      const data = await res.json();
      if (!data.ok) { setError(data.reason ?? "로그인에 실패했어요"); return; }

      sessionStorage.setItem(TICKET_KEY, data.ticket);
      router.push("/play");
    } catch {
      setError("서버에 연결할 수 없어요. 잠시 후 다시 시도해 주세요.");
    } finally {
      setBusy(false);
    }
  }, [busy, nick, password, router]);

  return (
    <main style={S.page}>
      <div style={S.card}>
        <h1 style={S.title}>묘항</h1>
        <p style={S.tagline}>고양이들이 사는 항구 마을</p>

        {open !== null && (
          <p style={{ ...S.state, color: open ? "#9ae6a0" : "#ff9b9b" }}>
            {open ? "● 지금 열려 있어요" : "● 지금은 닫혀 있어요 — 잠시 뒤에 다시 와 주세요"}
          </p>
        )}

        <form onSubmit={submit} style={S.form}>
          <label style={S.label}>
            닉네임
            <input
              value={nick}
              onChange={(e) => setNick(e.target.value.slice(0, 16))}
              style={S.input}
              placeholder="1~16자"
              autoComplete="username"
              autoFocus
            />
          </label>
          <label style={S.label}>
            비밀번호
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              style={S.input}
              placeholder="4~64자"
              autoComplete="current-password"
            />
          </label>

          {error && <p style={S.error}>{error}</p>}

          <button type="submit" style={S.button} disabled={busy || nick.trim().length === 0 || password.length === 0}>
            {busy ? "들어가는 중…" : "시작하기"}
          </button>
        </form>

        <p style={S.note}>
          처음 쓰는 닉네임이면 그 자리에서 가입됩니다.
          <br />
          아직 테스트 중이라 <b>아무 데도 쓰지 않는 비밀번호</b>를 써 주세요.
        </p>
      </div>
    </main>
  );
}

const S: Record<string, React.CSSProperties> = {
  page: { minHeight: "100dvh", display: "grid", placeItems: "center", padding: 16 },
  card: { width: "100%", maxWidth: 360, background: "#1b1a18", border: "1px solid #2f2d2a", borderRadius: 14, padding: "28px 24px" },
  title: { fontSize: 30, margin: "0 0 4px", letterSpacing: "0.06em" },
  tagline: { fontSize: 13, opacity: 0.6, margin: "0 0 12px" },
  state: { fontSize: 12.5, margin: "0 0 18px" },
  form: { display: "flex", flexDirection: "column", gap: 14 },
  label: { display: "flex", flexDirection: "column", gap: 6, fontSize: 13 },
  input: { padding: "11px 12px", borderRadius: 8, border: "1px solid #3a3733", background: "#121110", color: "inherit", fontSize: 15 },
  button: { marginTop: 4, padding: "12px", borderRadius: 8, border: 0, background: "#c98c4b", color: "#1b1a18", fontWeight: 700, fontSize: 15, cursor: "pointer" },
  error: { margin: 0, fontSize: 13, color: "#ff9b9b" },
  note: { fontSize: 12, opacity: 0.55, lineHeight: 1.7, margin: "20px 0 0" },
};
