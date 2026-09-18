# Harbor 작업 로그

## 2026-09-18 — 저장소를 PostgreSQL(Supabase)로 통 갈이 · 계정/게임 소유권 분리

웹(Next.js/Vercel)과 게임 서버가 **같은 DB 를 보되 서로 덮어쓰지 않게** 하는 것이 목적이었다.
파일 SQLite 로는 애초에 불가능했다(웹에서 접근 불가) — 그래서 옮겼다.

### 소유권을 테이블로 갈랐다 (이번 변경의 핵심)
- 옛 스키마는 `account` 한 테이블에 **비밀번호와 루피가 섞여** 있어 분리가 불가능했다.
- 이제 `account`(닉·비밀번호·role) = **웹 소유**, `player`(지갑·외모·출석) = **게임 서버 소유**로 쪼갰다.
- 가입은 웹에서만(`Accounts.Authenticate(allowCreate:)`). 테스트 중에는 `Server:AllowGameSignup=true` 로 열어 둠.
- 닉 대소문자 무시는 citext 확장 대신 **`nick_key` = 소문자 닉**을 PK 로 써서 해결.

### 같이 들어간 것
- `login_log` — 웹·게임 양쪽이 **성공·실패 모두** 기록(IP 포함). 게임 루프를 막지 않게 큐 → 3초 flush.
- `notice` — 공지/업데이트 노트(초안·발행 분리).
- `SqliteImport` — `Harbor.Server.exe --import-sqlite=<경로>` 로 옛 저장본을 한 번에 이관(여러 번 돌려도 안전).
- **RLS 자동 적용** — Supabase 는 `public` 스키마를 REST API 로 자동 노출하고 `anon` 키는 공개값이다.
  `Db.Migrate` 가 모든 테이블에 `ENABLE ROW LEVEL SECURITY` 를 건다(정책 없음 = 외부 전면 거부, 소유자인 서버는 무관).
  Supabase 프로젝트 설정에서도 **Data API 를 껐다.**

### 비밀정보 (저장소가 public 이라 기준이 다르다)
- 접속 문자열은 **환경변수 `HARBOR_DB` 로만**. `appsettings.json` 은 빈 값이고, 없으면 **서버가 기동하지 않는다.**
- `.env`(추적 제외) + `.env.example`(값 없음) + `tools/run-server.sh`(명령줄에 키가 남지 않게) + `tools/check-secrets.sh`(커밋 전 스캔).
- 상세: `docs/secrets.md`, 규칙 요약은 `CLAUDE.md` 맨 위.

### 실제로 걸린 것 두 가지
1. **csproj 주석에 `--`** 를 넣어 MSB4025(XML 주석에 `--` 불가). 문구만 바꿔 해결.
2. **`.env` 값에 따옴표가 없어** 셸이 `;` 에서 줄을 끊고 `SSL Mode=Require` 를 명령으로 실행했다.
   `HARBOR_DB` 에는 첫 `;` 앞 45자만 담겼다 → **값을 작은따옴표로 감싸야 한다.** `.env.example` 에 경고를 박아 뒀다.

### 검증 (2026-09-18)
- 이관: **계정 19 · 플레이어 19 · 가방 100줄 · 방 19 · 가구 23 · 원장 118줄**
- 기동: `db loaded: users=19 rooms=19 from aws-0-ap-northeast-2.pooler.supabase.com:5432/postgres`,
  `rooms=13 furni=19 저장된방=17 저장된유저=19`, 계열/단계 경고 없음, :30000·:8080 리슨.

## 2026-09-18 (배포 준비) — 입장권이 서버 주소를 싣는다

목표: GitHub 공개 + 지인 10명 테스트. 그 전에 **치명적인 구멍 하나**가 있었다.

- **클라이언트의 서버 주소가 `127.0.0.1` 로 박혀 있었다.** 테스터 PC 에서 실행하면 자기 자신에게 접속을 시도한다 — 아무도 못 들어온다.
- 고친 방식: **입장권 코드가 주소를 함께 싣는다.** `내주소:30000/a1b2…` 한 줄이면 끝이고, 앞에 `harbor://` 가 붙어도 된다.
  - 랜딩 페이지는 `location.hostname` 을 그대로 쓴다 — **페이지를 연 주소가 곧 서버 주소**이므로 설정이 필요 없다.
  - 게임 포트는 `/api/status` 가 `gamePort` 로 알려 준다(appsettings 를 바꿔도 따라간다).
  - `RoomView.ParseConnect` 가 `코드만` / `host/코드` / `host:port/코드` 세 형태를 모두 받는다. 비밀번호 칸에 붙여넣는 경로도 같은 함수를 쓴다.
  - 웹을 거치지 않을 때를 위해 `HARBOR_HOST`/`HARBOR_PORT`, `--host=`/`--port=` 도 받는다.
- `.gitignore` 에 **`saves/`·`*.db` 추가** — `harbor.db` 에는 계정 비밀번호 해시와 소금이 들어 있다. 공개 저장소에 올라가면 안 된다.
- 배포 순서·제약은 `docs/deploy-mvp.md` 에 정리. **Vercel 에는 게임 서버를 올릴 수 없다**(상주 TCP·파일 SQLite·입장권이 같은 프로세스여야 함).
  올릴 수 있는 건 소개/다운로드 정적 페이지까지다.
- 아직 없는 것: `export_presets.cfg`(Godot 내보내기 프리셋) — 에디터에서 한 번 만들어야 테스터용 exe 가 나온다.

## 2026-09-18 (13차) — 출석 보너스 · 수확물 팔기 · 방 넓히기

"꾸미고 꾸며 나가는 게임"을 **돌아올 이유 / 모을 이유 / 커질 이유** 셋으로 나눠 구현한 라운드.
앞선 세션에서 Core·프로토콜·스키마·데이터까지 들어가 있었고, 이번에 **서버 처리와 클라 UI**를 채웠다.

### 1. 출석 (돌아올 이유) — `Harbor.Core/Attendance.cs`
- 그전까지는 매일 같은 100루피였다. 이제 연속으로 올수록 `기본 + 50×(연속−1)`, **7일차에서 상한**.
  상한이 없으면 오래 한 사람만 부자가 되고, 하루 빠진 손해가 너무 커지면 접속이 부담이 된다.
- 끊김 판정은 **날짜**로: 어제 왔으면 +1, 하루라도 건너뛰면 1부터. 이미 받은 날이면 그대로.
- `account.streak` 컬럼(스키마 v3, `ALTER TABLE` 로 기존 DB 이관) · `UserSave.Streak` · `Session.Streak`.
- 로그인 안내가 "오늘의 용돈"에서 "**3일 연속! 출석 보상 200 루피**"로 바뀐다. 원장 이유는 `출석 N일차`.

### 2. 수확물 팔기 (모을 이유) — `Harbor.Core/Trade.cs`
- 캣닢 화분에서 나온 **캣닢 잎**(`flower_cut`)을 가방에서 판다. 낱개 40, **30장 묶음 1500**(낱개 30장=1200).
  묶음을 먼저 채우므로 **모아서 한 번에 파는 게 이득** — 그게 다시 화분을 돌볼 이유가 된다.
- 값은 순수 함수(`Trade.QuoteSell`)라 서버와 클라 미리보기가 같은 값을 본다. 클라는 `전부 팔기 3,200` 처럼 총액을 미리 보여준다.
- 서버 `C_SellItem`: 정의에 `sell` 이 없으면 코드 32, 수량이 모자라면 24. 차감은 `InvTryTakeMany` 로 **전부 아니면 전무**.
- **구멍 하나 막음**: 카탈로그가 `price 0` 인 정의까지 보내고 있었다 → `flower_cut` 을 **0루피에 사서 40루피에 파는** 무한 루피가 될 뻔했다.
  상점 목록에서 `Price.Amount > 0` 만 내보내고, 구매 핸들러도 0 이하를 거절한다(코드 31).

### 3. 방 넓히기 (커질 이유)
- `다락방`(10×6) → **`넓은 다락방`**(14×8, 800루피) → **`큰 다락방`**(18×10, 2500루피).
  넓힌 템플릿은 **이전 템플릿의 걸을 수 있는 칸을 같은 좌표로 품는다** → 놓아 둔 가구가 제자리에 남는다.
- 교체는 **옛 방의 룸 루프에서** 한다(`RoomCommand.Upgrade`): 그 순간의 가구 목록을 그대로 새 인스턴스에 넘기고,
  옛 방은 즉시 `_closed` 로 막는다. 이렇게 하지 않으면 옛 방이 뒤늦게 `SaveRoom` 하면서 **같은 키(u:닉)를 쓰는 새 방의 기록을 덮어쓴다**(템플릿이 도로 작아진다).
- 값은 핸들러가 먼저 받고, 교체가 실패하면 룸 루프가 **되돌려 준다**(요청이 겹쳤을 때 루피만 사라지지 않게).
- 방 id 가 바뀐다 → 서버는 `Session.HomeRoomId` 를 갱신하고, 클라는 스냅샷에서 `OwnerNick == 내 닉` 이면 `HomeRoomId` 를 다시 잡는다.
  (이게 없으면 넓힌 직후 **내 방인데 "구경만"** 이 된다.)
- `RoomDto` +UpgradePrice/UpgradeName/Width/Height → [방 목록] 창 맨 위에 "내 방 넓히기 → 넓은 다락방 800루피".

### 4. 그 밖에 고친 것
- `S_InventoryUpdate` 를 받을 때 클라가 `Interaction` 을 **버리고 있었다** → 포스트잇을 주웠다 놓으면 "붙이기"가 "배치"로 바뀌고 남의 방에 못 붙였다.
- `S_Notice` 는 정의만 있고 아무도 안 보냈다/안 받았다 → 판매·넓히기 안내에 쓰고, 클라가 토스트+채팅 로그로 받는다.
- `flower_pot`/`flower_cut` 의 클라 placeholder 이름·색을 캣닢(초록)으로 맞춤(서버 표시명과 어긋나 있었다).

