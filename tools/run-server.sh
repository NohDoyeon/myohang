#!/usr/bin/env bash
# 서버 실행 — .env 를 읽어 환경변수로 넘긴다.
#
# 접속 문자열을 **명령줄 인자로 주지 않는 것**이 요점이다.
# 인자로 주면 셸 히스토리(~/.bash_history)와 프로세스 목록(tasklist/ps)에 그대로 남는다.
#
#   bash tools/run-server.sh            # 서버 실행
#   bash tools/run-server.sh --import-sqlite=saves/harbor.db
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$ROOT/.env"

if [ ! -f "$ENV_FILE" ]; then
  echo "[run] $ENV_FILE 가 없습니다. .env.example 을 복사해 값을 채우세요:"
  echo "      cp .env.example .env"
  exit 1
fi

# set -a: 이 블록에서 정의되는 변수를 자동으로 export. 값이 화면에 찍히지 않게 조용히 읽는다.
set -a
# shellcheck disable=SC1090
. "$ENV_FILE"
set +a

if [ -z "${HARBOR_DB:-}" ]; then
  echo "[run] .env 에 HARBOR_DB 가 비어 있습니다."
  exit 1
fi

BIN="$ROOT/src/Harbor.Server/bin/Debug/net8.0"
if [ ! -f "$BIN/Harbor.Server.exe" ]; then
  echo "[run] 빌드가 없습니다: dotnet build Harbor.sln -c Debug"
  exit 1
fi

cd "$BIN"
exec ./Harbor.Server.exe "$@"
