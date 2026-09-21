// 묘항 로그인. 브라우저 → 여기 → Supabase(계정 확인) → **서명된 입장권**.
//
// 게임 서버는 서명만 검증하므로 이 함수와 통신하지 않는다 — 웹과 게임을 분리할 수 있는 이유가 이것이다.
// 웹 클라이언트는 받은 입장권으로 `wss://…/ws` 에 붙는다. **비밀번호는 게임 서버로 가지 않는다.**
//
// 비밀번호 규격은 게임 서버(`Harbor.Core/PasswordHash.cs`)와 **반드시 같아야** 한다:
//   PBKDF2-SHA256 · 120,000회 · 소금 16바이트 · 해시 32바이트 · 둘 다 base64
//
// 환경변수 (NEXT_PUBLIC_ 접두사를 붙이지 않는다 — 붙이면 번들에 박혀 공개된다):
//   HARBOR_DB              Postgres 접속 문자열 (서버리스이므로 6543 트랜잭션 풀러 권장)
//   HARBOR_TICKET_SECRET   입장권 서명 키 (게임 서버와 같은 값)

import crypto from "node:crypto";
import { Client } from "pg";
import { pgConfig } from "@/lib/db";

export const runtime = "nodejs";        // pg 는 Edge 런타임에서 못 돈다

const ITERATIONS = 120_000;
const SALT_BYTES = 16;
const HASH_BYTES = 32;
const TICKET_SECONDS = 300;             // 5분 — 게임 서버의 TicketStore.Lifetime 과 맞춘다
const NICK_MAX = 16;
const PW_MIN = 4;
const PW_MAX = 64;

const pbkdf2 = (password: string, salt: Buffer): Promise<Buffer> =>
  new Promise((resolve, reject) =>
    crypto.pbkdf2(password, salt, ITERATIONS, HASH_BYTES, "sha256", (e, key) => (e ? reject(e) : resolve(key))));

const b64url = (buf: Buffer) =>
  buf.toString("base64").replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");

/** `t1.<payload>.<서명>` — payload 는 `닉|만료(Unix초)`. 게임 서버가 같은 키로 검증한다. */
function issueTicket(nick: string, secret: string) {
  const payload = Buffer.from(`${nick}|${Math.floor(Date.now() / 1000) + TICKET_SECONDS}`, "utf8");
  const sig = crypto.createHmac("sha256", secret).update(payload).digest();
  return `t1.${b64url(payload)}.${b64url(sig)}`;
}

export async function POST(request: Request) {
  // 붙여넣다 보면 앞뒤에 공백·줄바꿈이 붙는다(실측: PORT 가 "30000\n" 이었다). 항상 다듬는다.
  const env = (k: string) => (process.env[k] ?? "").trim();
  const HARBOR_DB = env("HARBOR_DB");
  const HARBOR_TICKET_SECRET = env("HARBOR_TICKET_SECRET");

  if (!HARBOR_DB || !HARBOR_TICKET_SECRET) {
    // 값을 노출하지 않고 **무엇이 비었는지만** 알려 준다.
    const missing = [!HARBOR_DB && "HARBOR_DB", !HARBOR_TICKET_SECRET && "HARBOR_TICKET_SECRET"].filter(Boolean);
    return Response.json({ ok: false, reason: `서버 설정이 빠졌어요 (${missing.join(", ")})` }, { status: 500 });
  }

  let body: { nick?: unknown; password?: unknown };
  try {
    body = await request.json();
  } catch {
    return Response.json({ ok: false, reason: "요청을 읽지 못했어요" }, { status: 400 });
  }

  const nick = String(body.nick ?? "").trim();
  const password = String(body.password ?? "");
  if (nick.length === 0 || nick.length > NICK_MAX)
    return Response.json({ ok: false, reason: `닉네임은 1~${NICK_MAX}자로 입력해 주세요` }, { status: 400 });
  if (password.length < PW_MIN || password.length > PW_MAX)
    return Response.json({ ok: false, reason: `비밀번호는 ${PW_MIN}~${PW_MAX}자로 입력해 주세요` }, { status: 400 });

  const key = nick.toLowerCase();
  const ip = (request.headers.get("x-forwarded-for") ?? "").split(",")[0].trim();
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
      await db.query(
        "INSERT INTO login_log (nick_key, source, ok, detail, ip, at) VALUES ($1, 'web', true, $2, $3, now())",
        [key, created ? "가입" : "비밀번호 설정", ip]);
    } else {
      const { salt, hash } = found.rows[0];
      const actual = await pbkdf2(password, Buffer.from(salt, "base64"));
      const expected = Buffer.from(hash, "base64");
      const ok = actual.length === expected.length && crypto.timingSafeEqual(actual, expected);
      await db.query(
        "INSERT INTO login_log (nick_key, source, ok, detail, ip, at) VALUES ($1, 'web', $2, $3, $4, now())",
        [key, ok, ok ? "" : "비밀번호 불일치", ip]);
      if (!ok) return Response.json({ ok: false, reason: "비밀번호가 맞지 않아요" }, { status: 400 });
    }

    return Response.json({
      ok: true,
      nick,
      created,
      ticket: issueTicket(nick, HARBOR_TICKET_SECRET),
      expiresInSeconds: TICKET_SECONDS,
    });
  } catch (e) {
    console.error("login failed", e);      // 자세한 내용은 서버 로그에만
    // 원인 코드만 함께 돌려준다(ENOTFOUND=주소 오타, ETIMEDOUT=막힘, 28P01=비밀번호 틀림).
    // 값이 새지 않으면서 어디서 막혔는지 바로 알 수 있다.
    const code = (e as { code?: string; name?: string })?.code ?? (e as Error)?.name ?? "UNKNOWN";
    return Response.json(
      { ok: false, reason: "지금은 로그인할 수 없어요. 잠시 후 다시 시도해 주세요.", code },
      { status: 500 });
  } finally {
    await db.end().catch(() => {});
  }
}
