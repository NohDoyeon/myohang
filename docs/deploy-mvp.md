# 묘항 MVP 배포 — 10명 테스트용

> 목표: 오늘 GitHub 에 올리고, 지인 10명이 각자 PC 에서 접속해 볼 수 있는 상태를 만든다.
> 클라우드 이전은 사용성을 다듬은 **뒤에** 한다.

## 0. 무엇이 어디에 있는가

```
테스터 PC                      내 PC (상주)                         Vercel (선택)
Harbor 클라(exe) ──TCP 30000──▶ Harbor.Server.exe                   소개·다운로드 페이지
      ▲                         ├ 웹 8080 (랜딩·로그인·입장권)        (정적 HTML 만)
      └── 입장권 코드 ───────────┤ SQLite saves/harbor.db
                                └ 포트포워딩/터널로 외부 공개
```

### Vercel 에 게임 서버를 올릴 수 없는 이유 (구조적)

1. `Harbor.Server` 는 **TCP 30000 을 계속 물고 있는 상주 프로세스**다. Vercel 은 요청 단위로 뜨고 죽는 서버리스라 리스너를 유지할 수 없다.
2. 저장소가 **파일 기반 SQLite**(`saves/harbor.db`)다. 서버리스 파일 시스템은 요청이 끝나면 사라져 회원·방·원장이 남지 않는다.
3. **입장권(`TicketStore`)이 게임 서버와 같은 프로세스 메모리에 있다.** 로그인 API 만 Vercel 로 떼면 발급한 입장권을 게임 서버가 알지 못해 교환이 실패한다.

덧붙여 **브라우저에서 바로 플레이하는 것도 지금 구조로는 불가능**하다 — Godot 의 .NET 빌드는 웹 내보내기를 지원하지 않고, 브라우저는 raw TCP 를 열 수 없다.
웹 플레이를 하려면 클라이언트를 GDScript 로 다시 쓰고 서버에 WebSocket 을 붙여야 한다(별도 프로젝트 규모).

→ **Vercel 은 "소개 + 다운로드" 페이지까지.** 게임은 내 PC 서버 + 데스크톱 클라이언트.

---

## 1. GitHub 에 올리기

먼저 무엇이 올라가는지 **눈으로 확인**한다. `saves/` 나 `*.db` 가 목록에 보이면 안 된다 — 계정 비밀번호 해시가 들어 있다.

```
! cd /c/Users/User/Desktop/harbor && git init -b main && git add -A && git status --short | grep -Ei "saves|\.db" ; echo "---- 위에 아무것도 없으면 OK ----"; git status --short | wc -l
```

이상 없으면 커밋하고 올린다(저장소는 **비공개**로 시작하는 것을 권한다):

```
! cd /c/Users/User/Desktop/harbor && git commit -q -m "묘항 MVP — 서버·클라이언트·방 템플릿·경제" && git remote add origin https://github.com/<계정>/<저장소>.git && git push -u origin main
```

- 이미 `git` 저장소였다면 `git init` 은 생략한다.
- **한 번이라도 `harbor.db` 를 커밋한 적이 있으면** 히스토리에 남는다. `git log --all --oneline -- "*.db"` 로 확인하고, 있으면 테스트 계정 비밀번호를 전부 새로 만드는 편이 빠르다.

## 2. 클라이언트 exe 만들기

테스터에게 Godot 을 설치하게 할 수는 없으므로 **내보내기(export)** 가 필요하다. 이 프로젝트엔 아직 `export_presets.cfg` 가 없다 — 에디터에서 한 번 만들어야 한다.

1. 에디터 열기
   ```
   ! "C:\Users\User\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "C:\Users\User\Desktop\harbor\client-godot" --editor
   ```
2. **Editor → Manage Export Templates** 에서 4.7.2 mono 템플릿 내려받기 (한 번만, 용량 큼)
3. **Project → Export → Add… → Windows Desktop** → 경로를 `export/myohang.exe` 로 지정 → Export Project
4. 산출물(`myohang.exe` + `.pck` + .NET dll 들)을 통째로 zip
5. `export_presets.cfg` 는 커밋해 두면 다음부터 3번만 누르면 된다 (`.gitignore` 는 `export/` 산출물만 제외한다)

