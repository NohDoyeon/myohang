"""컷별 고양이 PNG 26장 → 게임용 아틀라스(client-godot/art/atlas.png + atlas.json).

    py tools/build-avatar-atlas.py art/ref/myohang_cat_sprites

세 가지를 한다. 하나라도 빠지면 게임에서 이상해진다.

1. **원본 격자 복원** — 생성 AI 의 그림은 "픽셀 아트 풍"이라 한 칸이 4~5픽셀로 확대돼 있다.
   임의 크기로 줄이면 어떤 칸은 사라지고 어떤 칸은 두 배가 된다(12차에 실제로 겪음).
   그래서 **칸 크기를 추정해 한 칸 = 한 픽셀**로 되돌리고, 칸의 색은 **최빈색**을 쓴다(평균은 도트를 흐린다).

2. **발 기준 정렬** — 프레임마다 경계 상자가 다르다(꼬리가 뻗은 프레임은 더 넓다).
   그대로 쓰면 걸을 때 몸이 좌우로 흔들린다. 그래서 **아래쪽 픽셀들의 가운데(발)** 를 기준으로
   모든 프레임을 같은 크기 캔버스에 **아래-가운데 정렬**해 붙인다. 클라이언트는 그냥 바닥에 세우면 된다.

3. **아틀라스 묶기** — 한 장의 PNG + 프레임 사각형 표(atlas.json). 키는 게임이 찾는 이름 그대로:
   `avatar/hd/001_{방향}_{동작}_{프레임}`

⚠ 만든 뒤에는 반드시 `tools/import-assets.ps1` 을 돌려야 Godot 이 새 PNG 를 읽는다.
"""
import sys, json, pathlib, collections
from PIL import Image

ALPHA_MIN = 16          # 이보다 투명하면 없는 픽셀
MARGIN = 1              # 칸 안쪽에서 이만큼 띄고 색을 센다(경계 흐림 오염 방지)

# 시트의 행마다 **캐릭터를 그린 크기가 다르다**(확대 배율이 아니라 그림 자체가 크다).
# 실측: walk/sit/dance 는 42x53 인데 stand·sleep 은 1.33배 크게 그려져 있어, 걸으면 고양이가 작아진다.
# 그래서 그 행만 칸 크기를 키워(=더 많이 줄여) 맞춘다.
# **크기가 일정한 시트를 새로 받으면 이 표는 전부 1.0 으로 돌리면 된다.**
ROW_SCALE = {"stand": 1.33, "walk": 1.0, "sit": 1.0, "sleep": 1.33, "dance": 1.0}

# 아틀라스 전체의 색 수. 팔레트 스왑(색 갈아끼우기)이 되려면 색이 흩어져 있으면 안 된다.
# 원본 고양이 도트가 14색이었으니 그 언저리가 적당하다. 너무 줄이면 음영이 뭉개진다.
COLORS = 16

# 파일 → 아틀라스 키. 폴더 구조가 바뀌면 여기만 고친다.
DIRS = ["dir0_back", "dir1_back_right", "dir2_right", "dir3_front_right", "dir4_front"]
PLAN = (
    [(f"stand/{i:02d}_{n}.png", f"001_{i}_stand_0") for i, n in enumerate(DIRS)] +
    [(f"walk/{i*2+f:02d}_dir{i}_walk{f}.png", f"001_{i}_walk_{f}") for i in range(5) for f in range(2)] +
    [(f"sit/{i:02d}_{n}.png", f"001_{i}_sit_0") for i, n in enumerate(DIRS)] +
    [(f"sleep/{i:02d}_{n}.png", f"001_{i}_sleep_0") for i, n in enumerate(DIRS)] +
    [("dance/00_dir4_front.png", "001_4_dance_0")]
)


