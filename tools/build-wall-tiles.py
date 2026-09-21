"""벽 원본 그림 → 게임용 **반복 무늬 타일**(32×48).

    py tools/build-wall-tiles.py art/ref/myohang_walls_5pack

산출물은 `webapp/public/art/wall/wall_*.png`. 파일명은 그대로 유지된다.

## 왜 이런 처리가 필요한가

받는 그림은 보통 **벽 한 판 전체**다 — 윗면 두께, 왼쪽 옆면 두께, 걸레받이까지 그려져 있다.
그런데 게임은 벽을 **타일 한 칸(32px)씩 이어 붙여** 그리므로, 그대로 반복하면 옆면 두께가
32px마다 나타나 세로줄이 죽 생긴다. 그래서 **정면 무늬만** 잘라낸다.

## 격자 복원을 하지 않는 이유

`build-avatar-atlas.py` 는 '확대된 도트'의 격자를 복원한다(한 칸이 8~17px). 벽 그림은 그게 아니라
**처음부터 고해상도로 그려진** 그림이었다 — 최빈 런 길이가 2~6으로 흩어져 복원할 격자가 없었다
(2026-09-21 확인). 그래서 LANCZOS 로 줄이고 색을 16개로 줄여 도트 느낌을 되살린다.

## 높이를 48로 맞추는 이유

벽 높이는 3칸 = 96px 다. 타일이 48이면 **정확히 2번** 반복된다. 40 같은 값이면 2.4번이라
벽 중간에 이음매가 보인다.

## 다음에 그림을 요청할 때

아래 한 줄을 넣으면 이 스크립트 자체가 필요 없어진다:
  "벽면의 무늬만 그려 줘. 윗면·옆면 두께와 걸레받이는 그리지 말 것.
   직사각형 안에 무늬만 채우고, 상하좌우로 이어 붙여도 이음매가 보이지 않게."
기울기는 렌더러가 준다(`skew.y = atan(0.5)`) — **평평한 직사각형으로 받는 것이 낫다.**
"""
import pathlib
import sys

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "webapp/public/art/wall"

TARGET_W, TARGET_H = 32, 48
COLORS = 16

# 잘라낼 비율. 그림마다 두께가 달라 맞지 않으면 여기를 조정한다.
CUT_LEFT = 0.22      # 왼쪽 옆면 두께
CUT_RIGHT = 0.03
CUT_TOP = 0.17       # 윗면 두께
CUT_BOTTOM = 0.20    # 걸레받이 (0.10 이면 회색 띠가 남았다)


def opaque_box(im):
    px = im.load()
    w, h = im.size
    xs = [x for x in range(w) if any(px[x, y][3] > 0 for y in range(0, h, 3))]
    ys = [y for y in range(h) if any(px[x, y][3] > 0 for x in range(0, w, 3))]
    if not xs or not ys:
        raise SystemExit("완전히 투명한 그림입니다")
    return xs[0], ys[0], xs[-1], ys[-1]


def top_edge(im, x):
    px = im.load()
    for y in range(im.size[1]):
        if px[x, y][3] > 0:
            return y
    return None


def convert(path: pathlib.Path) -> tuple[str, float]:
    im = Image.open(path).convert("RGBA")
    x0, y0, x1, y1 = opaque_box(im)
    width = x1 - x0 + 1

    # 윗변의 기울기. 정면 구간에서 잰다(왼쪽 옆면을 피해 20%~95%).
    ax, bx = x0 + int(width * 0.20), x0 + int(width * 0.95)
    ay, by = top_edge(im, ax), top_edge(im, bx)
    slope = (by - ay) / (bx - ax) if ay is not None and by is not None and bx > ax else 0.0

    # 기울기 펴기 — 열마다 밀어 평행사변형을 직사각형으로. 이걸 안 하면 렌더러에서
    # 다시 기울일 때 **기울기가 두 번** 걸려 무늬가 주저앉는다.
    flat = Image.new("RGBA", im.size, (0, 0, 0, 0))
    for x in range(im.size[0]):
        flat.paste(im.crop((x, 0, x + 1, im.size[1])), (x, -int(round(slope * (x - x0)))))

    fx0 = x0 + int(width * CUT_LEFT)
    fx1 = x1 - int(width * CUT_RIGHT)
    _, fy0, _, fy1 = opaque_box(flat)
    fh = fy1 - fy0
    face = flat.crop((fx0, fy0 + int(fh * CUT_TOP), fx1, fy1 - int(fh * CUT_BOTTOM)))

    # 32:48 비율로 **잘라서** 맞춘다 — 늘리면 무늬가 찌그러진다.
    want = TARGET_W / TARGET_H
    if face.width / face.height > want:
        nw = int(face.height * want)
        off = (face.width - nw) // 2
        face = face.crop((off, 0, off + nw, face.height))
    else:
        nh = int(face.width / want)
        off = (face.height - nh) // 2
        face = face.crop((0, off, face.width, off + nh))

    small = face.resize((TARGET_W, TARGET_H), Image.LANCZOS)
    small = small.convert("RGB").quantize(colors=COLORS, method=Image.MEDIANCUT).convert("RGBA")

    OUT.mkdir(parents=True, exist_ok=True)
    small.save(OUT / path.name)
    return path.name, slope


def main():
    src = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "art/ref/myohang_walls_5pack")
    if not src.is_absolute():
        src = ROOT / src
    files = sorted(src.glob("wall_*.png"))
    if not files:
        raise SystemExit(f"{src} 에 wall_*.png 가 없습니다")

    for p in files:
        name, slope = convert(p)
        # 2:1 아이소메트릭이면 -0.5 가 나온다. 크게 다르면 그림 각도가 우리와 안 맞는 것이다.
        flag = "" if abs(slope + 0.5) < 0.06 else "  <- 각도가 2:1 이 아닙니다"
        print(f"{name:18s} 기울기 {slope:+.3f} -> {TARGET_W}x{TARGET_H}{flag}")

    print(f"\n{len(files)}장을 {OUT} 에 넣었습니다.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
