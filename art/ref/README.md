# 레퍼런스 이미지 넣는 곳

여기에 그림을 넣고 "art/ref 에 넣었어" 라고만 말하면 됩니다. 제가 직접 열어 보고 색·비율·선 굵기·연출을 맞춥니다.
(PNG / JPG 를 읽을 수 있습니다. 파일명은 아무거나 괜찮지만 아래처럼 나눠 두면 더 정확합니다.)

## 특히 도움 되는 것
| 파일명 예시 | 무엇을 보나 |
|---|---|
| `room_*.png` | 방 전체 분위기 — 벽 높이, 바닥 톤, 카메라 각도, 여백 |
| `avatar_*.png` | 캐릭터 비율(머리:몸), 눈·머리 모양, 서기/걷기/앉기 자세 |
| `furni_*.png` | 가구 한 개의 크기감, 면 밝기(윗면/좌/우), 외곽선 유무 |
| `wall_*.png` | 벽에 붙는 것(포스트잇·액자·창문)이 벽면에 어떻게 얹히는지 |
| `ui_*.png` | 창·버튼·아이콘 스타일, 폰트 느낌, 모서리 둥글기 |
| `palette_*.png` | 쓰고 싶은 색 (이 그림에서 색을 뽑아 `art/palette48.json` 을 갱신) |

## 주의
- **원본 IP 의 실제 에셋은 넣지 않습니다.** (`LEGAL.md`) 분위기 참고용 스크린샷이라면 이 폴더 안에서만 보고, 따라 그리지 않고 우리 색·우리 비율로 재해석합니다.
- 직접 그린 그림·무료 라이선스 에셋이면 출처를 `art/licenses/` 에 적어 주세요.

## 이 그림이 실제로 반영되는 곳
- 색 → `art/palette48.json`, `client-godot/scripts/Ui.cs` (디자인 토큰)
- 가구 크기/색 → `client-godot/scripts/FurniPalette.cs`
- 캐릭터 그리기 → `client-godot/scripts/AvatarView.cs` 의 `_Draw`
- 가구 그리기 → `client-godot/scripts/FurniSprite.cs` 의 `_Draw`
- 나중에 실제 스프라이트가 생기면 → `client-godot/art/atlas.png` + `atlas.json` (자동 교체)