def color_runs(im):
    """
    같은 색이 가로·세로로 몇 픽셀씩 이어지는지 모두 센다.

    확대된 도트는 한 칸이 통째로 같은 색이므로, **짧은 이음의 최빈값이 곧 칸 크기**다.
    (열끼리 비교하는 방식은 가장자리가 부드럽게 처리된 그림에서 모든 열이 달라 보여 실패한다.)
    큰 면(몸통)은 칸보다 훨씬 길게 이어지므로 위쪽을 잘라 낸다.
    """
    w, h = im.size
    px = im.load()
    runs = collections.Counter()

    def scan(get, n, m):
        for j in range(m):
            prev, run = None, 0
            for i in range(n):
                c = get(i, j)
                c = c if c[3] >= ALPHA_MIN else None
                if c is not None and c == prev:
                    run += 1
                else:
                    if prev is not None and 2 <= run <= 16:
                        runs[run] += 1
                    prev, run = c, 1
            if prev is not None and 2 <= run <= 16:
                runs[run] += 1

    scan(lambda i, j: px[i, j], w, h)
    scan(lambda i, j: px[j, i], h, w)
    return runs


def estimate_block(im, fallback=0.0):
    """
    **프레임마다 따로** 추정한다. 시트가 행마다 다른 배율로 그려져 있기 때문이다
    (실측: stand 178px, walk 115px — 원본 도트는 같은 크기인데 시트에서만 다르다).
    하나로 잡으면 걸을 때 고양이가 작아진다.

    간격의 **중앙값(소수)** 을 쓴다. 배율이 4.5 같은 값일 때 정수로 반올림하면 격자가 밀린다.
    """
    runs = color_runs(im)
    if sum(runs.values()) < 30:
        return fallback or 1.0
    # 최빈 이음 길이. 바로 옆 길이(±1)까지 표를 합쳐 흔들림을 줄이고,
    # 실제 배율이 4.5 같은 값일 때를 위해 이웃과의 비율로 소수 보정을 한다.
    best = max(runs, key=lambda r: runs[r] + runs.get(r - 1, 0) + runs.get(r + 1, 0))
    lo, hi = runs.get(best - 1, 0), runs.get(best + 1, 0)
    return best + (hi - lo) / float(runs[best] + lo + hi) * 0.5


def downscale(im, block):
    """한 칸 = 한 픽셀로. 칸의 색은 최빈색(평균은 도트를 흐린다). block 은 소수여도 된다."""
    if block <= 1.05:
        return im
    w, h = im.size
    px = im.load()
    ow, oh = max(1, round(w / block)), max(1, round(h / block))
    out = Image.new("RGBA", (ow, oh), (0, 0, 0, 0))
    op = out.load()
    for oy in range(oh):
        for ox in range(ow):
            x0, x1 = round(ox * w / ow), round((ox + 1) * w / ow)
            y0, y1 = round(oy * h / oh), round((oy + 1) * h / oh)
            votes = collections.Counter()
            for x in range(x0 + MARGIN, max(x0 + MARGIN + 1, x1 - MARGIN)):
                for y in range(y0 + MARGIN, max(y0 + MARGIN + 1, y1 - MARGIN)):
                    if 0 <= x < w and 0 <= y < h:
                        c = px[x, y]
                        votes[c if c[3] >= ALPHA_MIN else (0, 0, 0, 0)] += 1
            if votes:
                op[ox, oy] = votes.most_common(1)[0][0]
    return out


