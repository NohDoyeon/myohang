# ▶ 여기부터 읽기 (이어서 하기)

> 터미널을 새로 열면 이 파일부터 읽는다. 상세 이력은 `WORKLOG.md`.
> 위치: `C:\Users\User\Desktop\harbor` (Harbor.sln 있는 폴더). 제품명 **팝플(Popple)**, 코드명 Harbor.

## 지금 상태 — 13차까지 (2026-09-18). 출석·팔기·방 넓히기·이사 — **빌드/테스트 통과 + 눈 확인 완료**

> 2026-09-18: Supabase Postgres 위에서 실제 플레이로 정상 동작 확인. 다음은 **배포**(클라 내보내기 → 서버 외부 공개 → 다른 PC 접속).

### 한눈에
- **묘항(Myohang)** = 고양이 묘 + 항구 항. 두 발로 다니는 고양이들이 사는 항구 마을.
- 웹(:8080) 랜딩 → 로그인 → 일회용 입장권 → 게임(:30000) 자동 실행까지 이어짐.
- 저장은 **PostgreSQL(Supabase)**. `account`(닉·비번·role)=웹 소유, `player`·`inventory`·`room`·`room_item`·`currency_log`=게임 서버 소유.
  접속 문자열은 `.env` 의 `HARBOR_DB` 에만 있고, 서버는 `bash tools/run-server.sh` 로 띄운다. 옛 `saves/harbor.db` 는 이관 완료(2026-09-18).
- 아바타에 **첫 도트 아트** 들어감(정면 서기 1장, 53×69, 14색). 나머지 방향·동작은 그 한 장으로 대체 중.
- 13차: **연속 출석 보상**(7일차 상한) · **캣닢 잎 팔기**(30장 묶음이 이득) · **방 넓히기**(다락방 → 넓은 → 큰).

### ⚠ 먼저 할 일 — 13차 눈 확인
빌드·테스트는 2026-09-18 에 통과했다(sln 0 / 클라 0,0 / 테스트 132). 다시 확인하려면:
```
! taskkill //F //IM Harbor.Server.exe 2>/dev/null; dotnet build Harbor.sln -c Debug 2>&1 | grep -E "error|오류 [0-9]+개"; dotnet build client-godot/HarborClient.csproj 2>&1 | grep -E "error|warning|오류 [0-9]+개"; dotnet test Harbor.sln --no-build 2>&1 | grep -E "통과|실패"
```
남은 것은 실제로 띄워서 보는 것:
- [ ] 로그인 안내가 "**N일 연속! 출석 보상 …루피**" 로 뜨는지. 같은 날 다시 들어오면 "이미 하셨어요".
- [ ] 캣닢 화분(가방에 2개)을 놓고 활짝 필 때까지 두었다가 클릭 → **캣닢 잎**이 가방에. [가방]에 `1개 40` / `전부 팔기` 버튼.
- [ ] [방 목록] 맨 위 **내 방 넓히기 → 넓은 다락방 800루피** → 누르면 방이 커지고 **가구가 제자리에 그대로** 있는지.
      (다락방은 이제 5단계: 800 → 2,500 → 8,000 → 20,000. 루피를 넣어 4·5단계까지 올려 보면 화면에 다 들어오는지도 같이 확인됨)
- [ ] 넓힌 뒤에도 내 방으로 인식되는지(상단이 "내 방", 가구를 계속 놓을 수 있는지).
- [ ] [방 목록] **이사** — 모퉁이집(ㄱ자)/복층방으로 1,000루피에 갈아타지는지. **놓아 둔 가구가 전부 가방으로** 돌아오는지,
      넓혀 둔 단계가 유지되는지(3단계에서 이사하면 상대 계열도 3단계), 서버를 껐다 켜도 새 집이 남는지.
- [ ] 서버 기동 로그에 `⚠` 경고가 없는지(방 계열/단계 사슬 점검).

