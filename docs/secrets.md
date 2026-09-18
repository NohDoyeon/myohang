# 비밀정보 관리 (저장소가 public 인 전제)

> 전제가 하나 바뀌면 기준이 전부 바뀐다: **이 저장소는 공개다.**
> 한 번 커밋된 값은 저장소를 비공개로 돌리거나 파일을 지워도 **히스토리와 포크, 검색 캐시에 남는다.**
> 그래서 "지우면 된다"가 아니라 **"애초에 들어가지 않게 한다"** 가 유일한 방어다.

## 원칙 네 가지

1. **값은 파일에 적지 않는다.** 소스·appsettings·문서·README 어디에도. 코드는 환경변수만 읽는다.
2. **`.env` 는 추적하지 않는다.** 커밋되는 건 값이 빈 `.env.example` 뿐이다.
3. **명령줄에 적지 않는다.** 인자로 주면 셸 히스토리와 프로세스 목록(`tasklist`, `ps`)에 남는다 → `tools/run-server.sh` 를 쓴다.
4. **유출되면 지우지 말고 폐기한다.** Supabase 대시보드에서 비밀번호를 새로 발급하는 것이 유일한 복구다.

## 이 저장소의 장치

| 장치 | 하는 일 |
|---|---|
| `.gitignore` | `.env`, `secrets/`, `*.pem`·`*.key`·`*.pfx`, `appsettings.*.Local.json`, `saves/`, `*.db` 제외 |
| `.env.example` | 필요한 변수 이름만 담은 견본 (값 없음). 이것만 커밋된다 |
| `tools/run-server.sh` | `.env` 를 읽어 환경변수로 넘겨 서버 실행 — 명령줄에 값이 남지 않는다 |
| `tools/check-secrets.sh` | 커밋 전에 접속 URL·JWT·개인키·토큰 패턴을 검사. pre-commit 훅으로 걸 수 있다 |
| `Program.cs` | `HARBOR_DB` 가 없으면 **기동하지 않고** 안내하고 끝낸다(빈 값으로 몰래 돌지 않게) |

pre-commit 훅 설치(한 번만):

```bash
printf '#!/bin/sh\nexec bash tools/check-secrets.sh\n' > .git/hooks/pre-commit && chmod +x .git/hooks/pre-commit
```

> 훅은 `.git/` 안에 있어 커밋되지 않는다. 팀원이 늘면 각자 한 번씩 걸어야 한다.

## 변수 목록

| 변수 | 어디서 쓰나 | 성격 |
|---|---|---|
| `HARBOR_DB` | 게임 서버 → Postgres | **최고 등급.** DB 전체 권한 |
| `HARBOR_TICKET_SECRET` | 웹(발급) · 게임 서버(검증) | 서명 키. 새면 남의 계정으로 입장권을 만들 수 있다 |

## Supabase 프로젝트 설정 (생성 시 고른 값)

| 옵션 | 설정 | 이유 |
|---|---|---|
| Enable Data API | **끔** | 게임 서버도 웹도 Postgres 에 **직접 접속**한다. REST API 를 쓰지 않으므로, 공개값인 `anon` 키로 들어올 경로 자체를 없앤다 |
| Enable automatic RLS | (Data API 를 끄면 **선택 불가** — 정상) | 그 트리거는 Data API 로 노출되는 테이블을 보호하는 장치다. 노출이 없으면 보호할 대상도 없다 |

- Data API 를 꺼도 대시보드의 Table Editor·SQL Editor 는 그대로 쓸 수 있다.
- 나중에 `supabase-js` 가 필요해지면 Settings → API 에서 켜되, **"Automatically expose new tables" 는 끈 채로** 두고
  필요한 테이블에만 정책을 붙인다.
- `Db.Migrate` 가 모든 테이블에 `ENABLE ROW LEVEL SECURITY` 를 거는 것과 **이중 방어**다.

## Supabase 를 쓸 때 특히 주의할 것

- **`service_role` 키는 절대 브라우저로 내려보내지 않는다.** RLS 를 통째로 무시하는 키다.
  Next.js 에서는 서버 컴포넌트/API Route(서버 측)에서만 쓰고, 클라이언트에는 `anon` 키만 둔다.
- Vercel 환경변수에서 **`NEXT_PUBLIC_` 접두사는 브라우저에 그대로 노출된다.** 비밀 값에 이 접두사를 붙이지 않는다.
- 게임 서버는 DB 에 직접 붙으므로 Supabase 의 API 키가 아니라 **Postgres 접속 문자열**만 필요하다.
- 나중에 웹과 게임 서버가 분리되면, 각자 **권한이 다른 DB 사용자**를 만드는 것이 낫다
  (웹은 `account`·`notice`·`login_log` 만, 게임은 게임 테이블만).

## 사고가 났을 때

1. Supabase 대시보드에서 **DB 비밀번호 재설정** → 새 값으로 `.env` 갱신 → 서버 재시작
2. `HARBOR_TICKET_SECRET` 은 새로 생성(발급된 입장권이 전부 무효가 되지만 5분짜리라 영향이 작다)
3. 유출 경로를 확인: `git log -p --all -S '<유출된 조각>'`
4. 히스토리에서 지우는 것(`git filter-repo`)은 **보조 조치일 뿐**이다. 이미 공개됐다면 키 폐기가 본 조치다.
