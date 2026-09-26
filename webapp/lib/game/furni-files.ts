// 가구 그림 파일 목록. **한 곳에만 적는다** — 게임 화면(Pixi)과 상점(React) 둘 다 여기를 본다.
//
// 목록을 코드에 적어 두는 이유: 브라우저는 폴더를 훑을 수 없다. 대신 **없으면 조용히 건너뛰므로**
// 여기 이름을 먼저 적어 두고 그림은 나중에 넣어도 된다(그때까지는 도형으로 그려진다).
//
// 키 규칙은 서버·Godot 과 같다: `{furniId}_{방향}_{상태}`. 방향이 없는 그림은 `_0_`,
// 벽걸이는 `_4_`(북쪽 벽) 이다. 파일은 `webapp/public/art/furni/` 에 있다.
//
// 새 그림을 받으면: `art/ref/files/` 에 넣고 → `py tools/build-furni-tiles.py art/ref/files`
// → 여기 이름 한 줄 추가.

export const FURNI_FILES = [
  "bookshelf_0_default",
  "chair_wood_0_default",
  "table_round_0_default",
  "rug_round_0_default",
  "plant_pot_0_default",
  "lamp_floor_0_off",
  "fridge_red_0_closed",
  "frame_photo_4_default",
  "flower_pot_0_seed",
  "flower_pot_0_sprout",
  "flower_pot_0_bud",
  "flower_pot_0_bloom",
  "flower_cut_0_default",
];

export const FURNI_BASE = "/art/furni";

/**
 * 목록에 있는 것 중 이 가구의 **첫 그림** 주소. 없으면 null — 부르는 쪽이 글자로만 보여준다.
 *
 * 상태가 여럿인 가구(화분 4단계)는 목록의 첫 상태가 나온다. 상점에서는 그게 맞다 —
 * 사는 시점의 모습이 씨앗이기 때문이다.
 */
export function furniThumb(furniId: string): string | null {
  const name = FURNI_FILES.find((f) => f.startsWith(`${furniId}_`));
  return name ? `${FURNI_BASE}/${name}.png` : null;
}
