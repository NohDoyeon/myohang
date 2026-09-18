# 묘항 (Myohang) — 고양이들의 항구 마을

> 코드명 Harbor. 제품명 **묘항** = 고양이 묘(猫) + 항구 항(港).

2000년대 "개인 공간 꾸미기 + 채팅" 커뮤니티 게임 장르를 **원본 IP와 무관하게** 재창조하는 프로젝트.
원본 명칭·로고·캐릭터·에셋·프로토콜은 일절 사용하지 않는다. (`LEGAL.md` 참고)

**세계관**: 두 발로 다니는 고양이들이 사는 항구 마을. 각자 다락방을 꾸미고, 이웃집에 놀러 가 벽에 쪽지를 남기고,
부둣가에 모여 논다. 장르 레퍼런스 정리는 `docs/reference-notes.md`, 주제 변경 근거는 `docs/theme-proposal.md`.

## 구성
```
Harbor.sln
├─ src/Harbor.Core        투영(Iso)·Heightmap·A*·ItemFsm  — 엔진 비종속
├─ src/Harbor.Protocol    Opcode·MessagePack 패킷·프레이밍  — 서버/클라 공용
├─ src/Harbor.Server      .NET 8 TCP 서버 (룸 단일스레드 루프, JSON 정의 로더)
├─ tests/Harbor.Core.Tests xunit (투영 왕복, A* 코너컷, FSM, 프레이밍)
├─ data/rooms, data/furni  콘텐츠 정의 JSON (코드 수정 없이 추가)
├─ client-godot/          Godot 4.7 C# 클라이언트 (씬 .tscn 포함, placeholder 렌더 동작)
├─ tools/                 Aseprite 배치 익스포트, 에셋 QA 스크립트
└─ art/                   palette48.json, licenses/ 대장
```

## 빌드 / 실행
```bash
dotnet restore
dotnet test                              # Core/Protocol 단위 테스트
dotnet run --project src/Harbor.Server   # :30000 리슨, data/ 자동 로드
```
설정: `src/Harbor.Server/appsettings.json` (포트·틱·경제 파라미터).

## Godot 클라이언트 (Godot 4.7 .NET/mono)
씬(`Main/Avatar/Furni.tscn`)·Autoload·placeholder 리소스는 이미 포함됨. 서버 먼저 띄우고:
```
Godot_v4.7.2-stable_mono_win64.exe --path client-godot            # 게임 바로 실행
Godot_v4.7.2-stable_mono_win64.exe --path client-godot --editor   # 에디터에서 F5
```
> 노드 export는 스크립트가 `??=` 폴백으로 자체 연결하므로 인스펙터 수동 연결 불필요.
> 실행/이어가기 상세는 **`NEXT.md`**, 이력은 **`WORKLOG.md`**.

조작: 좌클릭 이동 · 우클릭 빈 타일 = 냉장고 배치 · 우클릭 가구 = 사용(FSM).
에셋이 없으면 가구는 **아틀라스 키 텍스트**로 표시된다 → 아트 제작 전에도 전체 루프 검증 가능.

## 프로토콜 요약
`[uint32 len][uint16 opcode][MessagePack]` / opcode 는 `Harbor.Protocol/Opcode.cs`.
룸 상태 변경은 전부 `RoomCommand` → 룸 루프(단일 소비자)에서만 수행.

## 로드맵 (MVP)
- [x] 로그인·입장·이동·채팅·가구 배치/사용(FSM 타이머)
- [x] 시작 화면 · HUD · 배치 모드 · 미니어처 placeholder 렌더
- [x] 벽 렌더 + 벽 슬롯 그리드(벽걸이 가구는 벽 칸에 붙는다)
- [x] 인벤토리·카탈로그·지갑(루피) — 서버 메모리 + JSON 저장
- [x] 개인 방 + 방 목록으로 다른 사람 방 놀러 가기
- [x] 포스트잇 벽 방명록 (남의 방에도 붙이고, 누구나 읽기)
- [x] 자체 한글 입력기 (`HangulInput` — Godot 한글 IME 우회)
- [x] 떠오르는 채팅 · 문 클릭으로 다른 방 가기
- [x] 복권 + 일일 용돈 (루피를 버는 곳)
- [x] 외모 꾸미기(머리·피부·옷·모자) · 댄스홀 · 무한 콜라 자판기
- [x] 계정(비밀번호) · 웹 랜딩 페이지 · 로그인 → 일회용 입장권 → 게임 접속
- [x] 영속 계층 SQLite + Dapper (account / inventory / room / room_item)
- [ ] 루피 원장 `currency_log` (누가·얼마·왜·잔액)
- [ ] 가위바위보·낚시 미니게임
- [ ] 펫(사료로 유지비) · 커플 룸 · 결혼
- [ ] 팔레트 스왑 셰이더 + 아틀라스 파이프라인 연결

장르 레퍼런스 정리는 `docs/reference-notes.md`, 그림 참고는 `art/ref/`.

## 저장
지갑·가방·외모·비밀번호·방 꾸밈은 **SQLite** `src/Harbor.Server/bin/Debug/net8.0/saves/harbor.db` 에 남는다.
메모리가 진실이고 3초마다 바뀐 행만 쓴다. 초기화하려면 그 폴더를 지운다. 위치는 `appsettings.json` 의 `Save:Root`.
예전 `users.json`/`rooms.json` 이 있으면 첫 기동에 자동으로 DB 로 옮기고 `.imported` 로 이름을 바꾼다.

## 웹 (랜딩 + 로그인)
서버를 켜면 게임(:30000)과 웹(:8080)이 같은 프로세스에서 함께 뜬다.
`http://localhost:8080` 에서 가입·로그인하면 5분짜리 일회용 **입장권**을 받고, [게임 시작]이 `harbor://<코드>` 로 클라이언트를 띄운다.
프로토콜 등록은 `tools/register-url-scheme.bat` (해제는 `unregister-url-scheme.bat`). 등록하지 않았다면 코드를 복사해 게임 시작 화면의 비밀번호 칸에 붙여넣으면 된다.
비밀번호는 게임 서버로 넘어가지 않는다.
