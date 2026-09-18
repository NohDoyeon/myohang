"""투명 배경 스프라이트 시트를 컷별 PNG 로 자동 분할한다.

    py tools/cut-sheet.py art/ref/고양이시트.png art/out/avatar

칸이 정확한 격자가 아니어도 된다 — **알파(투명도)로 그림 덩어리를 찾아** 각각의 경계 상자를 잘라낸다.
행으로 묶고(위→아래), 행 안에서 왼쪽→오른쪽으로 정렬한 뒤 아래 NAMES 순서대로 이름을 붙인다.

⚠ **배경이 투명해야 한다.** 라벨·그림자·배경 그라데이션이 있으면 전부 한 덩어리로 잡힌다.
   (디자인 시트는 보통 배경이 있으므로, 같은 그림을 '투명 배경·글자 없음'으로 다시 받아야 한다.)
"""
import sys, pathlib
from PIL import Image

# 고양이 캐릭터 시트의 순서. 시트가 바뀌면 이 목록도 같이 고친다.
# 이름은 게임이 찾는 아틀라스 키와 같다: avatar/hd/{모델}_{방향}_{동작}_{프레임}
NAMES = (
    [f"001_{d}_stand_0" for d in range(5)] +
    [f"001_{d}_walk_{f}" for d in range(5) for f in range(2)] +
    [f"001_{d}_sit_0" for d in range(5)] +
    [f"001_{d}_sleep_0" for d in range(5)] +
    ["001_4_dance_0"]
)

ALPHA_MIN = 8        # 이보다 투명한 픽셀은 없는 것으로 친다
GAP = 6              # 이만큼 떨어져 있으면 다른 그림으로 본다(귀·꼬리가 끊겨 보이지 않게 넉넉히)


def islands(im):
    """알파가 있는 픽셀 덩어리들의 경계 상자를 찾는다 (flood fill, 인접 GAP 픽셀까지 같은 덩어리)."""
    w, h = im.size
    px = im.load()
    seen = [[False] * h for _ in range(w)]
    boxes = []
    for sx in range(w):
        for sy in range(h):
            if seen[sx][sy] or px[sx, sy][3] < ALPHA_MIN:
                continue
            stack, minx, miny, maxx, maxy = [(sx, sy)], sx, sy, sx, sy
            seen[sx][sy] = True
            while stack:
                x, y = stack.pop()
                minx, miny = min(minx, x), min(miny, y)
                maxx, maxy = max(maxx, x), max(maxy, y)
                for dx in range(-GAP, GAP + 1):
                    for dy in range(-GAP, GAP + 1):
                        nx, ny = x + dx, y + dy
                        if 0 <= nx < w and 0 <= ny < h and not seen[nx][ny] and px[nx, ny][3] >= ALPHA_MIN:
                            seen[nx][ny] = True
                            stack.append((nx, ny))
            if (maxx - minx) >= 8 and (maxy - miny) >= 8:      # 점 하나짜리 잡티는 버린다
                boxes.append((minx, miny, maxx + 1, maxy + 1))
    return boxes


def rows(boxes):
    """세로 위치가 겹치면 같은 행으로 묶고, 행 안에서 왼쪽→오른쪽 정렬."""
    out = []
    for b in sorted(boxes, key=lambda b: b[1]):
        for row in out:
            ry0 = min(x[1] for x in row); ry1 = max(x[3] for x in row)
            if b[1] < ry1 and b[3] > ry0:                      # 세로로 겹친다 = 같은 행
                row.append(b); break
        else:
            out.append([b])
    for row in out:
        row.sort(key=lambda b: b[0])
    return out


def main():
    if len(sys.argv) < 3:
        print(__doc__); return 1
    src = pathlib.Path(sys.argv[1])
    dst = pathlib.Path(sys.argv[2])
    dst.mkdir(parents=True, exist_ok=True)

    im = Image.open(src).convert("RGBA")
    boxes = islands(im)
    grouped = [b for row in rows(boxes) for b in row]
    print(f"{src.name}: 그림 {len(grouped)}개 찾음 (이름 {len(NAMES)}개 준비됨)")

    if len(grouped) != len(NAMES):
        print("⚠ 개수가 맞지 않는다. 배경이 투명한지, 라벨·글자가 지워졌는지 확인할 것.")
        print("  찾은 상자:", [(b[2] - b[0], b[3] - b[1]) for b in grouped][:40])
        return 1

    for name, (x0, y0, x1, y1) in zip(NAMES, grouped):
        im.crop((x0, y0, x1, y1)).save(dst / f"{name}.png")
        print(f"  {name}.png  {x1 - x0}x{y1 - y0}")
    print(f"완료 → {dst}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
