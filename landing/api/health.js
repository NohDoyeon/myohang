// 설정 점검용. `https://<사이트>/api/health` 로 열어 본다.
//
// **값은 절대 돌려주지 않는다** — 있는지(present)와 길이(length)만 알려 준다.
// 길이를 같이 보는 이유: 따옴표째 붙여넣거나 앞뒤 공백이 섞이면 길이로 티가 난다.
// (예: 64자여야 할 서명 키가 66자면 따옴표가 같이 들어간 것이다.)

const KEYS = ["HARBOR_DB", "HARBOR_TICKET_SECRET", "HARBOR_GAME_HOST", "HARBOR_GAME_PORT"];

export default function handler(req, res) {
  const seen = {};
  for (const k of KEYS) {
    const v = process.env[k];
    seen[k] = v ? { present: true, length: v.length, trimmedSame: v === v.trim() } : { present: false };
  }
  res.status(200).json({
    ok: KEYS.every((k) => seen[k].present || k === "HARBOR_GAME_PORT"),   // PORT 는 기본값이 있어 없어도 된다
    env: seen,
    // 이름이 비슷한 변수가 있으면 오타를 찾는 데 도움이 된다(값은 안 보여 준다).
    similar: Object.keys(process.env).filter((k) => k.toUpperCase().includes("HARBOR")),
    region: process.env.VERCEL_REGION ?? null,
    deployedAt: process.env.VERCEL_DEPLOYMENT_ID ? "vercel" : "local",
  });
}