### 테스트
`AttendanceTests`(5) · `TradeTests`(5) · **`RoomUpgradeTests`** — 넓힌 방이 이전 방의 칸·높이·벽을 같은 좌표로 품는지,
방이 한 덩어리로 이어졌는지, 뒤쪽 트인 면에 벽이 있는지, 줌 1배 화면에 들어오는지를 단위 테스트로 고정.
Seed 는 어긋난 가구를 버리지 않고 **보정**하므로, 템플릿 실수는 조용히 자리만 틀어진다 → 테스트로 잡는 수밖에 없다.

### 5. 방 템플릿 3계열 (2026-09-18 추가)
- **다락방 계열 5단계**: 다락방(60칸) → 넓은(112) → 큰(180) → **다락 저택**(264, 8,000) → **항구 저택**(336, 20,000).
- **모퉁이집 계열 3단계**(ㄱ자, 64/120/192) · **복층방 계열 3단계**(뒤쪽 세 줄이 한 단 높음, 60/126/198).
- **크기 상한은 화면이 정한다**: 줌은 1~4배뿐(기본 2배)이고 1배가 가장 넓다. 아이소 2:1 이라 화면 폭 = `(가로칸+세로칸)×32`
  → **걸을 수 있는 영역 24×14 가 1280px 한계**. `RoomUpgradeTests.Rooms_FitOnScreen` 이 지킨다.
- **ㄱ자에서 실제로 걸린 것**: 벽은 누적 영역(`x'≤x, y'≤y-1` 전체가 비어야 벽) 판정이라, 홈을 가까운 쪽에 파면
  **날개 윗변에 벽이 아예 안 생겨 방이 뚫려 보인다.** 홈은 **먼 구석(x·y 둘 다 작은 쪽)** 에만 팔 수 있다.

### 6. 이사 (계열 갈아타기) — 사용자 선택: "이사 기능"
계열은 넓히기로는 못 바꾼다(포함 관계). 그래서 **이사**를 넣었다. 첫 집은 여전히 다락방이고, 루피를 내면 언제든 모양을 바꾼다.

- `RoomDef` +`family`/`tier` → 11개 개인 방 JSON 에 표기. 이사 목록은 **계열마다 한 채**, **지금 단계와 같은 단계**를 보여준다.
  단계를 유지하지 않으면 넓히는 데 쓴 2만 루피가 이사 한 번에 날아간다.
- **가구는 전부 주인 가방으로 회수**된다(모양이 달라 제자리에 둘 수 없다). 새 방은 빈 채로 짓는다 —
  `Create` 에 **빈 seed 를 명시**해야 한다. 안 그러면 저장소에 남아 있는 옛 배치를 그대로 복원해 버린다(같은 키 `u:닉`).
- 넓히기와 **같은 경로**(`RoomCommand.Upgrade` + `ReturnItems`)를 쓴다 — 룸 루프에서 가구를 읽고, 옛 방을 닫고, 사람을 옮기는 절차가 똑같다.
- 값은 `Economy.RemodelPrice` = 1,000. **계열 간 단계 값 차이(최대 700)보다 비싸게** 잡았다 — 싸면 싼 계열로 올린 뒤 이사해 이득을 본다.
  같은 이유로 서버는 클라가 보낸 templateId 의 **계열만** 쓰고 단계는 다시 고른다.
- 프로토콜: `C_HouseList`/`S_HouseList`(HouseStyle: 이름·칸 수·값·현재 여부) · `C_RemodelRoom`. 오류 35 = 이미 그 집.
- `Program.cs` 가 기동할 때 계열/단계 사슬을 훑어 어긋나면 `⚠` 를 찍는다 — JSON 은 단위 테스트가 못 보기 때문.

### 검증 (2026-09-18)
- **sln 0 오류 · 클라 0 오류/0 경고 · 테스트 132/132 통과.** (13차 + 방 템플릿 11채 검사 포함)
- 잡힌 컴파일 오류 1건: `RoomManager.Remodel` 의 람다 파라미터를 `_` 로 두는 바람에 `_rooms.TryRemove(old.Id, out _)` 의
  `out _` 가 discard 가 아니라 **그 람다 파라미터**로 해석됐다(CS1503). 파라미터 이름을 `items` 로 바꿔 해결.
- ⏳ 아직 안 한 것: 실제 기동/e2e, 그리고 눈 확인. v2→v3 이관(streak 컬럼), 출석 연속 보상, 캣닢 판매 후 잔액·원장,
  **방 넓히기 후 가구가 제자리인지**, **이사 후 가구가 전부 가방에 들어왔는지**, 넓힌/이사한 뒤에도 내 방으로 인식되는지.

## 2026-09-17 (12차) — 첫 도트 아트 투입 (아틀라스 파이프라인 실전)

사용자가 GPT 로 고양이 도트를 생성 → `art/ref/고양이.png`(1254×1254, 정면 서기 1장). 이걸 게임에 넣기까지의 기록.

### 결과
`client-godot/art/atlas.png` (53×69, 14색) + `atlas.json` → 게임에서 실제로 렌더됨. 스크린샷 `art/ref/ingame2.png`.

### 이 과정에서 드러난 함정 4개 (전부 실제로 밟았다)

**1. Godot 은 새 PNG 를 자동으로 안 읽는다.**
프로젝트 폴더에 파일을 넣는 것만으로는 `GD.Load` 가 `No loader found for resource` 로 실패한다. `.import` 와 `.godot/imported/*.ctex` 가 있어야 하고, 그건 **에디터 패스**에서만 생긴다. `--path` 로 게임을 실행하는 것으로는 절대 안 생긴다.
→ `tools/import-assets.ps1` (`--import`, 실패 시 headless 에디터 폴백). **그림을 넣거나 바꿀 때마다 한 번 돌려야 한다.**

**2. `AssetCatalog` 이 실패했을 때 더 나빠졌다.**
시트 로드 실패 후에도 `AtlasTexture { Atlas = null }` 을 만들어 캐시에 넣었다 → `TryGet` 이 non-null → `_placeholder = false` → **코드로 그린 고양이는 꺼지고 스프라이트는 비어서 아무것도 안 그려짐.** 아틀라스를 넣기 전보다 나쁜 상태.
→ 전면 재작성. 어느 단계에서 실패하든 **캐시를 비운 채** 돌아가 placeholder 로 안전하게 되돌아간다. `ResourceLoader.Exists` 로 미리 확인해 엔진의 긴 오류 스택도 안 뱉게 했다.

**3. 방향이 없으면 아바타 전체가 placeholder 로 떨어졌다.**
`cabin_default_01` 스폰은 dir 0(뒤). 아틀라스엔 dir 4(정면) 한 장뿐 → 못 찾음 → 통째로 placeholder. 대체는 *동작*만 있고 *방향*은 없었다.
→ `PickTexture`: 그 방향+그 동작 → 그 방향+서기 → **정면+그 동작 → 정면+서기**. 정면으로 대체할 땐 좌우 반전을 끈다(미러는 옆모습 그림이 있을 때만 의미).
**이게 없으면 그림을 한 장씩 채워 넣는 작업 자체가 불가능하다.**

**4. 줄이면 도트가 깨진다.** (사용자: "도트가 너무 깨진다")
첫 변환은 693×898 → 37×48 단일 NEAREST. 원본 격자가 53×69 인데 1.44배로 줄이니 **어떤 칸은 사라지고 어떤 칸은 두 배**가 됐다. 확대해 보면 양쪽 귀 두께가 다르고 꼬리 줄무늬 폭이 들쭉날쭉.
→ **줄이지 않는다.** 블록 경계를 직접 검출(열/행 차분 신호의 지역 최대)해 한 칸 = 한 픽셀로 대응. 각 칸은 **최빈색**(평균 아님 — 평균은 도트를 흐린다), 칸 안쪽 1px 여백은 제외(경계 안티앨리어싱 오염 방지).
검출 결과 정확히 53×69. 블록 길이는 8~17px 로 **실제로 들쭉날쭉**했으므로 균일 격자 가정은 틀렸을 것.
곁가지 두 개도 처리: 가장자리 1px 슬리버 블록(암묵 경계 0/n 에도 최소 간격 적용), 같은 색이 이어져 경계가 안 잡힌 구간(중앙값의 1.6배 이상이면 등분).

### 도트가 아닌 것도 고침
- `project.godot`: `snap_2d_transforms_to_pixel` / `snap_2d_vertices_to_pixel` **켜기**. 걷는 중 위치가 소수점으로 보간되면 도트가 반 칸에 걸쳐 찢어진다.
- `AvatarView.AnimateSprite()`: 프레임이 한 장뿐이어도 몸통이 움직이게(걷기 3px, 춤 4px, 대기 숨쉬기 1px). **정수 픽셀로만** 움직인다.
- `spr.Offset = (0, -tex.Height/2)`: 발끝 기준 앵커를 텍스처 높이에서 계산 → 스프라이트 크기가 바뀌어도 `.tscn` 을 안 고쳐도 된다(실제로 40×48 → 53×69 로 바뀌었는데 무수정).

### AI 이미지로 도트를 만들 때 알아둘 것
- 생성 AI 는 **"픽셀 아트 풍"** 을 그리지 진짜 도트를 찍지 않는다. 격자가 균일하지 않다(여기선 8~17px).
- 그래서 **원본 격자를 복원**하는 게 핵심이고, 임의 크기로 리샘플하면 반드시 깨진다.
- 색은 897개 → 16색 요청 → 실제 14색. 거의 같은 크림색 수십 개가 합쳐진다.
- 프롬프트와 절차는 `art/ref/sprite-prompt.md`.

