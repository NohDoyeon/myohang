#!/usr/bin/env bash
# 공개 실행 — 터널과 게임 서버를 **함께** 띄우고, 바뀐 터널 주소를 서버가 웹에 알리게 한다.
#
#   bash tools/run-public.sh
#
# 왜 이게 있나:
#   무료 `trycloudflare.com` 주소는 터널을 열 때마다 바뀐다. 예전에는 그 주소를 사람이 읽어
#   Vercel 의 `NEXT_PUBLIC_GAME_WS` 에 붙여넣고 **재배포**해야 했다. 그 사이엔 아무도 못 들어온다.
#   이제는 이 스크립트가 주소를 읽어 서버에 넘기고(HARBOR_PUBLIC_WS), 서버가 DB(`server_endpoint`)에
#   적고, 웹의 `/api/endpoint` 가 그걸 읽어 준다. **손으로 나를 일이 없다.**
#
# 둘 중 하나가 죽으면 양쪽 다 내리고 처음부터 다시 띄운다(주소가 바뀌어도 스스로 따라간다).
# 멈추려면 Ctrl+C.
#
# ⚠ 이 터널은 :8080 **전체**를 연다 — `/admin` 도 같이 공개된다(토큰이 있어야 통과한다).
#   테스트가 끝나면 끄는 편이 안전하다.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_DIR="$ROOT/.run"
TUNNEL_LOG="$RUN_DIR/cloudflared.log"
mkdir -p "$RUN_DIR"

ENV_FILE="$ROOT/.env"
if [ ! -f "$ENV_FILE" ]; then
  echo "[public] $ENV_FILE 가 없습니다. cp .env.example .env 로 만들고 값을 채우세요."
  exit 1
fi
# 값이 화면에 찍히지 않게 조용히 읽는다 (run-server.sh 와 같은 방식).
set -a
# shellcheck disable=SC1090
. "$ENV_FILE"
set +a

if [ -z "${HARBOR_DB:-}" ]; then
  echo "[public] .env 에 HARBOR_DB 가 비어 있습니다."
  exit 1
fi

WEB_PORT="${HARBOR_WEB_PORT:-8080}"     # appsettings.json 의 Server:WebPort 와 같아야 한다
CF="${CLOUDFLARED:-/c/Program Files (x86)/cloudflared/cloudflared.exe}"
BIN="$ROOT/src/Harbor.Server/bin/Debug/net8.0"

if [ ! -x "$CF" ]; then
  echo "[public] cloudflared 를 찾지 못했습니다: $CF"
  echo "         다른 곳에 있으면 CLOUDFLARED=/c/경로/cloudflared.exe bash tools/run-public.sh"
  exit 1
fi
if [ ! -f "$BIN/Harbor.Server.exe" ]; then
  echo "[public] 빌드가 없습니다: dotnet build Harbor.sln -c Debug"
  exit 1
fi

tunnel_pid=""
server_pid=""

stop_both() {
  [ -n "$server_pid" ] && kill "$server_pid" 2>/dev/null
  [ -n "$tunnel_pid" ] && kill "$tunnel_pid" 2>/dev/null
  wait 2>/dev/null
  server_pid=""
  tunnel_pid=""
  return 0
}

on_exit() {
  echo ""
  echo "[public] 내려갑니다…"
  stop_both
}
trap 'on_exit; exit 0' INT TERM

while true; do
  : > "$TUNNEL_LOG"
  "$CF" tunnel --url "http://localhost:$WEB_PORT" --no-autoupdate >"$TUNNEL_LOG" 2>&1 &
  tunnel_pid=$!

  # 주소는 cloudflared 가 로그로 뱉는다. 최대 40초 기다린다.
  url=""
  for _ in $(seq 1 80); do
    url="$(grep -o 'https://[a-z0-9-]*\.trycloudflare\.com' "$TUNNEL_LOG" | head -1)"
    [ -n "$url" ] && break
    kill -0 "$tunnel_pid" 2>/dev/null || break
    sleep 0.5
  done

  if [ -z "$url" ]; then
    echo "[public] 터널 주소를 받지 못했습니다. 마지막 몇 줄:"
    tail -5 "$TUNNEL_LOG"
    stop_both
    sleep 5
    continue
  fi

  # **여기가 요점** — 주소를 환경변수로 서버에 넘긴다. 서버가 DB 에 적고 웹이 읽어 간다.
  export HARBOR_PUBLIC_WS="wss://${url#https://}/ws"
  echo "[public] 터널   $url"
  echo "[public] 게임   $HARBOR_PUBLIC_WS"
  echo "[public] 들어오는 곳: https://myohang.vercel.app  (Vercel 재배포 필요 없음)"

  ( cd "$BIN" && exec ./Harbor.Server.exe ) &
  server_pid=$!

  # 둘 다 살아 있는 동안 지켜본다. 하나라도 죽으면 같이 내리고 다시 띄운다
  # (터널만 죽으면 주소가 바뀌므로 서버도 새 주소로 다시 알려야 한다).
  while kill -0 "$tunnel_pid" 2>/dev/null && kill -0 "$server_pid" 2>/dev/null; do
    sleep 5
  done

  echo "[public] 한쪽이 멈췄습니다 — 5초 뒤 다시 시작합니다."
  stop_both
  sleep 5
done
