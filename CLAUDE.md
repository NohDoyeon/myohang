# 묘항 (Myohang) — 작업 규칙

> 이 파일은 세션을 열 때마다 먼저 읽힌다. **짧게 유지한다.** 상세는 링크된 문서로 넘긴다.

## ⚠ 먼저 읽을 것 — Supabase · 비밀정보 (이 저장소는 **public**)

1. **`service_role` 키는 브라우저로 절대 내려보내지 않는다.** RLS 를 통째로 무시하는 키다.
   Next.js 에서는 서버 컴포넌트·API Route(서버 측)에서만 쓰고, 클라이언트에는 `anon` 키만 둔다.
2. **Vercel 환경변수에 `NEXT_PUBLIC_` 접두사를 비밀 값에 붙이지 않는다.** 그대로 번들에 박혀 공개된다.
3. **게임 서버는 API 키가 아니라 Postgres 접속 문자열만 필요하다.** 상주 프로세스이므로 연결은 이 순서로 고른다:
   - ① **직접 연결** `db.<ref>.supabase.co:5432` — 가장 단순. 다만 **IPv6 전용이라 국내 IPv4 회선에서는 붙지 않을 수 있다.**
   - ② 안 되면 **Session pooler** `aws-0-<region>.pooler.supabase.com:5432` (Username 이 `postgres.<ref>` 형태) — IPv4 이고
     세션 모드라 상주 프로세스에 적합하다. **여기를 기본값으로 생각하는 편이 안전하다.**
   - ③ **Transaction pooler(6543)는 서버리스(Vercel)용.** Npgsql 의 prepared statement 와 충돌하므로 쓰려면
     `Max Auto Prepare=0;No Reset On Close=true` 를 붙인다. 게임 서버에는 권하지 않는다.

4. **`public` 스키마의 테이블은 Supabase 가 REST API 로 자동 노출한다.** `anon` 키는 공개 값이므로,
   RLS 를 켜지 않으면 바깥에서 계정·지갑 테이블을 읽고 쓸 수 있다. `Db.Migrate` 가 모든 테이블에
   `ENABLE ROW LEVEL SECURITY` 를 걸어 둔다(정책 없음 = 외부 전면 거부). 서버는 소유자로 붙어 영향 없음.
   **새 테이블을 추가하면 그 목록에도 반드시 넣는다.**

그리고 항상:

- 비밀 값은 **`.env` 에만**. 소스·`appsettings.json`·문서·커밋 메시지·채팅 어디에도 적지 않는다. 커밋되는 건 값이 빈 `.env.example` 뿐.
- **명령줄에 넣지 않는다** (셸 히스토리·프로세스 목록에 남는다) → 서버는 `bash tools/run-server.sh` 로 띄운다.
- **화면에 찍지 않는다.** 키를 만들 때도 출력하지 말고 파일로 바로 쓴다(`py tools/new-secret.py`) — 터미널 기록·스크롤백·대화 로그에 남는다.
  `HARBOR_TICKET_SECRET` 은 DB 문자열과 같은 급이다: 이 키를 아는 사람은 **아무 닉으로나 입장권을 만들어** 남의 계정으로 들어올 수 있다.
- 커밋 전 `bash tools/check-secrets.sh` (pre-commit 훅 권장). 상세: **`docs/secrets.md`**
- 첫 커밋 때 검증 로그(`*.log`)가 통째로 딸려 들어갈 뻔했다 → `.gitignore` 는 **확장자로** 막는다. 커밋 목록은 눈으로 한 번 훑을 것.
- 한 번이라도 커밋된 키는 지워도 히스토리에 남는다 → **폐기하고 새로 발급**하는 것이 유일한 복구다.

## 이름 규칙 (2026-09-18 확정)

**두 층으로 쓴다. 섞지 않는다.**

- **Harbor = 코드명** — 네임스페이스(`Harbor.Core/Protocol/Server`), 실행 파일, `harbor://` 프로토콜,
  환경변수 접두사(`HARBOR_DB`, `HARBOR_TICKET`, `HARBOR_HOST`, …), 솔루션·폴더 이름.
- **묘항 / Myohang = 제품명** — 랜딩 페이지, 게임 창 제목, 저장소(`NohDoyeon/myohang`), 테스터가 받는 클라 exe.

새 환경변수·클래스는 `HARBOR_`/`Harbor.` 를 따른다. 제품명으로 바꾸고 싶다면 **전부 함께** 바꾼다(부분 개명 금지).

## 이 환경에서의 실행

- 이 세션에는 **Bash 도구가 없다.** 명령은 사용자가 `! <cmd>` 로 실행하고, 그건 **Git Bash** 로 돈다(PowerShell 아님).
- **`!` 셸은 작업 디렉터리를 이어받는다** → 명령은 항상 `cd /c/Users/User/Desktop/harbor && …` 로 절대경로에서 시작한다.
- 파이썬은 `python` 이 아니라 **`py`**. 한글 출력이 깨지면 `PYTHONIOENCODING=utf-8`.
- **서버가 떠 있으면 DLL 이 잠겨 빌드가 실패한다** → `taskkill //F //IM Harbor.Server.exe` 먼저. (사용자가 띄워 둔 창도 같이 죽으니 알리고 할 것)