### 남은 것
프레임이 정면 서기 1장뿐 → 모든 방향·동작이 그 한 장. 다음 우선순위: **걷기 2장** → 옆모습 → 대각선 → 뒷모습 → 앉기.
크기(53×69)가 타일(64×32) 대비 큰지는 사용자 눈 확인 대기.
## 2026-09-17 (10차) — 루피 원장 · 시간이 흐르는 가구 · 꽃 화분

사용자 방향: "나가면 휘발되는 게임이 아니라 꾸미고 꾸며 나가는 게임". 퍼피레드의 **식물 키우기**가 이 문장에 가장 가까운 기능.

### 1. 루피 원장 (`currency_log`)
| 열 | 뜻 |
|---|---|
| nick, delta, balance, reason, at | 누가 / 얼마 / 그래서 잔액 / 왜 / 언제 |

- `Session` 의 루피 조작을 **이유 없이는 못 하게** 바꿨다: `RupeeSet(값, 이유)` · `RupeeAdd(증감, 이유)` · `RupeeTrySpend(금액, 이유)`. 읽기 전용 `Rupee` 프로퍼티만 남김.
- 남는 이유들: `시작 자금` · `일일 용돈` · `상점 구매 {furniId}×{수량}` · `복권 구매` · `복권 당첨 {등수}`.
- `SaveStore.LogCurrency` 로 큐에 쌓고 flush 때 함께 INSERT. flush 실패 시 큐 앞쪽에 되돌려 순서 유지.
- **왜 필요한가**: 재화 사고는 기록 없이는 복구도 추적도 못 한다. 그 시절 운영에서 가장 많이 터진 문제.

### 2. 스키마 v1 → v2 (`Db.ApplyV2`)
- 새 DB 는 최신 버전으로 바로 기록, 기존 DB 는 `schema_info.version` 을 보고 단계를 밟는다.
- v2 = `currency_log` 테이블 + `room_item.state_at` 컬럼(`ALTER TABLE`, `pragma_table_info` 로 중복 방지).

### 3. 시간이 흐르는 가구 (이번 라운드의 핵심)
그전까지 FSM 타이머는 **메모리에만** 있었다(`Task.Delay`). 서버를 껐다 켜면 타이머가 사라져 가구가 그 상태로 멈춘다.
- `RoomItem.StateAtUtc` — 지금 상태가 시작된 시각. `room_item.state_at` 에 저장.
- `ScheduleTimer(item)` — **남은 시간만큼만** 예약. 서버가 꺼져 있던 동안 지났으면 즉시 발화 → 돌아오면 이미 자라 있다.
- 호출 지점 세 곳: 배치 직후(`OnPlace`), 상태 전이 후(`Transition`), 복원 시(`Seed`). Seed 는 루프 시작 전이지만 `Post` 는 채널에 쌓이므로 안전.

### 4. 꽃 화분 (`flower_pot`, 60루피)
`seed` →(2분)→ `sprout` →(3분)→ `bud` →(5분)→ `bloom` →(use)→ `seed` + `give_item:flower_cut`.
다 피면 꺾어서 장식(`flower_cut`)으로 쓰고 화분은 다시 처음부터. **돌아올 이유**가 생긴다. 시작 가방에 2개.

### 5. 헛클릭 없애기 (`ItemDto.Usable`)
자라는 중인 화분을 눌러도 아무 반응이 없었다(`Fsm.Apply` 가 null 이면 서버가 조용히 무시).
- 서버가 "지금 상태에서 use 가 먹히는가"를 `Usable` 로 알려준다(`ItemDto` Key 16, `S_ItemState` Key 3).
- 클라는 `Usable` 이 false 면 사용 요청 대신 `꽃 화분 — 새싹` 같은 안내를 띄운다. 가구 종류를 클라가 알 필요가 없는 **일반적인** 방식.

### 6. 클라 렌더
`FurniPalette.Shape.Flower` 추가. `DrawPlant` 가 상태별로 분기: 씨앗(흙+씨), 새싹(짧은 줄기+잎 2), 꽃봉오리(줄기+닫힌 봉오리), 활짝(꽃잎 6장+노란 수술). 상태 없는 `plant_pot` 은 기존 관엽 그대로. 상태 배지 씨앗/새싹/꽃봉오리/활짝!

### 검증 (부분) — 사용자 서버가 떠 있어 e2e 는 보류
- 클라 0/0, 테스트 58/58. 서버는 출력 폴더를 옮겨 따로 빌드 → **0/0** (코드 자체는 이상 없음).
- `Harbor.Server.exe` 가 잠겨 있어 정식 빌드는 MSB3021/3027 로 실패. 실행 중인 건 여전히 9차 바이너리 → **DB 는 아직 v1 이라 이관 검증은 유효**.
- 남은 것: v1→v2 이관 확인, 원장 적재 확인, 식물 성장 왕복. 서버 종료 후 재실행.
- ⚠ 다음 검증 의뢰 때 **Godot 콘솔 exe 전체 경로를 반드시 명시**할 것(이번에 빠뜨려 에이전트가 못 찾음).

### 최종 검증 결과 (사용자가 서버를 닫은 뒤 재실행)
- 빌드 0/0, 테스트 58/58.
- **v1 → v2 이관 실측**: version 1→2, `currency_log` 생성, `room_item.state_at` 추가, 기존 계정 3개(dbuser 500 / DOYEON 0 / olduser 777) **루피 그대로**. 이관은 로그를 남기지 않으므로 스키마 차이로만 확인됨.
- **원장**: 86줄 기록, 첫 줄이 `planter | 500 | 500 | 시작 자금`. 모든 줄에서 잔액 = 직전 잔액 + 증감, 마지막 잔액(90) = `account.rupee`. 산술 일관성 확인.
- **식물 재시작 성장**: `seed`(2020년 심음) → 기동 즉시 `sprout`. 타이머가 저장된 시각에서 이어지는 것은 증명됨.

### 검증이 잡은 결함 2건
1. **밀린 성장이 한 단계씩만 처리됨** — 6년 밀렸는데 `bloom` 이 아니라 `sprout` 까지만. `Transition` 이 새 상태 시작을 **지금**으로 덮어써 남은 밀린 시간을 버렸다.
   **수정**: `OnItemTimer` 가 원래 만기 시각(`StateAtUtc + timer`)을 계산해, 그게 과거면 그 시각을 새 상태의 시작으로 넘긴다 → `ScheduleTimer` 가 다시 즉시 발화해 연쇄적으로 따라잡는다. 순환 타이머 FSM 이 생겨도 폭주하지 않도록 `MaxCatchUpSteps = 200` 상한(`RoomItem.CatchUpSteps`).
2. **아무도 누르지 않은 복권이 85회 뽑힘** — `planter` 루피 500 → 90. 조사 중.
   grep 결과 `C_LotteryDraw` 송신은 `RoomView.DrawLottery()` 한 곳, 그 호출자는 `HudView.RenderLottery()` 의 버튼 하나뿐. 그 창은 셀프테스트 중 열리지도 않는다 → 코드만으로는 설명 불가. 재현·이분탐색 조사 의뢰함.
   **이번에 만든 원장이 없었으면 못 찾았을 결함.** 원장을 넣은 이유가 그대로 증명됐다.

## 2026-09-17 (11차, 진행 중) — 주제 변경: 사람 → 고양이

사용자: "팝플은 사람이었는데 고양이라든가 새로운 주제로 바꿀까?" → "낭만고양이 컨셉으로 가자". 이름은 별도(도메인·상표 고려).
설계 근거는 `docs/theme-proposal.md`.

- **아바타를 두 발로 다니는 고양이로** (`AvatarView`): 둥근 머리 + 삼각 귀(안쪽 분홍, 가끔 쫑긋) + 주둥이·코·수염 + **늘 살랑거리는 꼬리**. 이족 유지 — 네 발로 바꾸면 앉기/옷/모자/춤이 전부 깨진다.
- **무늬 4종**: 민무늬 / 턱시도(가슴받이+꼬리끝) / 얼룩(이마 줄무늬+꼬리 링) / 젖소(한쪽 눈 얼룩+몸통 반점).
- **figure 문자열 형식은 그대로, 뜻만 변경**: `hd` 피부색→**털색**, `hr` 머리모양·색→**무늬·무늬색**. 옷·모자는 그대로.
  → **저장된 외모 데이터 마이그레이션 불필요.**
- `Ui`: `SkinTones` → `FurColors`(8색), `PatternColors`(6색), `EarInner` 추가. HUD 외모 창 라벨을 털색/무늬/무늬색으로.
### 제품명 확정: **묘항 (Myohang)** = 고양이 묘(猫) + 항구 항(港)
후보였던 묘연/밤항구/냥포트 중 선택. 짧고 뜻이 분명하며 코드명 Harbor 와 그대로 이어진다.
낭만고양이는 **컨셉으로만** 품는다(2002년 체리필터 곡 제목 — 도메인·상표 충돌 우려로 제품명으로는 쓰지 않음).

적용: 랜딩 페이지(제목·카피·밤항구 배경색), 게임 시작 화면, `project.godot` 창 제목, README.
방 이름 기본 선실 → **다락방**, 메인 갑판 → **부둣가 광장**. 콜라 캔/자판기 → **우유팩/우유 자판기**.
⚠ `furniId` 는 `cola_can`/`cola_machine` 그대로 뒀다 — **이미 저장된 방·가방이 그 id 로 가구를 가리킨다.** 표시 이름만 변경.

