#!/usr/bin/env bash
# 테스터에게 줄 클라이언트 zip 을 만든다. 내보내기부터 압축까지 한 번에.
#
#   bash tools/pack-client.sh
#
# 산출물: myohang-client.zip (저장소 루트, .gitignore 로 추적 제외)
#
# 왜 스크립트로 두는가: 에디터에서 손으로 내보내면 매번 같은 실수를 한다.
#
# 아틀라스 프레임 표는 이제 **C# 상수**(art/AtlasMeta.cs)로도 들어가 있어서, pck 에 atlas.json 이
# 빠져도 그림은 정상으로 나온다. 그 전에는 같은 설정으로 내보내도 들어갔다 말았다 했고,
# 빠지면 크래시가 아니라 **고양이가 네모로 나오는 조용한 실패**였다 → WORKLOG 17차.
# 그래서 여기서는 파일 목록이 아니라 **dll 이 제대로 들어갔는지**를 본다.

set -euo pipefail
cd "$(dirname "$0")/.."

PROJECT="$PWD/client-godot"
OUT="$PROJECT/export"
ZIP="$PWD/myohang-client.zip"

# Godot 은 버전이 오르면 경로가 바뀐다 → 고정 경로를 먼저 보고, 없으면 찾는다.
GODOT="$LOCALAPPDATA/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe"
if [ ! -f "$GODOT" ]; then
  GODOT=$(find "$LOCALAPPDATA/Microsoft/WinGet/Packages" -name 'Godot_v*_mono_win64.exe' ! -name '*console*' 2>/dev/null | sort -r | head -1)
fi
[ -n "${GODOT:-}" ] && [ -f "$GODOT" ] || { echo "[!] Godot 실행 파일을 못 찾았습니다."; exit 1; }

echo "== 1/4  내보내기 =="
rm -rf "$OUT"
mkdir -p "$OUT"          # Godot 은 이 폴더를 만들어 주지 않는다 (없으면 "경로가 존재하지 않습니다")
"$GODOT" --headless --path "$PROJECT" --export-release "Windows Desktop" "$OUT/myohang.exe" 2>&1 | tail -3

echo "== 2/4  검사 =="
[ -f "$OUT/myohang.exe" ] || { echo "[!] myohang.exe 가 없습니다."; exit 1; }
[ -f "$OUT/myohang.pck" ] || { echo "[!] myohang.pck 가 없습니다."; exit 1; }

# 아틀라스 표가 들어 있는 dll. 이게 없으면 게임이 아예 안 뜬다.
DLL=$(find "$OUT" -name 'HarborClient.dll' | head -1)
[ -n "$DLL" ] || { echo "[!] HarborClient.dll 이 없습니다 → .NET 빌드가 빠졌습니다."; exit 1; }
# .NET 은 문자열 상수를 **UTF-16 으로** 저장한다(`a\0v\0a\0…`) → 그냥 grep 하면 있어도 못 찾는다.
# 널바이트를 걷어내고 보면 평범한 ASCII 가 된다. `grep -P` 는 이 로케일에서 거부당하므로 쓰지 않는다.
FRAMES=$(tr -d '\000' < "$DLL" | grep -ac "avatar/hd/" || true)
[ "$FRAMES" -gt 0 ] \
  || { echo "[!] dll 에 아틀라스 프레임 표가 없습니다 → 고양이가 네모로 나옵니다."; \
       echo "    py tools/build-avatar-atlas.py 로 art/AtlasMeta.cs 를 다시 만드세요."; exit 1; }

echo "   pck $(stat -c%s "$OUT/myohang.pck") bytes · dll $(stat -c%s "$DLL") bytes · 프레임 키 ${FRAMES}개"

echo "== 3/4  테스터용 파일 동봉 =="
cp tools/client/register-url-scheme.ps1   "$OUT/"
cp tools/client/register-url-scheme.bat   "$OUT/"
cp tools/client/unregister-url-scheme.bat "$OUT/"
cp tools/client/읽어주세요.txt            "$OUT/"

echo "== 4/4  압축 =="
rm -f "$ZIP"
/c/Windows/System32/tar.exe -a -c -f "$ZIP" -C "$OUT" .    # Git Bash 에는 zip 이 없다
ls -lh "$ZIP"
echo
echo "다음: 이 zip 을 GitHub Releases 에 올리고, 랜딩의 다운로드 링크를 거기로 건다."
