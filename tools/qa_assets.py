"""에셋 규격 검사 (커밋 훅/CI). 위반 시 exit 1.
검사: 마스터 팔레트 준수, 캔버스 규격(avatar 32x56, furni 64x96 배수), 완전 투명 파일.
"""
import json, pathlib, sys
from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parent.parent
MASTER = {tuple(c) for c in json.load(open(ROOT / "art/palette48.json"))["colors"]}
SPEC = {"avatar": (32, 56), "furni": (64, 96), "tile": (64, 32)}

bad = []
for png in (ROOT / "art/out").rglob("*.png"):
    im = Image.open(png).convert("RGBA")
    cols = im.getcolors(1 << 22) or []
    opaque = {c[:3] for _, c in cols if c[3] > 0}
    if not opaque:
        bad.append((png, "empty")); continue
    if not opaque <= MASTER:
        bad.append((png, "palette", sorted(opaque - MASTER)[:5]))
    kind = png.relative_to(ROOT / "art/out").parts[0]
    if kind in SPEC:
        w, h = SPEC[kind]
        if im.width % w or im.height % h:
            bad.append((png, "size", im.size))
for b in bad:
    print(*b)
sys.exit(1 if bad else 0)