### 복권 69회 미스터리 — 버그 아님 (조사 결과)
- 재현 6회 시도, 0회 발생. 클릭 10회 → 정확히 10회 뽑힘(1:1, 증폭 없음). 버튼을 10초 누르고 있어도 1회.
- `planter` 기록 분석: 평균 **189ms 간격**(사람 연타 속도), 지터 ±20ms. 코드 루프면 ~16ms, 타이머면 더 일정.
  로그인 **7초 뒤** 시작(창 보고 → 복권 클릭 → 뽑기까지 걸리는 시간). 잔액 90에서 멈춤(9번 더 뽑을 수 있었는데도).
  당첨률 4.3%/18.8% 가 확률표 5%/20% 와 일치 → 69번 **독립적으로 추첨된 진짜 요청**.
- 결론: 자동 검사가 띄운 게임 창을 사람이 만졌다. **전제("아무도 안 눌렀다")가 틀렸던 것.**
- Godot 4.7 확인: `button_pressed` 대입은 `toggled` 만 내보내고 `pressed` 는 실제 입력에서만 → `ToggleWindow` 재진입 불가.

### 조사가 발견한 진짜 결함 3건 → 수정
1. **`QueueFree` 만으로는 트리에서 안 빠진다.** 삭제는 프레임 끝에 일어나므로 한 프레임 동안 옛 위젯이 남아 **보이고 클릭도 받는다**. 옛 버튼은 여전히 복권 뽑기·구매에 연결된 상태. 런타임에서 `kids=4` → `kids=8` 로 두 세대 공존 확인됨.
   → `RemoveChild(c)` 후 `QueueFree()`.
2. **한 프레임에 창을 두 번 그림** — 복권 1회에 지갑 갱신과 결과 도착이 따로 와서 각각 다시 그렸다.
   → `QueueRender()` 로 모아 프레임당 1회(`CallDeferred`). 창을 여는 순간만 즉시 그린다.
3. **포스트잇 창이 닫히는 버그** — 열려 있는데 포스트잇을 또 붙이면 `ToggleWindow(Win.Postit)` 가 같은 창이라 **닫아 버렸다**.
   → 이미 열려 있으면 내용만 다시 그린다.

### 검증 결과 (11차)
- sln 0/0, 클라 0/0(`Ui.SkinTones` 삭제 잔재 없음), 테스트 58/58.
- **밀린 성장 연쇄 확인**: 2020년 `seed` → 기동 후 **`bloom`**(이전엔 `sprout` 에서 멈췄다). `state_at` 이 `2020-01-01T00:10:00` 인 것은 **의도대로** — 2020년에 심었다면 10분 뒤에 폈을 것이므로 그 시각이 맞다.
- 셀프테스트 정상, `enter: catcheck → catcheck의 방` 로그 확인, `currency_log` 에 `catcheck` 는 **시작 자금 1줄뿐**(유령 복권 없음).
- **셀프테스트가 끝나도 창이 안 닫혀 화면에 남는 문제** → `GetTree().Quit()` 추가. 남은 창을 사람이 만져 검증이 오염된 게 바로 위의 복권 건이었다.

### 남은 것
낚시 미니게임을 중심 콘텐츠로, 펫 재검토(고양이가 주인공이므로 새·물고기 쪽), 배경음악, 친구 목록, 아틀라스 도트 아트(`art/ref/sprite-prompt.md`).

## 2026-09-17 (9차) — 저장을 테이블로 (SQLite + Dapper)

사용자: "회원들은 각각 테이블에 저장 되어있어야겠지", "나가면 휘발되는 그런 게임이 되어서는 안되고 꾸미고 꾸며 나가는 게임이어야 해".

### 왜 바꿨나
파일 두 개(`users.json`/`rooms.json`)에 **통째로** 쓰고 있었다. 누가 의자 하나를 옮겨도 전체를 다시 썼다. 사람이 늘면 그대로 못 쓴다.

### 스키마 (`Data/Db.cs`)
| 테이블 | 담는 것 |
|---|---|
| `account` | nick(PK, 대소문자 무시), salt, hash, rupee, figure, last_allowance, created_at, updated_at |
| `inventory` | (nick, furni_id) PK, qty — 계정 삭제 시 CASCADE |
| `room` | room_key(PK: `u:닉` / `t:템플릿`), template_id, name, owner_nick |
| `room_item` | id, room_key, furni_id, x, y, dir, wall_u, wall_v, tilt, state, extra, author |
| `schema_info` | version |

WAL 모드, `synchronous=NORMAL`, 외래키 ON. 표준 SQL 에 가깝게 써서 PostgreSQL 로 옮기기 쉽게.

### 설계 (기존 동작 유지)
`SaveStore` 의 **공개 API 는 그대로** 두고 내부만 바꿨다. 서버 나머지는 한 줄도 안 고쳤다.
- 진실은 여전히 메모리(`ConcurrentDictionary`). 게임 루프가 가방을 자주 건드리므로 호출마다 DB 를 치지 않는다.
- 바뀐 것만 **dirty 집합**에 표시 → `SaveService` 가 3초마다 그 행만 UPSERT. 방은 `room_item` 을 지우고 다시 넣는다(가구 수가 적어 충분).
- flush 실패하면 dirty 를 되돌려 놓고 다음 주기에 재시도 → 데이터를 잃지 않는다.
- DB 가 깨지면 `.bad-<시각>` 으로 치우고 빈 상태로 재생성 → 서버가 못 뜨는 일 방지.

### 기존 저장본 자동 이관
DB 가 비어 있고 `users.json`/`rooms.json` 이 있으면 **한 번만** 읽어 들여 DB 에 쓰고 `.imported` 로 이름을 바꾼다. 비밀번호가 없던 옛 계정은 salt/hash 를 비운 채로 옮기고, 그 사람이 다음에 로그인할 때 비밀번호가 생긴다.

### 검증 (빌드 검증 에이전트)
- sln 0/0, 클라 0/0, 테스트 58/58.
- **이관**: 비밀번호 없는 `olduser`(777루피, chair_wood×5, 외모, 용돈 날짜) JSON → DB 에 그대로. `users.json.imported` 로 변경, `harbor.db` 생성.
- **DB 직접 조회**(python sqlite3): 테이블 5개 + sqlite_sequence 확인. `olduser` 는 salt/hash 길이 0 유지(가짜 자격증명을 만들지 않음), `dbuser` 는 salt 24자·hash 44자.
- **왕복**: 웹 로그인 → 입장권 → 게임 접속 → 서버 강제 종료 → 재기동 시 `저장된유저=2 저장된방=1`. `taskkill /F` 라 종료 flush 가 안 돌았는데도 3초 주기 flush 가 이미 커밋해 둔 것.
- `enter:`/`leave:` 로그 동작 확인 — `dbuser → dbuser의 방`, `dbuser → 메인 갑판`.

### 알아둘 것
- **서버 로그 인코딩이 실행마다 다르다.** CP949 일 때도, UTF-8 일 때도 있었다. 읽기 전에 확인할 것.
- 깨끗이 시작하려면 `src\Harbor.Server\bin\Debug\net8.0\saves\` 폴더 삭제(지금 검증용 `olduser`/`dbuser` 가 들어 있음).
- 다음: **`currency_log` 원장** — 루피가 오간 내역(누가, 얼마, 왜, 잔액). 그 시절 운영에서 제일 많이 터진 게 재화 문제였고 기록 없이는 복구도 추적도 못 한다.

## 2026-09-17 (8차) — 계정 · 웹 랜딩 페이지 · 로그인 → 입장권 → 게임

사용자 요청: "각자 설치해서 연결하는 대신 특정 웹사이트의 게임 시작 버튼 눌러서 들어오면", "회원은 각각 테이블에 저장", "퍼피레드나 팝플처럼".

### 확인한 제약 (웹에서 게임 자체를 돌리는 건 지금 불가)
- **Godot 의 .NET 빌드는 웹 내보내기를 지원하지 않는다.** C# 때문이 아니라 .NET 빌드 자체가 웹으로 못 나간다(GDScript 프로젝트여도 마찬가지). 시기 미정.
- 브라우저는 **일반 TCP 소켓을 못 연다.** 웹 클라이언트를 만들려면 서버에 WebSocket 을 붙여야 한다.
- → 그래서 이번엔 **웹 로그인 + 입장권 → 데스크톱 클라이언트 실행**까지 만들었다. 이건 퍼피레드·팝플 시절의 정석 흐름 그대로다.

### 1. 계정 (보안 구멍 메움)
그전까지 로그인은 **닉네임만 치면 통과**였다. 닉으로 방·지갑을 찾으므로 공개 서버에 올리면 남의 닉을 입력하는 것만으로 남의 방과 루피를 가져갈 수 있었다.
- `Harbor.Core/PasswordHash.cs` — PBKDF2-SHA256, 12만 회, 계정마다 다른 소금, 상수 시간 비교. 평문은 어디에도 안 남는다. 테스트 9건.
- `Accounts` — 닉+비번 확인을 웹/게임이 공유. 처음 쓰는 닉이면 그 자리에서 가입(비밀번호가 곧 소유 증명). 비밀번호 없던 옛 저장본은 다음 로그인 때 자동 마이그레이션.
- `OnlineUsers` — 같은 닉 동시 접속 차단(둘이 들어오면 방·지갑이 서로 덮어써진다). `TcpHost` 가 끊길 때 반드시 `Release`.
- `UserSave` +Salt/Hash. `UpdateUser` 는 비밀번호를 건드리지 않고, `SetPassword` 는 나머지를 건드리지 않는다.

### 2. 웹 서버 (같은 프로세스)
- 서버 csproj 를 `Microsoft.NET.Sdk` → `Microsoft.NET.Sdk.Web`, `Program.cs` 를 `WebApplication` 으로. TCP 게임 서버는 그대로 `BackgroundService`.
- `StaticWebAssetsEnabled=false` + `PhysicalFileProvider` 로 `web/` 직접 서빙 — ContentRoot 를 `AppContext.BaseDirectory` 로 고정해 둔 것과 맞추기 위함.
- `POST /api/login` {nick,password} → 확인/가입 → **일회용 입장권**. `GET /api/status` → 접속자·방 수.
- `web/index.html` — 랜딩 페이지(소개, 실시간 접속자, 로그인 폼, 게임 시작, 입장권 코드+복사, 기능 소개).

### 3. 입장권 (`TicketStore`)
- 5분, 1회용, 40자리 hex. **비밀번호는 게임 서버로 넘어가지 않는다.**
- `C_Login.Token` 에 입장권이 오면 먼저 `Redeem` → 성공하면 그 닉으로 인증 완료. 아니면 비밀번호 경로.
- `S_LoginResult.Nick` 추가 — 입장권으로 들어오면 클라가 친 닉과 다를 수 있으므로 **서버가 정한 닉이 진짜**(포스트잇 작성자 비교에 쓰인다).
- 클라: `harbor://<코드>` / `--ticket=` / `HARBOR_TICKET` 에서 입장권을 찾으면 시작 화면을 건너뛰고 바로 입장. 비밀번호 칸에 코드를 붙여넣어도 동작(40자리 hex 면 입장권으로 판단).
- `tools/register-url-scheme.bat` — `harbor://` 를 HKCU 에 등록(관리자 권한 불필요). 해제용 bat 도 같이.

