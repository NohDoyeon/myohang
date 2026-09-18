// 접속 문자열 해석. `_` 로 시작하는 파일은 Vercel 이 엔드포인트로 노출하지 않는다.
//
// 게임 서버(.NET)는 **Npgsql 형식**(`Host=…;Port=…;Username=…`)을 쓰고,
// Node 의 `pg` 는 **URI 형식**(`postgresql://…`)을 기대한다.
// 같은 값을 두 벌로 관리하면 반드시 어긋나므로 **둘 다 받아들인다.**

export function pgConfig(raw) {
  const s = (raw ?? "").trim();
  if (/^postgres(ql)?:\/\//i.test(s)) return { connectionString: s, ssl: { rejectUnauthorized: false } };

  const kv = {};
  for (const part of s.split(";")) {
    const i = part.indexOf("=");
    if (i > 0) kv[part.slice(0, i).trim().toLowerCase()] = part.slice(i + 1).trim();
  }
  const sslMode = kv["ssl mode"] ?? kv["sslmode"] ?? "";
  return {
    host: kv["host"],
    port: Number(kv["port"] || 5432),
    database: kv["database"] || "postgres",
    user: kv["username"] ?? kv["user id"] ?? kv["user"],
    password: kv["password"],
    // Supabase 는 자체 인증서를 쓴다. 검증을 끄지 않으면 연결이 거부된다.
    ssl: /require|prefer|true|verify/i.test(sslMode) ? { rejectUnauthorized: false } : undefined,
    connectionTimeoutMillis: 8000,
  };
}
