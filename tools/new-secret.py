"""서명 키를 만들어 `.env` 에 바로 써 넣는다. **값은 화면에 찍지 않는다.**

    py tools/new-secret.py                    # HARBOR_TICKET_SECRET 생성/교체
    py tools/new-secret.py OTHER_NAME         # 다른 변수 이름으로

화면에 찍으면 터미널 기록·스크롤백·캡처·대화 로그에 남는다. 키는 만든 자리에서 바로 파일로 들어가야 한다.
Vercel 같은 다른 곳에 넣어야 할 때만 `.env` 를 열어 그 줄을 복사한다.
"""
import re, sys, pathlib, secrets

ROOT = pathlib.Path(__file__).resolve().parent.parent
NAME = sys.argv[1] if len(sys.argv) > 1 else "HARBOR_TICKET_SECRET"

env = ROOT / ".env"
if not env.exists():
    print(f"{env} 가 없습니다. .env.example 을 복사해 두세요.")
    raise SystemExit(1)

value = secrets.token_hex(32)
text = env.read_text(encoding="utf-8")
line = f"{NAME}='{value}'"          # 따옴표로 감싼다 — 셸이 값을 끊지 않게

if re.search(rf"^{NAME}=", text, re.MULTILINE):
    text = re.sub(rf"^{NAME}=.*$", line, text, count=1, flags=re.MULTILINE)
    how = "교체"
else:
    if not text.endswith("\n"):
        text += "\n"
    text += line + "\n"
    how = "추가"

env.write_text(text, encoding="utf-8")
print(f"{NAME} 을 .env 에 {how}했습니다 ({len(value)}자). 값은 출력하지 않습니다.")
print("Vercel 에 넣을 때만 .env 를 열어 그 줄을 복사하세요.")