### 검증 (빌드 검증 에이전트)
- sln 0/0, 클라 0/0, 테스트 **58/58**, 랜딩 페이지 200 + 팝플 포함, `/api/status` 200.
- 로그인 3연속: 최초 `created:true`+입장권 / 틀린 비번 **400 "비밀번호가 맞지 않아요"** / 맞는 비번 `created:false`+**다른** 입장권.
- 그 입장권으로 `HARBOR_TICKET` 게임 접속 → `users.json` 에 `webuser` 가 Salt/Hash **와 함께** Rupee 500·시작 가구·용돈 날짜·기본 외모까지 기록됨.
  웹 로그인만으로는 Rupee 가 0 이므로, **이 값들은 입장권이 실제로 게임에서 교환됐다는 증거**다.

### 지적받은 것 → 수정
서버가 **방 입장을 전혀 로깅하지 않았다.** `Enter failed` 라는 문자열은 저장소에 존재하지도 않아서, 그동안 "Enter failed 없음"은 아무것도 증명하지 못하는 조건이었다.
→ `OnEnter`/`OnLeave` 에 `enter:`/`leave:` 정보 로그, 방이 가득 차면 `enter failed:` 경고 추가.

## 2026-09-17 (7차) — 외모 꾸미기(미용실·옷가게) · 댄스홀 · 무한 콜라

레퍼런스의 미용실(머리 감기·염색), 옷 가게(고인물은 늘 모자를 씀), 댄스홀(무제한 콜라 뽑아 마시며 춤) 대응.

### 1. 외모 시스템 (`Harbor.Core/Figure.cs`)
`파츠-모델-팔레트` 조각을 '.' 로 이은 문자열. `hd`(피부) `hr`(머리) `ch`(상의) `lg`(하의) `sh`(신발) `ha`(모자, 없어도 됨).
- 서버는 **형식만 검사**하고 뜻은 해석하지 않는다(렌더는 클라 몫). 길이 96, 모델 1~999, 팔레트 1~99, 파츠 중복 금지. 깨지면 `Sanitize` 가 기본 외모로.
- `C_SetFigure` → 검증(코드 40) → `Session.Figure` 저장 → `RoomCommand.Figure` → 같은 방에 `S_UserFigure` 방송.
- `UserSave.Figure` 로 영속. 재접속해도 그대로.
- 클라: `AvatarView.ApplyFigureColors()` 가 팔레트 인덱스 → 실제 색(머리/피부/상의/하의/모자). **figure 가 없으면 예전처럼 닉 해시 색**으로 폴백.
- 그리기 추가: 긴 머리(모델 2·3 이면 옆머리), 모자(챙 + 반원 크라운).
- HUD `[외모]` 창: 머리색 10 · 머리 모양 3 · 피부 5 · 상의 8 · 하의 6 · 모자(안 씀 + 8색). 누르면 즉시 반영·저장·전파.
- `Ui.Swatch` (색 견본 버튼), `Ui.Pick`(1-based 팔레트 인덱싱), `Ui.PantsColors`/`SkinTones`/`HatColors` 추가.

### 2. 방 템플릿 기본 가구 (`RoomDef.Furni`)
방 JSON 에 `"furni": [{furniId,x,y,dir,wallU,wallV,tilt}]` 를 적으면 **저장본이 없을 때** 그대로 놓인다(`RoomManager.TemplateFurni`).
저장본이 있으면 저장본이 이긴다 → 공용 방에서 누가 치우면 그 상태가 남는다.

### 3. 댄스홀 + 콜라 자판기
- `data/rooms/dancehall.json` — 공용, 16×9, 가운데 한 단 높은 무대, 최대 60명. 기본 가구 7개(콜라 자판기 2, 스탠드 조명 2, 벽시계 1, 창문 2).
- `data/furni/cola_machine.json` — `use` → `give_item:cola_can` → 1.5초 뒤 자동 복귀. **무한 콜라**(레퍼런스의 "무제한 병 콜라"). 상태 배지 "덜컹!".

### 검증 (빌드 검증 에이전트)
- sln 0/0, 클라 0/0, 테스트 **42/42**, e2e `rooms=3 furni=17`, 경고·예외 0. `users.json` 에 `Figure` 필드 왕복 확인.
- 댄스홀 기본 가구 7개 전부 seed 통과(`seed: unknown furni` 0건).

### 지적받은 빈틈 → 수정
`RoomInstance.Seed` 가 **가구 id 존재만 확인**하고 벽 위치·슬롯 범위를 검사하지 않았다. 대화형 배치(`OnPlace`)는 검사하지만 seed 경로는 안 거치므로, 템플릿에 잘못된 벽 칸을 적으면 **로그 한 줄 없이 엉뚱한 곳에 렌더**된다.
**수정 방침 — 버리지 않고 경고 후 보정.** seed 는 저장본도 지나가므로 조용히 버리면 사용자가 놓은 가구가 사라진다.
- 벽이 없는 모서리 → 반대쪽 벽이 있으면 그쪽으로 dir 보정, 없으면 그대로 두고 경고.
- 슬롯 범위 밖 → 범위 안으로 clamp 후 경고. 기울기도 `ClampTilt`.
- 걸을 수 없는 타일의 바닥 가구 → 경고(그대로 둠).
- 추가로 `DancehallTemplateTests` — 템플릿 좌표가 실제로 벽이 있는 자리인지, 슬롯이 범위 안인지, 바닥 가구·스폰·문이 걸을 수 있는 타일인지 단위 테스트로 고정(방 JSON 을 고치면 이 표도 같이 고쳐야 함).

## 2026-09-17 (6차) — 떠오르는 채팅 · 문으로 방 이동 · 복권과 일일 용돈

`docs/reference-notes.md`(사용자가 공유한 회고 글 정리)에서 뽑은 세 가지.

### 1. 떠오르는 채팅 — 이 장르의 시각적 서명
원작은 하단 채팅창이 아니라 **친 말이 머리 위에서 하늘로 붕붕 떠올랐다.**
- `AvatarView.ShowBubble` 이 매번 말풍선 노드를 새로 만들고 트윈으로 위로 띄우며 서서히 지운다(4.2초 상승, 마지막 1.3초 페이드, 끝나면 `QueueFree`).
- 고정 노드가 아니라 매번 생성 → **여러 개가 자연스럽게 쌓인다**(새 말은 아래에서 시작, 먼저 친 말은 이미 올라가 있음).
- `Avatar.tscn` 의 `Bubble` 노드 제거, `_bubbleText`/`_bubbleSeq` 제거.
- 줄바꿈 판단은 폰트 측정 대신 **글자 수(>14)** 로 — 트리에 들어가기 전엔 테마 조회가 불안정.

### 2. 문 = 다른 방으로 (`DoorMarker.cs`)
원작은 선실 **문**을 눌러 남의 방에 갔다(메뉴가 아니라 공간 안 오브젝트).
- 문 타일에 자립형 아치 문을 그린다(문틀+하늘색 문짝+손잡이, 마우스 올리면 반짝임).
- 문 타일 좌클릭 → `DoorClicked` → HUD 가 방 목록 창을 연다. 호버 시 커서도 강조.
- `RoomDto.DoorX/DoorY` 를 그대로 사용(이미 오고 있던 값).

### 3. 복권 + 일일 용돈 — 루피를 **버는 곳**
지금까지 시작 500루피가 전부라 쓰기만 하고 벌 데가 없었다.
- `Harbor.Core/Lottery.cs` — 순수 함수 확률표(1등 0.2%/2등 1%/3등 5%/4등 본전 20%). 기대값 8.5 < 가격 10 → **돈이 빠져나가는 곳**. 테스트 11건.
- `C_LotteryDraw` → 10루피 차감 → 추첨 → `S_LotteryResult{Rank, Prize, Price, Message}`. HUD `[복권]` 창에서 뽑고 최근 결과 12건 표시. 1·2등은 토스트.
- **일일 용돈**: 그날 첫 로그인에 100루피(`UserSave.LastAllowance` = yyyy-MM-dd 로 하루 1회 보장). `S_LoginResult.Notice` 로 안내 문구 전달 → 입장 시 시스템 채팅 + 토스트.
- `EconomyOptions`: `LotteryDailyMax` 제거, `DailyAllowance`/`LotteryPrice`/`LotteryJackpot` 추가.
- `Session.RupeeAdd(delta)` 추가 — 읽고-쓰기 경합 없이 가감.

