"""가구 원본 그림 → 게임용 스프라이트.

    py tools/build-furni-tiles.py art/ref/files

산출물은 `webapp/public/art/furni/<파일명>.png`. 파일명이 곧 아틀라스 키다(`{furniId}_{방향}_{상태}`).

## 왜 일괄 축소가 아닌가

**가구는 서로 크기가 달라야 한다.** 폭을 전부 64로 맞추면 조명이 탁자만큼 넓어진다.
받는 그림은 크기가 제각각이라(68~256px) 원본 비율을 믿을 수도 없다.
그래서 **물건별 목표 폭**을 아래 표에 적어 둔다. 눈으로 보고 한 번 맞추면 되는 값이다.

기준: 바닥 타일이 64×32, 고양이가 53×69. 의자는 고양이보다 조금 작고, 냉장고는 고양이보다 크다.

## 왜 색을 줄이는가

생성 AI 는 거의 같은 색을 수천 가지로 만든다(여기선 6,000~27,000개).
그대로 두면 파일이 커지고 도트 느낌이 뭉개진다. 고양이 아틀라스 때와 같은 처리다.
"""
import pathlib
import sys

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "webapp/public/art/furni"

COLORS = 24          # 가구는 캐릭터보다 색이 다양해도 된다(나무결·금속 등)
DEFAULT_W = 56

# 물건별 목표 **폭**(px). 높이는 비율대로 따라간다.
# 바닥에 닿는 면이 64×32 다이아몬드 안에 들어오는 것이 원칙이라 대개 64 이하다.
TARGET_W = {
    "table_round": 62,      # 한 칸을 거의 채우는 둥근 탁자
    "rug_round": 64,        # 바닥에 깔리는 것 — 타일 폭에 맞춘다
    "bookshelf": 58,
    "fridge_red": 52,       # 좁고 높다
    "chair_wood": 44,       # 고양이(53)보다 작아야 앉은 그림이 자연스럽다
    "lamp_floor": 30,       # 가늘고 높다 — 폭을 넓히면 기둥이 통나무가 된다
    "plant_pot": 40,
    "frame_photo": 38,      # 벽걸이
    "flower_pot": 46,       # 캣닢 화분 (4단계 공통)
    "flower_cut": 26,       # 캣닢 잎 — 가방 아이콘 크기
}

MAX_H = 112              # 벽 높이(96)보다 조금 더. 이보다 높으면 방을 가린다.


def target_width(name: str) -> int:
    """파일명 앞부분(furniId)으로 목표 폭을 찾는다. `flower_pot_0_bloom` → `flower_pot`."""
    for key, w in sorted(TARGET_W.items(), key=lambda kv: -len(kv[0])):
        if name.startswith(key):
            return w
    return DEFAULT_W


def convert(path: pathlib.Path) -> tuple[str, str]:
    im = Image.open(path).convert("RGBA")

    # 투명 여백을 먼저 잘라낸다. 그래야 "폭 46" 이 그림의 실제 폭이 된다
    # (여백이 섞여 있으면 물건마다 실제 크기가 달라진다).
    box = im.getbbox()
    if box:
        im = im.crop(box)

    w = target_width(path.stem)
    h = max(1, round(im.height * w / im.width))
    if h > MAX_H:                       # 너무 높으면 높이를 기준으로 다시 맞춘다
        h = MAX_H
        w = max(1, round(im.width * h / im.height))

    small = im.resize((w, h), Image.LANCZOS)

    # 색 줄이기. 알파를 보존해야 하므로 RGB 만 양자화하고 알파는 따로 붙인다.
    alpha = small.getchannel("A")
    rgb = small.convert("RGB").quantize(colors=COLORS, method=Image.MEDIANCUT).convert("RGB")
    out = rgb.convert("RGBA")
    out.putalpha(alpha)
    # 반투명 가장자리는 도트에서 지저분하다 → 임계값으로 자른다.
    out.putalpha(alpha.point(lambda a: 255 if a > 128 else 0))

    OUT.mkdir(parents=True, exist_ok=True)
    out.save(OUT / path.name)
    return path.name, f"{im.width}x{im.height} -> {w}x{h}"


def main():
    src = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "art/ref/files")
    if not src.is_absolute():
        src = ROOT / src
    files = sorted(src.glob("*.png"))
    if not files:
        raise SystemExit(f"{src} 에 png 가 없습니다")

    for p in files:
        if "OPTIONAL" in p.stem:        # 아직 정의(JSON)가 4단계라 쓰이지 않는다
            print(f"{p.name:40s} 건너뜀 (정의가 5단계가 되면 쓴다)")
            continue
        name, how = convert(p)
        print(f"{name:40s} {how}")

    print(f"\n{OUT} 에 넣었습니다.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