### 👀 눈 확인 — 14차 선물 화분 (**혼자서는 확인 불가**, 클라 2개 필요)
- [ ] 상점에서 **선물 화분**(120루피) 구매 → 내 방에 배치 → `빈 화분`
- [ ] 내 화분을 클릭 → *"내 화분에는 못 꽂아요"* 안내 (혼자 채우기 차단)
- [ ] 다른 닉으로 접속해 **그 방에 놀러 가서** 화분 클릭 → 캣닢이 꽂히고 개수/단계가 오르는지
- [ ] 같은 사람에게 **4개째** 꽂으면 *"오늘은 이 분께 더 못 드려요"*
- [ ] 주인 닉 옆에 **🌿 인기도**가 붙고, 방을 나갔다 들어와도 유지되는지
- [ ] 30개를 채우면 반짝임 + 주인이 클릭 → **1,500루피**, 화분은 비워지고 **인기도는 그대로**
- [ ] Supabase 에서 `SELECT * FROM gift_log ORDER BY id DESC LIMIT 5;` 로 기록이 쌓였는지
- [ ] 상점 목록에 **캣닢 잎이 안 보이는지**(0루피 구매 구멍을 막았음).

### 나중에 할 것 — 조작 편의 (2026-09-18 요청)
- **키보드 이동**: WASD / 화살표로 걷기. 지금은 좌클릭 이동만이다.
  구현은 크지 않다 — 누른 방향의 **인접 타일로 `C_Move` 를 보내면** 기존 경로·충돌 판정을 그대로 탄다
  (새 프로토콜 없이 된다). 누르고 있는 동안 반복 전송하되 틱(60ms)보다 자주 보내지 않게 막을 것.
- **포털로 이동**: 특정 타일/가구를 밟거나 누르면 다른 방으로. 문(`DoorMarker`)이 이미 방 목록을 여는 구조라,
  "밟으면 바로 이동"하는 포털은 가구 `interaction: "portal"` + 대상 방 id 로 만들면 된다.
- **단축키 안내 버튼**: HUD 에 `?` 버튼 → 조작·단축키를 설명하는 창. 지금은 상단에 한 줄 힌트만 있다.
  키보드 이동을 넣으면 안내가 반드시 같이 있어야 한다.

### 그 다음 할 일 (우선순위)
1. **캐릭터 프레임 추가.** 지금 어느 방향으로 걸어도 정면 그림 하나다. **걷기 2장**이 체감을 가장 크게 바꾼다.
   프롬프트는 `art/ref/sprite-prompt.md` 에 우선순위대로 정리돼 있다. 받은 파일은 `art/ref/` 에 넣기만 하면 된다.
   ⚠ 그림을 넣거나 바꾼 뒤에는 **반드시** `tools/import-assets.ps1` 을 한 번 실행해야 Godot 이 읽는다.
2. **낚시 미니게임** — 고양이 + 항구 + 생선. 잡은 생선은 `sell` 정의만 붙이면 그대로 팔린다(13차 판매 경로 재사용).
3. **펫 재검토** — 고양이가 주인공이라 펫이 또 고양이면 어색하다. 새·물고기 쪽으로.
4. 친구 목록, 배경음악, 미니공원(실외 개인 공간).

### 실행
저장소가 **PostgreSQL(Supabase)** 로 바뀌었다. 접속 문자열은 `.env` 의 `HARBOR_DB` 에만 있고,
서버는 그걸 읽어 주는 스크립트로 띄운다(명령줄에 키가 남지 않게):

```
! cd /c/Users/User/Desktop/harbor && bash tools/run-server.sh
```
```
! cd /c/Users/User/Desktop/harbor && bash tools/run-server.sh --import-sqlite=saves/harbor.db   # 옛 SQLite 이관(1회)
```
웹에서 가입·로그인 → [게임 시작] → 클라이언트 자동 실행. 입장권 코드에 **서버 주소가 함께** 실린다.
`HARBOR_DB` 가 없으면 서버는 기동하지 않고 안내만 하고 끝난다(빈 값으로 도는 사고 방지).

