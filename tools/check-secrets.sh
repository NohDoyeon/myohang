#!/usr/bin/env bash
# 커밋에 비밀정보가 섞였는지 검사한다. 걸리면 exit 1.
#
#   bash tools/check-secrets.sh          # 스테이징된 변경만 (커밋 직전)
#   bash tools/check-secrets.sh --all    # 추적 중인 파일 전체 (저장소를 공개로 돌리기 전)
#
# pre-commit 훅으로 걸어 두려면:
#   printf '#!/bin/sh\nexec bash tools/check-secrets.sh\n' > .git/hooks/pre-commit && chmod +x .git/hooks/pre-commit
#
# 완벽한 탐지가 아니다 — 마지막 방어선일 뿐, 애초에 값을 파일에 적지 않는 것이 원칙이다.
set -uo pipefail

MODE="${1:-staged}"
if [ "$MODE" = "--all" ]; then
  FILES=$(git ls-files)
else
  FILES=$(git diff --cached --name-only --diff-filter=ACM)
fi
[ -z "$FILES" ] && { echo "[secrets] 검사할 파일 없음"; exit 0; }

# 값이 들어 있으면 안 되는 흔적들
PATTERNS='postgres(ql)?://[^[:space:]"]+:[^[:space:]"@]+@'   # 비밀번호가 박힌 접속 URL
PATTERNS="$PATTERNS|(Password|Pwd)[[:space:]]*=[[:space:]]*[^;[:space:]\"']{4,}"
# service_role 은 문서에서 "노출 금지"를 설명할 때도 쓰이는 단어다 → **값이 붙어 있을 때만** 잡는다.
PATTERNS="$PATTERNS|(service_role|SERVICE_ROLE)[A-Za-z_]*[[:space:]]*[:=][[:space:]]*[\"']?[A-Za-z0-9._-]{20,}"
PATTERNS="$PATTERNS|eyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}"  # JWT (supabase anon/service key) — 실제 키는 이 형태다
PATTERNS="$PATTERNS|-----BEGIN [A-Z ]*PRIVATE KEY-----"
PATTERNS="$PATTERNS|gh[pousr]_[A-Za-z0-9]{20,}"                  # GitHub 토큰
PATTERNS="$PATTERNS|AKIA[0-9A-Z]{16}"                            # AWS 액세스 키

bad=0
for f in $FILES; do
  [ -f "$f" ] || continue
  case "$f" in
    .env.example|tools/check-secrets.sh) continue ;;   # 견본과 이 스크립트 자신은 제외
    *.png|*.jpg|*.jpeg|*.webp|*.pck|*.dll|*.exe) continue ;;
  esac
  if hits=$(grep -nEI "$PATTERNS" "$f" 2>/dev/null); then
    echo "[secrets] ⚠ $f"
    echo "$hits" | sed 's/^/    /' | cut -c1-160
    bad=1
  fi
done

# 추적되면 안 되는 파일이 목록에 올라왔는지
for f in $FILES; do
  case "$f" in
    .env|.env.*|secrets/*|*.pem|*.key|*.pfx|*.db|saves/*)
      [ "$f" = ".env.example" ] && continue
      echo "[secrets] ⚠ 추적 대상이 아닌 파일이 포함됨: $f"; bad=1 ;;
  esac
done

if [ "$bad" -ne 0 ]; then
  echo
  echo "[secrets] 커밋을 멈췄습니다. 값을 .env 로 옮기고 소스에서는 환경변수로 읽으세요."
  echo "          이미 올라간 적이 있다면 그 키는 **폐기하고 새로 발급**해야 합니다(히스토리에 남습니다)."
  exit 1
fi
echo "[secrets] 이상 없음 ($(echo "$FILES" | wc -l) 파일 검사)"