빌드·테스트 한 줄:

```
! cd /c/Users/User/Desktop/harbor && taskkill //F //IM Harbor.Server.exe 2>/dev/null; dotnet build Harbor.sln -c Debug 2>&1 | grep -E "error|오류 [0-9]+개"; dotnet build client-godot/HarborClient.csproj 2>&1 | grep -E "error|warning|오류 [0-9]+개"; dotnet test Harbor.sln --no-build 2>&1 | grep -E "통과|실패"
```

## 아키텍처 불변식 (어기면 조용히 깨진다)

- **스키마에 컬럼을 추가하면 `Db.AddMissingColumns` 에도 한 줄 넣는다.** `CREATE TABLE IF NOT EXISTS` 는 이미 있는
  테이블에 컬럼을 더해 주지 않아서, **새 DB 에서는 멀쩡하고 기존 DB 에서만 터진다**(서버가 기동 중 죽는다).
- **데이터 소유권**: `account`·`notice`·`login_log` = **웹**, `player`·`inventory`·`room`·`room_item`·`currency_log` = **게임 서버**.
  가입은 웹에서만(테스트 중에는 `Server:AllowGameSignup` 으로 열어 둠 — 공개 전 false).
- **방 저장 키는 `u:{닉}`** 으로 템플릿과 무관하다. 넓히기·이사에서 **옛 인스턴스를 먼저 닫지 않으면** 새 방 기록을 덮어쓴다.
- 상점 카탈로그는 `price.amount > 0` 만 내보낸다(0 = 수확물 = 파는 물건).
- `[MessagePackObject]` 타입에 **한 줄 다중 필드 선언 금지**(같은 `[Key]` 공유 → 직렬화 예외).
- **텍스트 입력에 `LineEdit`/`TextEdit` 금지** — 이 환경에서 한글 IME 조합이 깨진다. `HangulInput` 을 쓴다.
- 그림을 넣거나 바꾼 뒤에는 **`tools/import-assets.ps1`** 을 한 번 돌려야 Godot 이 읽는다.
- **아틀라스 프레임 표는 두 곳에 있다** — `art/atlas.json`(에디터용)과 `art/AtlasMeta.cs`(배포용 C# 상수).
  `tools/build-avatar-atlas.py` 가 **둘 다** 갱신한다. `.json` 은 Godot 리소스가 아니라 pck 에 들어간다고 믿을 수 없고,
  빠져도 크래시가 아니라 **고양이가 네모로 나오는 조용한 실패**다.
- **클라 배포는 `bash tools/pack-client.sh`** — 에디터에서 손으로 내보내지 않는다(내보내기 폴더 미생성·필터 누락으로 매번 같은 곳에서 막힌다).
  Godot 의 .NET 내보내기는 `client-godot/HarborClient.sln` 을 요구하고, 그 안의 **`ExportRelease` → `Release` 매핑**이 없으면 참조 프로젝트가 깨진다.
- **자동 검사는 통과·실패를 둘 다 눈으로 본 뒤에 넣는다.** (.NET 문자열 상수는 **UTF-16** 이라 그냥 `grep` 하면 못 찾는다 → `tr -d '\000'`)
- `S_InventoryUpdate.Qty` 는 델타가 아니라 **현재 보유 수량(절대값)**.
- 방 템플릿은 규칙이 많다 → **`docs/room-template-spec.md`**. 넓히기 포함관계는 `RoomUpgradeTests` 가 지킨다.
- 클라이언트는 **웹으로 내보낼 수 없다**(Godot .NET 빌드 제약). 웹 플레이는 GDScript 재작성 + WebSocket 이 필요하다.
  → **서버·데이터에 넣은 것은 남고, 클라 UI 에 쌓은 것은 재작성 대상**이다.

## 문서 지도

| 파일 | 언제 |
|---|---|
| `NEXT.md` | **이어서 할 때 가장 먼저** — 현재 상태·실행 명령·눈 확인 목록 |
| `WORKLOG.md` | 왜 그렇게 했는지, 무엇에 걸렸는지 (라운드별) |
| `docs/secrets.md` | 키·환경변수 관리 |
| `docs/deploy-mvp.md` | GitHub·클라 내보내기·서버 공개·백업 |
| `docs/platform-plan.md` | 관리자/팀 권한·공지·콘텐츠 반영 경로·접속 로그 |
| `docs/room-template-spec.md` | 방 만들 때 (다른 AI 에 줄 프롬프트 포함) |
| `art/ref/sprite-prompt.md` | 도트 의뢰·아틀라스 투입 절차 |