### 저장소 (2026-09-18~)
**https://github.com/NohDoyeon/myohang** (public). 원격은 `https://NohDoyeon@github.com/...` 형태로 박아 두었다 —
이 PC 에 회사 계정(`NohDoyeon241104`) 자격증명이 캐시돼 있어서, 계정명을 URL 에 넣지 않으면 403 이 난다.
커밋 정체성도 **이 저장소 안에서만** 개인 계정으로 설정돼 있다(`git config user.email`, `--global` 아님).

작업 후 루틴:
```
! cd /c/Users/User/Desktop/harbor && git add -A && bash tools/check-secrets.sh && git commit -m "무엇을 왜" && git push
```
pre-commit 훅이 한 번 더 검사한다. **커밋 목록에 `saves/`·`.env`·`*.log` 가 없는지 눈으로 확인할 것.**

### 배포 (10명 MVP 테스트)
**공개 소개 페이지: https://myohang.vercel.app** — 저장소의 `landing/` 폴더를 Vercel 이 서빙한다
(Framework `Other` · Root Directory `landing`). `main` 에 푸시하면 자동 재배포되므로 따로 할 일이 없다.
게임 서버가 서빙하는 `web/index.html` 과는 **다른 파일**이다(이유는 `landing/README.md`).

남은 것: 클라이언트 zip → GitHub Releases → 랜딩의 다운로드 버튼 링크 연결.
`docs/deploy-mvp.md` — GitHub 올리기 · 클라 exe 내보내기 · 서버 외부 공개 · 백업.
⚠ **Vercel 에는 게임 서버를 못 올린다**(상주 TCP·파일 SQLite·입장권이 같은 프로세스). 소개/다운로드 정적 페이지까지만.
⚠ 커밋 전에 `git status` 에 `saves/`·`*.db` 가 없는지 확인 — 계정 비밀번호 해시가 들어 있다.

### 도구
| 스크립트 | 언제 |
|---|---|
| `tools/import-assets.ps1` | **그림을 넣거나 바꿀 때마다** (안 돌리면 Godot 이 새 PNG 를 못 읽음) |
| `tools/register-url-scheme.ps1` | `harbor://` 등록 (1회, 완료됨) |
| `tools/unregister-url-scheme.ps1` | 위 해제 |

⚠ `.bat` 에 한글을 넣지 말 것 — cmd 가 CP949 로 읽어 줄이 깨지고 명령이 반으로 쪼개진다. 로직은 `.ps1` 에, `.bat` 은 껍데기로.

## (이전) 9차까지
8차 = 계정(비밀번호) · 웹 랜딩 페이지 · 로그인 → 일회용 입장권 → 게임.
9차 = 저장을 **SQLite 테이블**로 (account / inventory / room / room_item). 기존 JSON 자동 이관.
빌드 0/0, 테스트 58/58, DB 직접 조회로 값 확인, 서버 강제 종료 후 재기동에도 데이터 유지.

### 실행 (서버 + 웹 + 게임)
```
! cmd //c start "Harbor Server" //D "src\Harbor.Server\bin\Debug\net8.0" Harbor.Server.exe; sleep 3; cmd //c start "" http://localhost:8080
```
웹에서 가입·로그인 → [게임 시작]. 프로토콜 미등록이면 코드 복사 → 게임 시작 화면 비밀번호 칸에 붙여넣기.
`harbor://` 등록: `! cmd //c tools\register-url-scheme.bat`
게임만 바로 띄우기: 기존 명령 그대로(아래 "실행 방법").
⚠ 깨끗이 시작하려면 `src\Harbor.Server\bin\Debug\net8.0\saves\` 삭제(검증용 `olduser`/`dbuser` 들어 있음).

### 👀 눈 확인 체크리스트 (8·9차)
- [ ] `http://localhost:8080` 랜딩 페이지 — 접속자 수가 실시간으로 바뀌는지, 가입/로그인, 틀린 비밀번호 거부.
- [ ] [게임 시작] → 클라이언트가 열리고 **시작 화면 없이 바로 입장**하는지(프로토콜 등록했을 때).
- [ ] 코드 복사 → 게임 시작 화면 비밀번호 칸에 붙여넣기 → 닉을 안 쳐도 입장되는지.
- [ ] 방 꾸미고 서버 껐다 켜기 → 가구·루피·외모 그대로인지.
- [ ] 같은 닉으로 클라 두 개 → 두 번째가 "이미 접속 중" 으로 막히는지.

