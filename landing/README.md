# landing/ — Vercel 에 올리는 공개 페이지

게임 서버가 서빙하는 `web/index.html` 과 **다른 물건**이다. 헷갈리면 사고가 난다.

| | `web/index.html` | `landing/index.html` |
|---|---|---|
| 누가 서빙하나 | **게임 서버**(:8080) | **Vercel**(공개 URL) |
| 하는 일 | 로그인 → 일회용 입장권 → 게임 실행 | 소개 · 다운로드 · 개발 현황 |
| API 호출 | `/api/login`, `/api/status` (같은 프로세스) | **없음** |

## 왜 나눴나

`web/index.html` 을 그대로 Vercel 에 올리면 로그인이 죽는다. 이유 둘:

1. Vercel 에는 `/api/login` 이 없다 — 그 API 는 게임 서버 프로세스 안에 있고, 입장권(`TicketStore`)도 거기 메모리에 있다.
2. 올려도 **https 페이지에서 http 서버를 호출할 수 없다**(브라우저의 mixed content 차단).

로그인까지 웹으로 옮기려면 먼저 **입장권을 HMAC 서명 토큰으로** 바꿔야 한다(`docs/deploy-mvp.md` · `docs/platform-plan.md` 2단계).
그러면 브라우저는 Vercel 만 호출하고, 게임 클라이언트만 게임 서버에 TCP 로 붙는다.

## Vercel 설정

저장소를 Import 한 뒤:

| 항목 | 값 |
|---|---|
| Framework Preset | **Other** (React/Next 아니어도 된다) |
| Root Directory | **`landing`** |
| Build Command | 비움 |
| Output Directory | 비움 |

`landing/` 안의 파일을 그대로 서빙한다. `main` 에 푸시할 때마다 자동 배포된다.

## 손댈 때 주의

- **게임 서버 주소·IP 를 여기 적지 않는다.** 공개 페이지다.
- 다운로드 링크는 GitHub Releases 를 쓴다(클라이언트 zip 이 준비되면 버튼의 `disabled` 를 풀고 링크를 넣는다).
- 이미지를 쓰려면 `landing/` 안에 둔다. Root Directory 가 `landing` 이라 바깥 파일은 서빙되지 않는다.