### 검증 (빌드 검증 에이전트)
- sln 0/0, 클라 0/0, 테스트 **24/24**(기존 13 + 복권 11), 헤드리스 e2e 정상(`furni=16`, 저장된 방/유저 복원, 예외 0).
- `Avatar.tscn` 에서 `Bubble` 노드를 뺐지만 **씬 오류 없음** — 남은 참조는 코드로 만드는 것뿐(`GetNode("Bubble")` 잔재 없음).
- ⚠ 이번 e2e 는 접속·입장만 했다. **떠오르는 말풍선 트윈과 복권 창은 런타임 미실행** — 컴파일·씬 오류 없음까지만 확인됨. 눈으로 볼 것.

## 2026-09-17 (5차) — 벽 방향 버그 수정 · 저장(영속) · 가구 10종 · 모양별 렌더

사용자 피드백: "포스트잇 방향 설정 가능한가? 고정되어 있을 때 벽이랑 방향 매치가 안 맞는 경우가 있어", "한글 채팅 잘 쳐진다", "좀 더 업데이트하자", "이미지 레퍼런스 넣어두면 괜찮을려나".

### 1. 벽 방향 버그 (진짜 원인)
벽면 기울기 부호가 **반대**로 들어가 있었다. 화면 좌표에서
- 서쪽 벽 = `left→top` = (+32, −16) → 기울기 **−0.5** (오른쪽으로 갈수록 위로)
- 북쪽 벽 = `top→right` = (+32, +16) → 기울기 **+0.5** (오른쪽으로 갈수록 아래로)

`DrawPostit` 의 `slope` 와 `DrawWallPanel` 의 `along` 이 둘 다 뒤집혀 있어, 한쪽 벽에서는 맞고 다른 쪽 벽에서는 어긋나 보였다. 부호를 바로잡음.
또한 `WallSlotAt` 이 `_wallSeg` 를 순회하며 **처음 맞는** 벽을 골랐다 → 화면에서 겹치는 벽이 있으면 뒤쪽 벽이 잡힐 수 있음. **x+y 가 가장 큰(앞쪽) 벽**을 고르도록 변경.

### 2. 기울기(사용자가 말한 "방향 설정")
벽에 붙이는 물건에 기울기 부여: 배치 모드에서 **R** 이 0° → −7° → 7° → −14° → 14° 순환(바닥 가구는 기존대로 회전).
`ItemDto.Tilt`(Key 15, sbyte) · `C_PlaceItem.Tilt`(Key 6) · `Walls.MaxTilt/ClampTilt`(±20° 서버 검증). 노드 `Rotation` 으로 적용해 종이만 삐뚜름하고 벽면 기울기는 `_Draw` 가 따로 맞춘다.

### 3. 저장 (영속 계층) — "나만의" 세상이 남도록
`SaveStore` (JSON, `saves/users.json` + `saves/rooms.json`, 실행 파일 폴더 기준) + `SaveService`(15초마다 flush, 종료 시 강제 flush).
- **유저**: 닉 → 루피 + 가방. `Session` 이 지갑/가방 변경 때마다 write-through(`Persist()`, `_eco` 락 안에서). 로그인 시 저장본 있으면 복원, 없으면 시작 지급(`RestoreEconomy`).
- **방**: 키 = 개인 `u:{닉}` / 공용 `t:{템플릿}`. 가구 배치·상태·포스트잇 본문·기울기까지 저장. `RoomInstance` 가 배치/줍기/상태전이/포스트잇쓰기 후 `SaveRoom()`(룸 루프 스레드에서만 → `_items` 안전).
- 기동 시 `RoomManager.RestoreSaved()` 로 **저장된 개인 방을 전부 되살림** → 주인이 접속 안 해도 방 목록에 보이고 놀러 갈 수 있다. `OwnerId` 는 그 유저가 로그인할 때 채워지므로 그전엔 아무도 못 꾸민다.
- 저장 파일이 깨지면 `.bad-<시각>` 으로 백업하고 빈 상태로 기동(서버가 못 뜨는 일 방지). 쓰기는 tmp → Move(원자적).
- 한계: 같은 닉 동시 접속 시 서로 덮어씀. 토큰 인증 들어오면 정리.

### 4. 가구 10종 추가 (총 16종)
바닥: 원형 탁자, 동그란 러그(안 막힘·밟고 지나감), 화분, 스탠드 조명(on/off), 브라운관 TV(on/off + 웃음), 책장.
벽: 둥근 창문, 사진 액자, 벽시계, 민트 포스트잇. 카테고리 `table`/`wallart` 추가(상점에서 한글 그룹명).

### 5. 모양별 렌더 (`FurniPalette.Shape`)
전부 같은 아이소 박스였던 걸 형태별로 분리: Box / Chair(다리·좌판·등받이, 등받이는 dir 반대쪽) / Table(타원 상판+다리) / Rug(바닥 타원 두 겹) / Plant(화분+잎 5개) / Lamp(기둥+갓, 켜면 발광 원) / Can(원통+띠) / Panel(에어컨) / Window(창틀+하늘+바다+해) / Frame(액자+언덕 그림) / Clock(문자판+시침/분침) / Postit(메모지+테이프+접힌 귀퉁이, 빈 메모는 줄만).

### 6. `art/ref/`
레퍼런스 이미지 넣는 폴더 + README(무엇을 넣으면 어디에 반영되는지, 원본 IP 에셋 금지).

### 7. 검증에서 잡힌 실제 결함 2건 (빌드 검증 에이전트)
1. **`SaveStore.cs` 에 `using Harbor.Server.Config;` 누락** → 서버 빌드 실패. 한 줄 추가.
2. **빈 방이 저장되지 않음** — `SaveRoom()` 호출처가 전부 *가구 변경* 시점(배치/줍기/상태전이/포스트잇쓰기)뿐이라, 가구를 한 번도 안 놓은 개인 방은 디스크에 기록되지 않았다.
   → 재시작하면 방이 사라지고 방 목록에도 안 뜸(= "주인이 접속 안 해도 놀러 갈 수 있다"는 의도가 깨짐).
   **수정**: `RoomInstance` 생성자에서 `OwnerNick` 이 있으면 즉시 `SaveRoom()`.
3. 부수 개선: `FlushSeconds` 15→3. 서버는 `taskkill /F` 로만 죽일 수 있어 `SaveService.StopAsync` 의 종료 flush 가 안 돈다 → **주기가 곧 최대 손실 구간**이므로 짧게.
4. 헤드리스 검증용 `HARBOR_SELFTEST=1`(`RoomView.RunSelfTest`): 입장 1.5초 뒤 의자+포스트잇 배치, 1초 뒤 메모 작성. 사람 손 없이 배치→저장→재시작→복원 경로를 통과시킨다.

### 최종 검증 결과 (2026-09-17)
- sln 0/0, 클라 0/0, 테스트 **13/13**.
- 2회 기동 e2e: 1회차 `저장된방=0 저장된유저=0` → 셀프테스트 배치 → `rooms.json` 에 `u:tester`(의자 + 포스트잇 `Tilt:-7`, `Extra:"저장 테스트 메모"`, `State:"written"`), `users.json` 에 가방 차감 반영(chair_wood 2→1, postit_yellow 3→2) → 2회차 `저장된방=1 저장된유저=1`.
- **한글 본문·기울기·상태가 JSON 왕복과 강제종료를 견딤.** 예외/스택트레이스 0.
- 남은 한계: 종료 시 flush 경로는 여전히 미검증(콘솔 Ctrl+C 가 필요). 주기 flush(3초)가 실질 보증.
- 서버 로그 파일은 **CP949** — UTF-8 로 읽으면 한글이 깨져 보인다(`iconv -f CP949`).

## 2026-09-17 (4차) — 자체 한글 입력기 + 벽 슬롯 그리드

### 채팅 1글자 문제 — 원인 확정
진단 로그(`chat_debug.log`): `ㅇ`, `ㅇㅇ`, `ㅇㅇㅇ`… 자모가 **낱개로 즉시 커밋**되고 조합(ㅇ+ㅏ→아)이 전혀 안 일어남. 포커스는 유지됨(focus=True). 즉 우리 코드가 아니라 Godot 창에서 Windows 한글 IME 조합이 깨지는 문제. 캐럿 완화로는 해결 불가.

**해결: `HangulInput.cs` — OS IME 를 쓰지 않는 자체 한글 입력 Control(두벌식 오토마타).**
- 키코드(QWERTY 자리)→자모, 초성/중성/종성 조합, 복모음(ㅘㅙㅚㅝㅞㅟㅢ)·겹받침(ㄳㄵㄶㄺㄻㄼㄽㄾㄿㅀㅄ), 받침이 다음 초성으로 넘어가기, Backspace 자모 단위 지우기, Shift 쌍자음/ㅒㅖ.
- 한/영: 오른쪽 Alt, Shift+Space, 입력창 오른쪽 배지 클릭. Enter 제출, ESC 포커스 해제, Ctrl+V 붙여넣기. 조합 중 글자는 노란색+밑줄.
- 닉네임(영문 기본)·채팅·포스트잇 본문(Multiline) 전부 교체. `Ui.Input`(LineEdit) 은 더 안 씀. 진단 로그·캐럿 완화 제거.
- 한계: 캐럿은 항상 끝(중간 편집 불가), 방향키 없음. 채팅용으론 충분.