### 다음 후보
1. **`currency_log` 원장** — 루피가 오간 내역(누가·얼마·왜·잔액). 재화 사고는 기록 없이는 복구 불가.
2. **펫 + 사료**(팝플) / **식물 키우기**(퍼피레드) — 시간이 지나며 자라는 가구. FSM+타이머로 가능.
3. **친구 목록** — 친구 방으로 바로 가기.
4. 미니공원(실외 개인 공간), 배경음악, 가위바위보·낚시.

## (이전) 7차 작업
7차 = 외모 꾸미기(머리/피부/상의/하의/모자) · 댄스홀 공용 방 · 무한 콜라 자판기 · 방 템플릿 기본 가구.
**빌드 미검증** — 검증할 때 `Harbor.Server.exe` 가 떠 있으면 파일이 잠겨 빌드가 안 된다(`! taskkill //F //IM Harbor.Server.exe` 후 재시도).

### 👀 눈 확인 체크리스트 (7차)
- [ ] 하단 **[외모]** — 머리색/길이, 피부, 상의, 하의, 모자를 고르면 내 캐릭터가 바로 바뀌는지. 클라 두 개 띄워 **상대 화면에도 즉시 반영**되는지. 껐다 켜도 유지되는지.
- [ ] **[방 목록] → 댄스홀** — 가운데 한 단 높은 무대, 조명 2개, 벽시계·창문 2개, 콜라 자판기 2대가 처음부터 놓여 있는지.
- [ ] **콜라 자판기** 클릭 → 콜라 캔이 가방에 들어오고 "덜컹!" 배지 → 1.5초 뒤 복귀. 여러 번 눌러 무한인지.
- [ ] 댄스홀은 공용이라 아무나 가구를 놓고 주울 수 있는지. 자판기를 주워 가면 내 가방에 들어오는지(그 상태가 저장되는지).

## (이전) 6차 — 빌드 통과
6차 = 떠오르는 채팅 · 문 클릭으로 방 이동 · 복권과 일일 용돈. 빌드 0/0, 테스트 **24/24**, e2e 예외 0.
⚠ e2e 는 접속만 해서 **말풍선 트윈·복권 창은 런타임 미검증**. 아래 체크리스트로 눈 확인 필요.

### 👀 눈 확인 체크리스트 (6차)
- [ ] 채팅을 치면 말풍선이 **머리 위에서 하늘로 떠오르며** 사라지는지. 연속으로 여러 줄 치면 겹치지 않고 쌓이는지. 긴 문장(15자 이상)은 줄바꿈되는지.
- [ ] 방 안의 **문**이 보이는지(아치형, 하늘색 문짝). 마우스 올리면 반짝이고, 누르면 방 목록이 열리는지.
- [ ] **[복권]** 창 — 10루피 뽑기, 루피가 줄고 결과가 뜨는지, 당첨되면 루피가 늘고 1·2등은 토스트가 뜨는지. 루피가 10 미만이면 버튼이 비활성인지.
- [ ] 입장할 때 **용돈 안내**가 시스템 채팅+토스트로 뜨는지. 같은 날 다시 접속하면 "이미 받으셨어요"로 바뀌는지(날짜 기준이라 하루 1회).

