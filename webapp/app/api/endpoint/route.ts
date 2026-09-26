// 지금 게임 서버가 어디에 떠 있는지 알려 준다.
//
// 터널 주소는 켤 때마다 바뀐다. 그걸 빌드 타임 환경변수(`NEXT_PUBLIC_GAME_WS`)로만 알면
// 주소가 바뀔 때마다 Vercel 값을 고치고 **재배포**해야 한다 — 그 사이 아무도 못 들어온다.
// 그래서 주소를 아는 쪽(게임 서버)이 DB 에 적고, 여기서 읽어 브라우저에 준다.
//   게임 서버 `EndpointService` → `server_endpoint` 테이블 → 이 라우트 → `/play`
//
// 돌려주는 값은 **공개 주소**다(브라우저가 붙을 곳). 비밀이 아니다.
// 반대로 `HARBOR_DB` 는 여기서만 쓰이고 브라우저로 내려가지 않는다.

import { Client } from "pg";
import { pgConfig } from "@/lib/db";

export const runtime = "nodejs";        // pg 는 Edge 런타임에서 못 돈다

// 이 Next 에서는 Route Handler 가 **기본적으로 캐시되지 않는다**(node_modules/next/dist/docs
// 01-app/01-getting-started/15-route-handlers.md). 그래서 `dynamic` 을 따로 걸지 않는다.
// 다만 앞단(CDN)·브라우저가 들고 있으면 옛 주소를 계속 내주므로 응답에 `no-store` 는 붙인다.

/** 심장박동은 30초마다 온다. 세 번을 놓치면 꺼진 것으로 본다(강제 종료되면 online 이 true 로 남는다). */
const STALE_SECONDS = 90;

export async function GET() {
  const env = (k: string) => (process.env[k] ?? "").trim();
  const fallback = env("NEXT_PUBLIC_GAME_WS");
  const HARBOR_DB = env("HARBOR_DB");

  // DB 가 없어도 화면이 죽지는 않게 — 예전 방식(빌드 타임 주소)으로 물러난다.
  if (!HARBOR_DB) return json({ ok: true, ws: fallback, online: null, source: "env" });

  const db = new Client(pgConfig(HARBOR_DB));
  try {
    await db.connect();
    const r = await db.query(
      "SELECT ws_url, online, EXTRACT(EPOCH FROM (now() - updated_at)) AS age FROM server_endpoint WHERE name = 'game'");

    if (r.rowCount === 0 || !r.rows[0].ws_url)
      return json({ ok: true, ws: fallback, online: null, source: "env" });

    const age = Math.max(0, Math.round(Number(r.rows[0].age) || 0));
    return json({
      ok: true,
      ws: String(r.rows[0].ws_url),
      online: r.rows[0].online === true && age <= STALE_SECONDS,
      ageSeconds: age,
      source: "db",
    });
  } catch (e) {
    console.error("endpoint lookup failed", e);   // 자세한 내용은 서버 로그에만
    // 읽기에 실패했다고 못 들어가게 하지는 않는다. 빌드 타임 주소가 있으면 그걸 준다.
    return json({ ok: true, ws: fallback, online: null, source: "env" });
  } finally {
    await db.end().catch(() => {});
  }
}

const json = (body: unknown) =>
  Response.json(body, { headers: { "Cache-Control": "no-store" } });
