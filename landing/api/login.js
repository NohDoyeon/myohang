// 묘항 로그인 API (Vercel 서버리스)
//
// 브라우저 → 여기 → Supabase(계정 확인) → **서명된 입장권** 발급 → 게임 클라이언트가 그걸 들고 게임 서버에 접속.
// 게임 서버는 서명만 검증하므로 이 함수와 통신할 필요가 없다 — 웹과 게임을 분리할 수 있는 이유가 이것이다.
//
// 비밀번호 규격은 게임 서버(`Harbor.Core/PasswordHash.cs`)와 **반드시 같아야** 한다:
//   PBKDF2-SHA256 · 120,000회 · 소금 16바이트 · 해시 32바이트 · 둘 다 base64
//
// 필요한 환경변수 (Vercel 프로젝트 설정에서, NEXT_PUBLIC_ 접두사 없이):
//   HARBOR_DB              Postgres 접속 문자열 — 서버리스이므로 **6543 트랜잭션 풀러**를 쓴다
//   HARBOR_TICKET_SECRET   입장권 서명 키 (게임 서버와 같은 값)
//   HARBOR_GAME_HOST       게임 서버 공개 주소 (예: myhome.duckdns.org)
//   HARBOR_GAME_PORT       게임 서버 포트 (기본 30000)

import crypto from "node:crypto";
import { Client } from "pg";
import { pgConfig } from "./_db.js";

const ITERATIONS = 120_000;
const SALT_BYTES = 16;
const HASH_BYTES = 32;
const TICKET_SECONDS = 300;      // 5분 — 게임 서버의 TicketStore.Lifetime 과 맞춘다
const NICK_MAX = 16;
const PW_MIN = 4, PW_MAX = 64;

const pbkdf2 = (password, salt) =>
  new Promise((resolve, reject) =>
    crypto.pbkdf2(password, salt, ITERATIONS, HASH_BYTES, "sha256", (e, key) => (e ? reject(e) : resolve(key))));

const b64url = (buf) => buf.toString("base64").replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");


/** `t1.<payload>.<서명>` — payload 는 `닉|만료(Unix초)`. 게임 서버가 같은 키로 검증한다. */
function issueTicket(nick, secret) {
  const payload = Buffer.from(`${nick}|${Math.floor(Date.now() / 1000) + TICKET_SECONDS}`, "utf8");
  const sig = crypto.createHmac("sha256", secret).update(payload).digest();
  return `t1.${b64url(payload)}.${b64url(sig)}`;
}

export default async function handler(req, res) {
  if (req.method !== "POST") return res.status(405).json({ ok: false, reason: "POST 만 받습니다" });

  // 붙여넣다 보면 앞뒤에 공백·줄바꿈이 붙는다(실측: PORT 가 "30000\n" 이었다).
  // 그대로 쓰면 주소가 `host:30000\n/ticket` 이 되어 접속이 깨지므로 항상 다듬는다.
  const env = (k) => (process.env[k] ?? "").trim();
  const [HARBOR_DB, HARBOR_TICKET_SECRET, HARBOR_GAME_HOST, HARBOR_GAME_PORT] =
    ["HARBOR_DB", "HARBOR_TICKET_SECRET", "HARBOR_GAME_HOST", "HARBOR_GAME_PORT"].map(env);

  if (!HARBOR_DB || !HARBOR_TICKET_SECRET || !HARBOR_GAME_HOST) {
    // 값을 그대로 노출하지 않고 **무엇이 비었는지만** 알려 준다.
    const missing = [!HARBOR_DB && "HARBOR_DB", !HARBOR_TICKET_SECRET && "HARBOR_TICKET_SECRET",
                     !HARBOR_GAME_HOST && "HARBOR_GAME_HOST"].filter(Boolean);
    return res.status(500).json({ ok: false, reason: `서버 설정이 빠졌어요 (${missing.join(", ")})` });
  }

  const nick = String(req.body?.nick ?? "").trim();
  const password = String(req.body?.password ?? "");
  if (nick.length === 0 || nick.length > NICK_MAX)
    return res.status(400).json({ ok: false, reason: `닉네임은 1~${NICK_MAX}자로 입력해 주세요` });
  if (password.length < PW_MIN || password.length > PW_MAX)
    return res.status(400).json({ ok: false, reason: `비밀번호는 ${PW_MIN}~${PW_MAX}자로 입력해 주세요` });

  const key = nick.toLowerCase();
  const db = new Client(pgConfig(HARBOR_DB));

  try {
    await db.connect();
    const found = await db.query("SELECT nick, salt, hash FROM account WHERE nick_key = $1", [key]);
    let created = false;

    if (found.rowCount === 0 || !found.rows[0].salt || !found.rows[0].hash) {
      // 가입은 **웹에서만** 한다(account 테이블의 주인이 웹이다). 게임 서버는 확인만 한다.
      const salt = crypto.randomBytes(SALT_BYTES).toString("base64");
      const hash = (await pbkdf2(password, Buffer.from(salt, "base64"))).toString("base64");
      await db.query(
        `INSERT INTO account (nick_key, nick, salt, hash, updated_at) VALUES ($1, $2, $3, $4, now())
         ON CONFLICT (nick_key) DO UPDATE SET nick = EXCLUDED.nick, salt = EXCLUDED.salt,
                                             hash = EXCLUDED.hash, updated_at = now()`,
        [key, nick, salt, hash]);
      created = found.rowCount === 0;
    } else {
      const { salt, hash } = found.rows[0];
      const actual = await pbkdf2(password, Buffer.from(salt, "base64"));
      const expected = Buffer.from(hash, "base64");
      const ok = actual.length === expected.length && crypto.timingSafeEqual(actual, expected);
      await db.query(
        "INSERT INTO login_log (nick_key, source, ok, detail, ip, at) VALUES ($1, 'web', $2, $3, $4, now())",
        [key, ok, ok ? "" : "비밀번호 불일치", (req.headers["x-forwarded-for"] ?? "").toString().split(",")[0]]);
      if (!ok) return res.status(400).json({ ok: false, reason: "비밀번호가 맞지 않아요" });
    }

    if (created)
      await db.query("INSERT INTO login_log (nick_key, source, ok, detail, ip, at) VALUES ($1, 'web', true, '가입', $2, now())",
        [key, (req.headers["x-forwarded-for"] ?? "").toString().split(",")[0]]);

    const ticket = issueTicket(nick, HARBOR_TICKET_SECRET);
    const port = HARBOR_GAME_PORT || "30000";
    return res.status(200).json({
      ok: true, nick, created, ticket,
      connect: `${HARBOR_GAME_HOST}:${port}/${ticket}`,   // 클라이언트가 이 한 줄이면 접속한다
      expiresInSeconds: TICKET_SECONDS,
    });
  } catch (e) {
    console.error("login failed", e);            // 자세한 내용은 서버 로그에만
    // 원인 코드만 함께 돌려준다(ENOTFOUND=주소 오타, ETIMEDOUT=막힘, 28P01=비밀번호 틀림, 3D000=DB 이름 틀림).
    // 값이 새지 않으면서 "어디서 막혔는지"를 바로 알 수 있다.
    return res.status(500).json({
      ok: false,
      reason: "지금은 로그인할 수 없어요. 잠시 후 다시 시도해 주세요.",
      code: e?.code ?? e?.name ?? "UNKNOWN",
    });
  } finally {
    await db.end().catch(() => {});
  }
}