## (이전) 5차 — 빌드·저장 e2e 전부 통과
빌드 0/0, 테스트 13/13, **저장 왕복 실증**(의자+기울어진 포스트잇+한글 메모가 서버 재시작 후 복원됨).
⚠ 검증이 `saves/` 에 `tester` 방을 하나 만들어 뒀다(셀프테스트가 놓은 의자 1개 + 포스트잇 1장). `tester` 로 들어가면 그게 보인다 — 저장이 되는 증거이기도 하다. 깨끗이 시작하려면 `src\Harbor.Server\bin\Debug\net8.0\saves\` 삭제.

### 다음 라운드 후보 (레퍼런스 글에서 뽑음 — `docs/reference-notes.md`)
1. **떠오르는 채팅** — 원작은 하단 채팅창이 아니라 친 말이 하늘로 붕붕 떠올랐다. 이 장르의 시각적 서명.
2. **문 클릭 = 방 이동** — 원작은 선실 문으로 남의 방에 갔다(메뉴가 아니라 공간 안 오브젝트). `RoomDto.DoorX/Y` 이미 옴.
3. **복권** — 한 장 10루피, 1등 1000루피. 지금 루피 **벌 곳이 없다**(시작 500뿐).

## (이전) 5차 작업 요약
사용자 확인 완료: 1차(시작 화면/HUD/배치/placeholder), 2차(벽·가방/상점/루피·방 목록), 4차 한글 채팅(`HangulInput` 로 IME 우회 — 잘 됨).
5차 내용: 벽 기울기 부호 버그 수정(포스트잇이 벽과 어긋나던 원인), R 로 기울기 주기, **JSON 저장**(지갑·가방·방 꾸밈이 서버 재시작에도 남음), 가구 16종, 모양별 렌더.

- .NET SDK: 9.0.301 (8.0.420 병존), `global.json` 없음 → net8.0 빌드 OK
- Godot: **4.7.2 .NET(mono)** — winget `GodotEngine.GodotEngine.Mono`
  - exe: `C:\Users\User\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe`
- 아트 에셋 없음 → placeholder 모드(그려진 캐릭터 + 아이소 박스 가구). `res://art/atlas.png`+`atlas.json` 넣으면 자동 교체.

## 실행 방법 (⚠ 이 세션 `!` = Git Bash, PowerShell 아님)
서버 + 게임 창 동시 실행 (시작 화면에서 닉네임 입력 → 입장하기):
```
! cmd //c start "Harbor Server" //D "src\Harbor.Server\bin\Debug\net8.0" Harbor.Server.exe; sleep 2; cmd //c start "" "C:\Users\User\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "C:\Users\User\Desktop\harbor\client-godot"
```
같은 명령을 한 번 더 치면 클라 2개 → 서로 보이는지/채팅 말풍선 확인 가능(닉네임만 다르게).
개발은 에디터로: 위 exe 뒤에 `--editor` 붙이면 됨 → F5.

빌드/테스트 한 방 확인:
```
! dotnet build Harbor.sln -c Debug 2>&1 | grep -E "error|오류 [0-9]+개"; dotnet build client-godot/HarborClient.csproj 2>&1 | grep -E "error|warning|오류 [0-9]+개"; dotnet test Harbor.sln --no-build 2>&1 | grep -E "통과|실패"
```
기대: 오류 0 / 오류 0 경고 0 / 9 통과.

헤드리스 e2e (시작 화면 건너뛰고 자동 입장 = `HARBOR_AUTOJOIN=1`):
```
! taskkill //F //IM Harbor.Server.exe 2>/dev/null; sleep 1; (cd src/Harbor.Server/bin/Debug/net8.0 && ./Harbor.Server.exe > "$OLDPWD/srv.log" 2>&1) & sleep 2; HARBOR_AUTOJOIN=1 "C:\Users\User\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --path "C:\Users\User\Desktop\harbor\client-godot" > godot_run.log 2>&1 & sleep 12; taskkill //F //IM Godot_v4.7.2-stable_mono_win64_console.exe //T 2>/dev/null; taskkill //F //IM Godot_v4.7.2-stable_mono_win64.exe //T 2>/dev/null; taskkill //F //IM Harbor.Server.exe 2>/dev/null; cat godot_run.log; echo ---; grep -v Hosting srv.log
```
정상 신호: 클라 로그에 `[AssetCatalog] atlas.json 없음 — placeholder 모드`, 서버에 `Enter failed` 없음.

