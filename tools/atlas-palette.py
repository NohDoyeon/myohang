"""아틀라스에 실제로 쓰인 색을 많이 쓰인 순서로 뽑는다.

    py tools/atlas-palette.py                      # client-godot/art/atlas.png
    py tools/atlas-palette.py art/ref/고양이.png    # 다른 파일

팔레트 스왑 셰이더는 "이 색 → 저 색" 표가 있어야 만들 수 있다.
도트는 안티앨리어싱이 없어 색 수가 적으므로, 이 목록이 곧 그 표의 재료다.
밝기(L)도 같이 찍는다 — 같은 털색 계열이 밝은 순서로 늘어서므로 어디까지가 '털'인지 눈으로 구분된다.
"""
import sys, pathlib, colorsys
from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parent.parent
path = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "client-godot/art/atlas.png"
if not path.is_absolute():
    path = ROOT / path

im = Image.open(path).convert("RGBA")
counts = {}
for r, g, b, a in im.getdata():
    if a < 8:                      # 투명은 센다는 의미가 없다
        continue
    counts[(r, g, b)] = counts.get((r, g, b), 0) + 1

total = sum(counts.values())
print(f"{path.name}  {im.width}x{im.height}  색 {len(counts)}개  불투명 픽셀 {total}")
print(f"{'#':>3}  {'HEX':<8} {'픽셀':>7} {'비율':>6}  {'H':>5} {'S':>5} {'L':>5}")
for i, (rgb, n) in enumerate(sorted(counts.items(), key=lambda kv: -kv[1]), 1):
    r, g, b = rgb
    h, l, s = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
    print(f"{i:>3}  {'%02x%02x%02x' % rgb:<8} {n:>7} {n/total*100:>5.1f}%  {h*360:>5.0f} {s*100:>4.0f}% {l*100:>4.0f}%")