### 벽 슬롯 그리드 (사용자: "벽걸이는 바닥 그리드가 아니라 벽 기준 그리드에")
- `Walls.SlotCols = 2`(타일 모서리를 가로 2칸), 세로 = 벽 높이(층) 칸 → 슬롯 (x, y, dir, u, v). `Walls.SlotValid`.
- 프로토콜: `C_PlaceItem.WallU/WallV`(Key 4,5), `ItemDto.WallU/WallV`(Key 13,14). 서버 `RoomItem.WallU/V`, 같은 슬롯 중복 금지, 슬롯 범위 검사(코드 23).
- 클라: `_wallSeg[(x,y,dir)] = (a,b)` 벽 아랫변 저장. `WallSlotAt(마우스)` = 벽 평행사변형 안의 (s,t) → (u,v). 벽걸이 배치 모드는 바닥이 아니라 **벽면 슬롯**을 가리키며 방향은 자동(R 불필요). 커서는 슬롯 사각형(`TileCursor.SetShape`). 벽걸이 노드 위치 = 슬롯 중심, 그림은 중심 기준 대칭(`_elevPx=0`).
- 일반 모드에서 벽걸이 위에 마우스 → 슬롯 강조, 클릭 = 포스트잇 읽기 / fsm 사용, 우클릭 = 줍기 (`WallItemAtMouse`).

### 검증 (빌드 검증 에이전트)
- sln 0/0, 클라 0/0 (`HangulInput.cs` 진단 없음), 테스트 12/12, 헤드리스 e2e 정상. 눈 확인은 아직.

## 2026-09-17 (3차) — 포스트잇 방명록 아이템 + 채팅 1글자 문제 완화

사용자 피드백: "채팅 입력은 여전히 1글자만 입력됨", "포스트잇은 아직?" → "아이템 포스트잇".

### 포스트잇 설계 (동물의 숲 느낌의 방명록)
- 상점 아이템(벽걸이, 10루피): `postit_yellow` / `postit_pink` (`interaction.type = "postit"`, flags `wall`). 시작 가방에 노란 포스트잇 3장.
- **남의 방에도 붙일 수 있다** (배치 권한 예외). 붙인 사람(`ItemDto.Author` = 닉)만 본문을 쓴다: `C_PostitWrite{ItemId, Body}` → 서버가 `S_ItemState{State="written", Extra=본문}` 방송. 200자.
- 누구나 클릭하면 읽기 창. 떼기는 방 주인(공용 방은 누구나) 또는 글쓴이. 뗀 포스트잇은 뗀 사람 가방으로.
- 내가 붙이면 바로 쓰기 창이 뜬다(`S_ItemAdd` 에서 Author==나 && Extra 비어있음).
- 렌더: 벽면 기울기에 맞춘 사각 메모지 + 테이프 + 접힌 귀퉁이 + 본문 첫 6자(`DrawString`, 6px). 이름 라벨은 "OO의 포스트잇".
- 프로토콜: `ItemDto.Author`(Key 12), `CatalogEntry.Interaction`(6), `InventoryEntry.Interaction`(4), `S_InventoryUpdate.Interaction`(4). `C_PostitWrite` 필드를 (ItemId, Body) 로 재정의. `C_PostitRead/S_PostitList` 는 미사용(본문이 ItemDto.Extra 로 오므로 불필요).
- 서버: `RoomItem.Author/IsPostit`, `OnPlace` 포스트잇 권한 예외, `OnPick` 글쓴이 허용, `OnPostitWrite`(코드 25 = 남의 포스트잇), `RoomCommand.PostitWrite`.
- 클라: `RoomView.PostitOpened` 이벤트, `WritePostit/PickItem/CanWrite/CanRemove`, 배치 모드 `_buildPostit`. `HudView` 창 `Win.Postit`(TextEdit 쓰기 / 카드 읽기 / 떼기).

### 채팅 1글자 문제
- 코드 경로(수신 큐 drain, `_UnhandledInput` Enter/ESC 만)에서는 포커스를 뺏는 곳이 없음. 웹 검색: Godot 한글 IME 캐럿 버그(조합 완료 후 캐럿이 끝으로 안 가서 다음 글자가 덮어씀) 보고 있음 → 증상과 일치 가능.
- **완화**: `TextChanged` 마다 캐럿을 끝으로(`SetCaretColumn` deferred) — 채팅/닉네임 입력.
- **진단 로그** 추가(원인 확정 후 제거): 포커스 변경(`GuiFocusChanged`), 채팅 `TextChanged`(text/len/caret/focus), `FocusExited` → 콘솔 exe 로 실행하면 보임.

### 검증 (빌드 검증 에이전트)
- sln 0/0, 클라 0/0, 테스트 12/12, 헤드리스 e2e 정상 (`rooms=2 furni=6`, 예외 없음). 이번엔 사용자 프로세스 안 죽임.
- 포스트잇·채팅 완화는 아직 눈으로 확인 전.

## 2026-09-17 (2차) — 벽 · 가방/상점/루피 · 방 목록(다른 사람 방)

사용자 피드백: "가구 4개 목록 대신 인벤토리/상점(루피)", "다른 사람 방으로 이동", "벽이 있어야 포스트잇을 붙인다", "채팅 입력이 잘 안 쳐진다(원인 확인 요청)".

### Core
- `Walls.cs`(서버·클라 공용): 뒤쪽 두 면만 벽. West(x,y) = (x'≤x-1, y'≤y) 영역에 걸을 수 있는 타일이 없을 때, North = (x'≤x, y'≤y-1). 2D prefix 로 O(1).
  문 타일처럼 앞으로 튀어나온 타일엔 벽이 안 생김(다른 타일을 가리므로). 벽걸이 규칙 `CanHang(x,y,dir)`: dir 4=북쪽 벽, 2=서쪽 벽. 테스트 3건 추가(총 12).

### 프로토콜
- `RoomDto` +WallHeight/OwnerId/OwnerNick/Kind, `ItemDto` +Wall, `S_LoginResult` +HomeRoomId.
- 신규: `RoomInfo`/`S_RoomList`/`C_RoomList`, `CatalogEntry`/`S_Catalog`/`C_Catalog`, `InventoryEntry`/`S_Inventory`/`C_Inventory`, `C_BuyCatalog{FurniId,Qty}`.
- **`S_InventoryUpdate.Qty 는 절대값(현재 보유 수량)** 으로 의미 변경 (+Name, +Wall). 이전엔 델타처럼 쓰였음.

### 서버 (경제는 메모리 — 영속 계층 전)
- `Session`: Rupee/가방(`InvAdd/InvTryTake/InvSnapshot`, lock — 세션 스레드와 룸 루프 양쪽에서 접근). `HomeRoomId`.
- `EconomyOptions` +`StarterItems`(appsettings: 의자2·콜라2·냉장고1) +`HomeTemplate`.
- 로그인: 닉 기준 개인 방 재사용(`RoomManager.FindHomeByNick`) 또는 생성("{닉}의 방"), 시작 루피/가방 지급, LoginResult(HomeRoomId)+Wallet+Inventory+Catalog 전송. 클라는 홈으로 입장.
- `RoomInstance`: Owner/Name/UserCount(Volatile). `CanEdit` = 공용이거나 주인. 배치: 권한(22) → 벽걸이면 벽 판정(23)+같은 벽 중복(21) / 바닥이면 기존 검사+유저 위 solid 금지 → 가방 차감(24). 줍기: 권한 + 가방 반환. give_item 도 가방에 실제 추가.
- 상점: `C_BuyCatalog` → 루피 차감(30) → Wallet + InventoryUpdate. 방 목록: 공용 → 내 방 → 남의 방 순.
- `FurniDef.Wall`(flags "wall") → Solid 아님. `aircon_wall.json` flags `solid`→`wall`.

### 클라이언트
- `RoomView`: WallLayer(바닥 뒤)에 벽 폴리곤(면·걸레받이·윗선, 스타일별 색: wood/rail/기본), 카메라 중앙 계산에 벽 높이 포함. 가방/카탈로그/지갑/방목록 상태+이벤트, `EnterRoom/RequestRoomList/Buy`. 배치 모드는 **가방에 있는 가구만**, 벽걸이는 dir 4↔2 토글, 고스트가 벽 모서리 중앙에 붙음(`ItemPos`). 다 놓으면 자동 종료. 남의 방에선 배치/줍기 차단 + 안내. 오류 코드 22/23/24/30/31 한글화.
- `HudView`: 우상단 지갑, 하단 [가방][상점][방 목록] 토글 창(우측 중앙 카드, ESC 닫기). 가방: 이름×수량+배치 토글, 상점: 카테고리별 가격+구매(부족 시 비활성), 방 목록: 내 방/공용/OO님의 방 + 인원 + 입장. 방 라벨에 소유 표시.
- `FurniSprite`: 벽걸이는 납작한 패널로 벽과 나란히 그림(`DrawWallPanel`).
- 채팅 입력 문제: 코드상 수신 처리는 프레임당 큐 drain 뿐이라 입력을 막지 않음. 증상(자모 분리 / 포커스 / 한글만) 확인 필요 → 사용자에게 질문한 상태.

### 검증 (빌드 검증 에이전트)
- sln 0/0, 클라 0/0, 테스트 12/12, 헤드리스 e2e 정상(connect/disconnect, 예외 없음).
- 주의: 검증 중 이전 서버 프로세스가 DLL 을 잠가 빌드가 실패해서 `taskkill` 했음 → 사용자가 띄워둔 서버/클라가 종료됨. 재실행 필요.
- Git Bash 에서 `taskkill /F` 가 `F:/` 로 바뀌는 문제: `MSYS_NO_PATHCONV=1` 또는 `//F`.

## 2026-09-17 — 팝플 UX/UI 1차: 시작 화면·HUD·자연스러운 조작·미니어처 placeholder

### 목표
"팝플 — 나만의 온라인 미니어처 세상"으로서 아트 없이도 *게임처럼* 느껴지게. 서버 프로토콜은 최소 확장, 나머지는 클라이언트 UI/UX.