## 👀 눈 확인 체크리스트 (5차)
- [ ] **벽 방향**: 포스트잇/액자/시계를 **왼쪽 벽과 오른쪽 벽 양쪽**에 붙여 보고 둘 다 벽면에 딱 붙는지(예전엔 한쪽이 어긋났음).
- [ ] **기울기**: 붙이기 전에 R 을 눌러 0/−7/7/−14/14° 로 바뀌는지, 삐뚜름하게 붙는지.
- [ ] **저장**: 방 꾸미고 → 서버 창 닫고 → 다시 서버+클라 실행 → 같은 닉으로 입장 → **가구·루피·가방 그대로**인지. (초기화하려면 `src\Harbor.Server\bin\Debug\net8.0\saves\` 폴더 삭제)
- [ ] **새 가구**: 상점에서 러그(밟고 지나가짐)·화분·스탠드 조명(켜면 빛)·TV(켜면 웃음)·책장·탁자·의자(등받이 방향), 벽 장식(창문·액자·시계) 사 보기.
- [ ] 다른 닉으로 하나 더 띄워 **남의 방 놀러 가서 포스트잇 남기기** → 원래 닉으로 돌아와 읽기.

## 👀 눈 확인 체크리스트 (2차, 완료분)
- [ ] 입장하면 **"{닉}의 방"**(기본 선실 템플릿)으로 들어가고, 뒤쪽 두 면에 나무색 벽(걸레받이·윗선)이 보이는지. 문 쪽엔 벽이 없어야 정상.
- [ ] 우상단 "루피 500". [가방]에 의자×2·콜라×2·냉장고×1. [배치] → 놓기 → 수량 줄고, 다 놓으면 배치 모드 자동 종료.
- [ ] [상점] 구매 → 루피 줄고 가방에 추가. 루피 부족하면 구매 버튼 비활성.
- [ ] 에어컨(벽걸이) 배치: 벽 없는 타일은 빨강, 벽 있는 타일은 초록, R 로 북쪽/서쪽 벽 전환, 놓이면 벽에 납작하게 붙는지. 클릭 → 켜짐.
- [ ] [방 목록] → 메인 갑판(공용) 입장 → 누구나 배치 가능. 다른 닉으로 클라 하나 더 띄워 그 사람 방에 들어가면 "구경만" 안내 + 배치/줍기 차단.
- [ ] 같은 닉으로 재접속하면 내 방 가구가 남아있는지(서버 재시작 전까지).
- [ ] **한글 채팅**: 채팅창 클릭(또는 Enter) → "안녕하세요" 가 정상 조합되는지, 겹받침(닭/값)·복모음(왜/의) 되는지, Backspace 가 자모 단위로 지워지는지, 오른쪽 Alt 로 영문 전환되는지. 닉네임은 영문 기본(배지 클릭하면 한글).
- [ ] **포스트잇 + 벽 슬롯**: 가방의 노란 포스트잇 [붙이기] → 마우스를 **벽면**에 올리면 슬롯 사각형이 초록/빨강 → 클릭 → 쓰기 창 자동(Enter = 붙이기) → 벽에 메모지+첫 글자. 한 벽 타일에 가로 2칸 × 세로 3칸까지 붙는지. 클릭하면 읽기 창. 다른 닉으로 들어와 남의 방에 붙여지는지, 주인이 뗄 수 있는지.
- [ ] **에어컨**도 벽 슬롯에 붙는지(위쪽 칸에 달아보기).
- 1차 체크(시작 화면·이동·감정표현·자동 다가가기·줌)는 사용자 확인 완료.
- 문제 있으면 `WORKLOG.md` 2026-09-17 항목의 파일 설명 보고 해당 스크립트 수정.

## 다음 할 일 (로드맵 MVP 순서)
- [ ] 위 눈 확인 → 어색한 부분 다듬기 (폰트 크기, 색, 말풍선 위치, 카메라 오프셋 등은 `Ui.cs`/`HudView.cs` 상수).
- [x] 포스트잇 벽 방명록 (3차, 2026-09-17) — 다듬을 것: 색 더 추가, 메모지에 본문 더 보이게, 붙인 시각 표시.
- [ ] **영속 계층 (PostgreSQL + Dapper)** — 지금 지갑/가방/개인 방은 서버 메모리(재시작 시 초기화, 닉 기준 재사용). 로그인 토큰검증도 없음.
- [ ] **아틀라스 파이프라인 + 팔레트 스왑 셰이더** — placeholder 를 실제 스프라이트로 (`AssetCatalog` 자동 교체, `AvatarView.ApplyFigure` 에 팔레트 메타 자리 있음).
- [ ] 루피 벌기: 복권(1일 1회)·미니게임 보상 → 지금은 시작 500 루피뿐.
- [ ] 복권(1일 1회)·가위바위보·낚시 미니게임
- [ ] 커플 룸·결혼

## 알아둘 함정
- `!` 명령은 **한 줄에 하나**, 맨 앞 글자가 `!` 여야 실행됨.
- 이 세션엔 Bash 도구가 없음 → 빌드/실행은 사용자가 `!` 로 하거나, 빌드 검증 에이전트에게 위임(2026-09-17 에 그렇게 검증함).
- `dotnet run --project`는 CWD가 프로젝트 폴더 → `Program.cs`에서 `AppContext.BaseDirectory` 기준으로 고정함(해결됨).
- 손으로 쓴 .tscn 노드 export는 C#에 자동 주입 안 됨 → 스크립트가 `??=`로 자체 폴백. **HUD 는 아예 코드로 조립**(`HudView.cs`), 새 UI 도 그 방식 권장.
- `[MessagePackObject]` 타입에 한 줄 다중 필드 선언 금지(키 중복). ItemDto 는 Key 0~11, RoomDto 0~11 사용 중.
- `S_InventoryUpdate.Qty` 는 **절대값**(현재 보유 수량). 델타 아님. 받을 때 `Interaction`·`Sell*` 을 **버리지 말 것**(버리면 포스트잇이 "붙이기"를 잃는다).
- 방 저장 키는 `u:{닉}` 이라 **템플릿과 무관**하다 → 방을 넓힐 때 옛 인스턴스를 먼저 닫지 않으면 옛 방의 `SaveRoom` 이 새 방 기록을 덮어쓴다(템플릿이 도로 작아짐).
- 상점 카탈로그는 `price.amount > 0` 만 내보낸다. 0 이면 수확물 = **파는 물건**이지 사는 물건이 아니다(0루피에 사서 파는 무한 루피 구멍).
- 빌드 전에 서버가 떠 있으면 DLL 잠김(MSB3026) → `taskkill //F //IM Harbor.Server.exe` 먼저. 검증 에이전트가 이걸 하면 사용자 창도 같이 죽으니 주의.
- Godot Node 서브클래스에서 `IsConnected` 같은 이름은 `GodotObject` 멤버와 충돌 → 다른 이름 쓰기.
- **텍스트 입력은 LineEdit/TextEdit 쓰지 말 것** — 이 환경에서 한글 IME 조합이 깨진다(자모 낱개 커밋). `HangulInput` 사용.
- 정리용 로그 파일: `build.log server*.log srv.log godot_run.log godot_err.log Harbor.sln.bak` → `! rm -f build.log server*.log srv.log godot_run.log godot_err.log Harbor.sln.bak`