테스터 PC 에는 `harbor://` 프로토콜이 등록돼 있지 않으므로, **웹에서 [코드 복사] → 게임 시작 화면 비밀번호 칸에 붙여넣기**가 기본 경로다.
원하면 `tools/register-url-scheme.bat` 를 zip 에 같이 넣어 준다(관리자 권한 불필요, HKCU 에 등록).

> **입장권 코드에는 서버 주소가 함께 실린다** (`내주소:30000/a1b2…`). 2026-09-18 에 넣은 변경으로,
> 이게 없으면 테스터의 클라이언트가 `127.0.0.1` = 자기 자신에게 접속을 시도한다.

## 3. 서버를 바깥에 열기

| 방법 | 장점 | 단점 |
|---|---|---|
| **공유기 포트포워딩 + DDNS** | 가장 단순하고 지연이 적다. 8080·30000 둘 다 한 번에 | 공유기 설정 필요, 공인 IP 가 드러남 |
| 게임용 TCP 터널 (playit.gg 등) | 공유기를 못 만질 때 | 지연 추가, 무료 조건이 바뀔 수 있음 |
| Cloudflare Tunnel | 웹(8080)은 무료·간단 | **TCP(30000)는 테스터 PC 에도 cloudflared 가 필요** → 게임 포트에는 부적합 |

10명 규모면 **포트포워딩 하나로 8080 + 30000 을 여는 것**이 가장 빠르다.

안전 메모:
- 비밀번호는 PBKDF2-SHA256 12만 회 + 계정별 소금으로 저장된다. 평문은 어디에도 없다.
- 같은 닉 동시 접속은 막혀 있다.
- **로그인 시도 횟수 제한은 없다.** 테스트하는 시간에만 포트를 열고, 끝나면 닫는 것을 권한다.

## 4. Vercel (선택)

`web/index.html` 을 그대로 올리면 **작동하지 않는다** — `/api/login`, `/api/status` 가 없기 때문이다.
Vercel 에는 다음만 올린다:

- 게임 소개 · 스크린샷
- 클라이언트 **다운로드 링크** (GitHub Releases 가 편하다)
- "서버 접속하기" 버튼 → 내 서버의 랜딩 주소(`http://<내주소>:8080`)로 이동

별도 폴더(예: `landing/`)로 두고 Vercel 프로젝트 루트를 거기로 지정하면, 게임 서버가 서빙하는 `web/` 과 섞이지 않는다.

## 5. 운영 (테스트 기간)

**백업** — 서버를 끄고 복사하는 것이 가장 안전하다. WAL 모드라 `-wal` 파일도 같이 챙긴다.

```
! cd src/Harbor.Server/bin/Debug/net8.0 && mkdir -p ../../../../../backup && cp saves/harbor.db* "../../../../../backup/harbor-$(date +%F-%H%M).db.bak" 2>/dev/null; ls -la ../../../../../backup | tail -5
```

알아둘 것:
- 저장은 3초마다 flush → 강제 종료 시 **최대 3초 분량**만 손실된다.
- 계정 삭제·비밀번호 재설정 기능은 **없다.** 테스터가 비밀번호를 잊으면 DB 에서 그 행을 지워 새로 만들게 해야 한다.
- 서버 로그 인코딩이 실행마다 다르다(CP949/UTF-8). 한글이 깨져 보이면 `iconv -f CP949`.

## 6. 테스터를 부르기 전 점검

- [ ] **다른 PC 에서 접속 성공** — 웹 로그인 → 코드 복사 → 클라 붙여넣기 → 입장
- [ ] 동시 5명 이상이 부둣가 광장(정원 60)에서 채팅·이동
- [ ] 서버 재시작 후 방·가구·루피·출석이 그대로
- [ ] 백업 한 번 만들어 보기
- [ ] 클라 zip 을 **내 PC 가 아닌 곳**에서 압축 풀어 실행 (dll 누락 확인)