def quantize(im, colors):
    """
    색 수를 줄인다. **팔레트 스왑이 되려면 반드시 필요하다.**

    생성 AI 의 그림은 음영이 부드러워 거의 같은 색이 수천 개다(실측 20,014개).
    게임은 "이 색을 저 색으로" 갈아끼우는 방식이라, 색이 흩어져 있으면 몇 개만 바뀌어 **얼룩**이 된다.
    덤으로 도트도 또렷해진다 — 진짜 도트는 원래 색이 열몇 개다.

    투명한 곳은 팔레트 한 칸을 잡아먹지 않도록 가장 흔한 색으로 메운 뒤, 알파는 따로 되돌린다.
    가장자리 알파는 반올림해 흐릿한 테두리(헤일로)를 없앤다.
    """
    alpha = im.split()[3].point(lambda a: 255 if a >= 128 else 0)
    rgb = im.convert("RGB")
    fill = collections.Counter(
        c for c, a in zip(rgb.getdata(), im.getdata()) if a[3] >= 128
    ).most_common(1)
    if fill:
        rgb.paste(fill[0][0], (0, 0), Image.eval(alpha, lambda a: 255 - a))
    q = rgb.quantize(colors=colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGB")
    r, g, b = q.split()
    return Image.merge("RGBA", (r, g, b, alpha))


def keep_largest_blob(im):
    """
    떨어져 있는 조각을 지운다 — 시트의 라벨이 프레임 영역에 걸쳐 같이 잘려 들어온 경우가 있다.
    고양이는 한 덩어리이므로 **가장 큰 덩어리만** 남기면 대개 깨끗해진다.
    (라벨이 고양이에 **닿아 있으면** 한 덩어리가 되어 못 지운다 — 그건 라벨 없는 시트로 다시 받아야 한다.)
    """
    w, h = im.size
    px = im.load()
    seen = [[False] * h for _ in range(w)]
    best, best_size = None, 0
    for sx in range(w):
        for sy in range(h):
            if seen[sx][sy] or px[sx, sy][3] < ALPHA_MIN:
                continue
            stack, blob = [(sx, sy)], []
            seen[sx][sy] = True
            while stack:
                x, y = stack.pop()
                blob.append((x, y))
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h and not seen[nx][ny] and px[nx, ny][3] >= ALPHA_MIN:
                        seen[nx][ny] = True
                        stack.append((nx, ny))
            if len(blob) > best_size:
                best, best_size = blob, len(blob)
    if best is None:
        return im
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    op = out.load()
    for x, y in best:
        op[x, y] = px[x, y]
    return out.crop(out.getbbox() or (0, 0, w, h))


def feet_anchor(im):
    """발 기준점 — 아래쪽 20% 줄의 불투명 픽셀 가운데. 꼬리가 흔들려도 잘 안 변한다."""
    w, h = im.size
    px = im.load()
    xs = []
    for y in range(int(h * 0.8), h):
        for x in range(w):
            if px[x, y][3] >= ALPHA_MIN:
                xs.append(x)
    if not xs:                                  # 누워 있는 프레임 등 — 전체 가운데로
        xs = [x for y in range(h) for x in range(w) if px[x, y][3] >= ALPHA_MIN] or [w // 2]
    xs.sort()
    return xs[len(xs) // 2]


def main():
    root = pathlib.Path(__file__).resolve().parent.parent
    src = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "art/ref/myohang_cat_sprites")
    if not src.is_absolute():
        src = root / src

    missing = [f for f, _ in PLAN if not (src / f).exists()]
    if missing:
        print("파일이 없습니다:", *missing[:5], sep="\n  ")
        return 1

    # 1) 라벨 조각 제거 — 시트의 'dir 0' 같은 글자가 프레임에 걸쳐 같이 잘려 온다.
    #    글자가 아래에 붙으면 그게 바닥이 되어 **고양이가 떠 보인다.** 그래서 반드시 먼저 지운다.
    raws = [keep_largest_blob(Image.open(src / f).convert("RGBA")) for f, _ in PLAN]

    # 2) 칸 크기는 **프레임마다** 추정한다(시트가 행마다 다른 배율로 그려져 있다).
    forced = next((float(a) for a in sys.argv[2:] if a.replace(".", "", 1).isdigit()), 0.0)
    rough = estimate_block(raws[0]) or 3.0
    blocks = [(forced if forced > 0 else estimate_block(im, rough)) * ROW_SCALE.get(f.split("/")[0], 1.0)
              for im, (f, _) in zip(raws, PLAN)]

    smalls = [downscale(im, b) for im, b in zip(raws, blocks)]
    for (f, key), im, b in zip(PLAN, smalls, blocks):
        print(f"  {key:<18} 칸 {b:>4.1f}px → {im.size[0]}x{im.size[1]}")
    anchors = [feet_anchor(im) for im in smalls]

    # 모든 프레임을 같은 캔버스에 아래-가운데 정렬 → 걸을 때 몸이 흔들리지 않는다
    left = max(a for a in anchors)
    right = max(im.size[0] - a for im, a in zip(smalls, anchors))
    cw, ch = left + right, max(im.size[1] for im in smalls)
    print(f"공통 캔버스: {cw}x{ch}")

    frames, cols = {}, 8
    rows = (len(smalls) + cols - 1) // cols
    atlas = Image.new("RGBA", (cols * cw, rows * ch), (0, 0, 0, 0))
    for i, (im, anchor) in enumerate(zip(smalls, anchors)):
        cx, cy = (i % cols) * cw, (i // cols) * ch
        atlas.paste(im, (cx + left - anchor, cy + ch - im.size[1]))
        frames[f"avatar/hd/{PLAN[i][1]}"] = {"frame": {"x": cx, "y": cy, "w": cw, "h": ch}}

    # 색 줄이기는 **아틀라스 전체에 한 번** 한다 — 프레임마다 따로 하면 프레임끼리 색이 미세하게 달라진다.
    before = len(set(atlas.getdata()))
    atlas = quantize(atlas, COLORS)
    print(f"색 {before} -> {len(set(c[:3] for c in atlas.getdata() if c[3] > 0))}개로 정리")

    out_png = root / "client-godot/art/atlas.png"
    out_json = root / "client-godot/art/atlas.json"
    out_cs = root / "client-godot/art/AtlasMeta.cs"
    atlas.save(out_png)
    meta = json.dumps({
        "frames": frames,
        "meta": {"image": "atlas.png", "size": {"w": atlas.size[0], "h": atlas.size[1]}, "format": "RGBA8888"},
    }, ensure_ascii=False, indent=2)
    out_json.write_text(meta, encoding="utf-8")

    # 같은 내용을 C# 상수로도 뱉는다. **내보낸 게임에서는 이쪽이 쓰인다.**
    # atlas.json 은 Godot 이 말하는 '리소스'가 아니라서 export_presets.cfg 의 include_filter 에
    # 걸려야만 pck 에 들어가는데, 그 동작이 같은 설정에서도 들쭉날쭉했다(2026-09-18).
    # 빠져도 크래시가 아니라 **고양이가 네모로 나오는 조용한 실패**라 알아채기 어렵다.
    # 스크립트는 dll 로 컴파일되므로 이 경로는 빠질 수가 없다.
    if '"""' in meta:                      # raw string literal 의 구분자와 충돌하면 안 된다
        raise SystemExit('atlas.json 에 \"\"\" 가 들어 있어 C# 상수로 감쌀 수 없습니다.')
    out_cs.write_text(
        "// 자동 생성됨 — tools/build-avatar-atlas.py. 손으로 고치지 말 것.\n"
        "// atlas.json 과 같은 내용이며, 내보낸 게임은 파일 대신 이것을 읽는다(AssetCatalog 참고).\n"
        "namespace HarborClient;\n\n"
        "public static class AtlasMeta\n{\n"
        "    public const string Json = \"\"\"\n"
        f"{meta}\n"
        "\"\"\";\n}\n",
        encoding="utf-8")

    print(f"프레임 {len(frames)}개 -> {out_png.name} ({atlas.size[0]}x{atlas.size[1]})")
    print(f"             -> {out_json.name} · {out_cs.name}")
    print("[!] 이제 tools/import-assets.ps1 을 실행해야 Godot 이 새 PNG 를 읽습니다.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
