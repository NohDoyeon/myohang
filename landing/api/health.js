// 설정 점검용. `https://<사이트>/api/health` 로 열어 본다.
//   · 그냥 열면 → 환경변수가 있는지/길이만 (값은 절대 돌려주지 않는다)
//   · `?db=1` 을 붙이면 → **DB 연결까지 시험**하고 결과만 돌려준다
//
// 길이를 같이 보는 이유: 따옴표째 붙여넣거나 앞뒤 공백이 섞이면 길이로 티가 난다.
// (예: 64자여야 할 서명 키가 66자면 따옴표가 같이 들어간 것이다.)

import { Client } from "pg";
import { pgConfig } from "./_db.js";

const KEYS = ["HARBOR_DB", "HARBOR_TICKET_SECRET", "HARBOR_GAME_HOST", "HARBOR_GAME_PORT"];

export default async function handler(req, res) {
  const seen = {};
  for (const k of KEYS) {
    const v = process.env[k];
    seen[k] = v ? { present: true, length: v.length, trimmedSame: v === v.trim() } : { present: false };
  }

  const out = {
    ok: KEYS.every((k) => seen[k].present || k === "HARBOR_GAME_PORT"),   // PORT 는 기본값이 있어 없어도 된다
    env: seen,
    similar: Object.keys(process.env).filter((k) => k.toUpperCase().includes("HARBOR")),
    region: process.env.VERCEL_REGION ?? null,
  };

  if (req.query?.db && process.env.HARBOR_DB) {
    const cfg = pgConfig(process.env.HARBOR_DB);
    const db = new Client(cfg);
    try {
      await db.connect();
      const r = await db.query("SELECT count(*)::int AS accounts FROM account");
      // 어디에 붙었는지도 알려 준다(비밀번호는 제외) — 포트가 5432/6543 중 무엇인지 바로 보인다.
      out.db = { ok: true, host: cfg.host ?? "(uri)", port: cfg.port ?? null, accounts: r.rows[0].accounts };
    } catch (e) {
      out.db = { ok: false, code: e?.code ?? e?.name ?? "UNKNOWN", host: cfg.host ?? "(uri)", port: cfg.port ?? null };
    } finally {
      await db.end().catch(() => {});
    }
  }

  res.status(200).json(out);
}