### 프로토콜/서버 (작은 변경)
- `ItemDto` 에 `Name`(displayName)·`Solid`·`Interaction`(fsm/seat/none) 추가 `[Key(8..10)]`. 서버 `RoomItem.ToDto` 가 채움.
  이유: 클라가 가구 한글 이름을 표시하고, "클릭하면 사용할지/걸어갈지"를 판단하려면 정의 일부가 필요. 카탈로그 패킷 생기면 대체.

### 클라이언트 (Godot) — 새 파일
- `scripts/Ui.cs` — 디자인 토큰(palette48 기반 색) + 위젯 팩토리(Box/Card/Button/Input/Text). HUD 는 전부 코드로 조립(손으로 쓴 tscn export 미주입 이슈 회피, 토큰 한 곳 관리).
- `scripts/HudView.cs` — CanvasLayer HUD. 시작 패널(닉네임→입장, 연결 실패/끊김 안내+재시도) · 상단(방 이름·인원, 배치 모드 배지, 조작 힌트) · 하단(채팅 로그·입력, 감정표현 5종, 가구 배치 바 4종+배치 끝) · 토스트.
- `scripts/FurniPalette.cs` — 클라 임시 카탈로그(placeholder 크기/색/이름, 상태·동작 한글 배지). 인벤토리/카탈로그 패킷 전까지의 stopgap.
- `scripts/TileCursor.cs` — 호버 타일 다이아몬드(일반=노랑, 사용 가능 가구=진노랑, 배치 가능=초록/불가=빨강).
- `ui/theme.tres` — SystemFont(맑은 고딕→Noto Sans KR 폴백) 기본 테마. `project.godot` 에 `gui/theme/custom` 등록, 창 제목 "팝플 (Popple)".

### 클라이언트 — 수정
- `NetClient.cs` — 연결 실패는 예외(4초 타임아웃), 끊김은 `Disconnected` 이벤트(메인 스레드 1회, 세대 카운터로 의도적 Close 는 무시). 전송은 lock+동기 Write 로 순서 보장(이전엔 fire-and-forget WriteAsync 가 겹칠 수 있었음).
- `RoomView.cs` — Phase(Idle/Connecting/InRoom) + HUD 용 이벤트. 조작 모델:
  - 좌클릭 빈 타일 = 이동, **가구(fsm) 클릭 = 인접하면 사용, 멀면 가장 가까운 빈 인접 타일로 걸어간 뒤 도착 시 자동 사용**(`_pendingUse` + `AvatarView.Arrived`).
  - 우클릭 가구 = 줍기(`C_PickItem`). 예전의 "우클릭 빈 타일 = 냉장고 배치"는 제거 → 배치는 하단 가구 버튼으로 **배치 모드**(고스트 미리보기, R 회전, 우클릭/ESC 종료, 연속 배치 가능).
  - 휠 줌(1~4x), 휠 드래그 이동. 스냅샷 로드 시 방 중앙으로 카메라.
  - 높은 타일 클릭 판정 보정(`MouseTile`: 높이 후보를 위에서부터 검사).
  - 바닥: 체커 톤 + 문 타일 강조 + **단차/디오라마 받침 riser**(Polygon2D, x+y 순 정렬로 앞 타일이 뒤 riser 를 가림).
  - `S_Error` 코드 → 한글 토스트, `S_InventoryUpdate` → 획득 토스트, 입장/퇴장 시스템 채팅 라인.
  - 자동 입장: `HARBOR_AUTOJOIN=1` 환경변수 또는 `++ --autojoin` (헤드리스 e2e 용). 그 외엔 시작 화면.
- `AvatarView.cs` + `Avatar.tscn` — placeholder 를 `_Draw` 로 그림: 그림자·다리·몸통·머리·머리카락·눈(정면/측면/뒷모습 = 서버 dir), 걷기 흔들림, 춤/웃음 바운스, 손인사 팔, 잠(감은 눈), 앉기(낮은 자세). 닉 해시로 셔츠/머리색(모든 클라 동일). 말풍선은 PanelContainer(종이색 둥근 박스, 110px 넘으면 줄바꿈, 새 메시지가 오면 이전 타이머 무시). 동작 배지(♪♪/안녕!/ㅋㅋㅋ/z z Z…). 도착 이벤트 `Arrived`.
- `FurniSprite.cs` + `Furni.tscn` — placeholder 를 아이소 박스로: 윗면/좌/우면 + 외곽선, **앞면(dir 2=오른쪽면, 4=왼쪽면) 패널로 회전 방향 표시**, open/on 이면 윗면 발광, 벽걸이(에어컨)는 띄워서 지지선. 이름 라벨(서버 Name) + 상태 배지(열림/켜짐/빈 캔). 고스트 모드(반투명, 가능=초록/불가=빨강 틴트).

### 검증 결과 (빌드 검증 에이전트, 2026-09-17)
- `dotnet build Harbor.sln` 0 오류 / `HarborClient.csproj` 0 오류(경고 1 → `IsConnected`→`Online` 개명으로 해소) / 테스트 9/9.
- 헤드리스 e2e(`HARBOR_AUTOJOIN=1`, 15초): 클라 `[AssetCatalog] atlas.json 없음 — placeholder 모드`, 예외 없음. 서버 session 1 connected/disconnected 깨끗, `Enter failed` 없음.
- **눈으로 확인은 아직** — 창 띄워서 시작 화면·HUD·배치 모드·말풍선·자동 다가가기 확인 필요 (NEXT.md 체크리스트).

## 2026-09-16 — 서버 빌드 검증 + Godot 클라이언트 첫 기동

### 환경
- .NET SDK 9.0.301 (8.0.420 병존), `global.json` 없음 → net8.0 빌드 정상.
- 이 세션의 `!` 프리픽스는 **Git Bash**(`/usr/bin/bash`)로 실행됨(PowerShell 아님).
  `Tee-Object`·`Test-NetConnection` 불가 → `tee`·`/dev/tcp`·`taskkill //F //IM`로 대체.
- Godot 4.7.2 .NET(mono)을 winget으로 신규 설치(`GodotEngine.GodotEngine.Mono`).

### 검증 결과
- `dotnet restore/build/test` : 빌드 0 오류, 단위 테스트 **9/9 통과**(Iso 4, AStar 3, Fsm 1, Framing 1).
- 서버 기동: `[Harbor] rooms=2 furni=4`, `:30000` 리슨, TCP 세션 연결/해제 로그 정상.

### 수정 사항
1. **`src/Harbor.Server/Program.cs`** — content root + `Data:Root`를 `AppContext.BaseDirectory` 기준으로 고정.
   이유: `dotnet run --project`는 CWD가 프로젝트 폴더라 `appsettings.json`/`data/`를 못 찾아 `rooms=0`이 조용히 발생(`DefinitionStore.LoadDir`는 폴더 없으면 예외 없이 빈 dict 반환). VS F5 경로에서도 동일하게 해결됨.
2. **`src/Harbor.Protocol/Harbor.Protocol.csproj`** — MessagePack `2.5.187` → `2.5.302`.
   이유: NU1902/NU1903 취약성 경고 11건(높음 2건: GHSA-hv8m-jj95-wg3x, GHSA-vh6j-jc39-fggf). 두 어드바이저리 모두 patched=2.5.301 → 2.x 유지. 경고 11→0.
3. **`Harbor.sln`** — dotnet CLI로 재생성(원본 `Harbor.sln.bak`).
   이유: 손으로 만든 sln의 1행에 헤더가 바로 있어 VS가 열지 못함(정본은 1행 빈 줄). 4개 프로젝트 등록, `client-godot`은 의도적으로 제외.
4. **`client-godot/HarborClient.csproj`** — `Godot.NET.Sdk/4.3.0`→`4.7.2`, `<ImplicitUsings>enable</ImplicitUsings>` 추가.
   `project.godot` — `config/features "4.3"`→`"4.7"`.
5. **`client-godot/scripts/AssetCatalog.cs`** — `using FileAccess = Godot.FileAccess;` (ImplicitUsings로 들어온 `System.IO.FileAccess`와 CS0104 충돌 해소).

### 실제 버그 2건 (Godot이 아니라 프로토콜/씬 문제였음)
6. **MessagePack Key 중복** — `Packets.cs`의 `RoomDto`/`ItemDto`/`UserDto`에서
   `[Key(4)] public short DoorX, DoorY;` 처럼 한 줄 다중 필드 선언이 **같은 키를 공유**(C# 문법: 속성이 모든 선언자에 적용).
   → 서버가 `S_RoomSnapshot` 직렬화 시 `key is duplicated` 예외 → 스냅샷 미전송 → 클라 connect 직후 disconnect.
   해결: 세 DTO의 다중 필드를 각각 고유 `[Key(n)]`로 분리·재번호(클라/서버가 같은 소스 공유라 와이어 호환 무관).
7. **손으로 쓴 .tscn의 노드 export 미주입** — `FloorLayer = NodePath("FloorLayer")`가 C# `[Export] Node2D`에 자동 주입 안 됨 → `RoomView.LoadSnapshot`에서 `FloorLayer` null → NRE.
   해결: `RoomView`(FloorLayer/ObjectLayer/FloorTile/AvatarScene/FurniScene), `AvatarView.Bind`(Layers/NameLabel/Bubble), `FurniSprite.Bind`(Sprite/DebugLabel)에 `??=` 폴백(GetNode / GD.Load) 추가. export가 채워져 있으면 그대로 사용.

### 최종 e2e (헤드리스)
클라 로그 `[AssetCatalog] atlas.json 없음 — placeholder 모드` 출력 = 스냅샷 수신→바닥/아바타 생성까지 도달. 크래시 없음. 서버 `Enter failed` 없음.

### 수동 확인 — ✅ 완료
창 띄워 눈으로 확인: 주황 막대(tester) 스폰, 좌클릭 이동, 우클릭 가구 배치/사용(FSM closed↔open, 5초 복귀) 전부 정상 동작. MVP 코어 루프 완성.
