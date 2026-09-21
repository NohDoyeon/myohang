// 화살표 / WASD 이동.
//
// **화면 기준으로 맞춘다.** 아이소메트릭에서 월드 축(x, y)은 화면상 대각선이라,
// 화살표를 월드 축에 그대로 붙이면 "위를 눌렀는데 오른쪽 위로" 간다.
//   sx = (x-y)·32,  sy = (x+y)·16   →   화면에서 곧게 위 = x-1 **이면서** y-1
//
// 서버에는 걸음 단위 패킷이 없다. `C_Move(목표칸)` 하나뿐이고 경로는 서버가 찾는다.
// 그래서 키를 누르고 있으면 **한 칸씩 목표를 갱신**해 보낸다 — 칸당 간격은 room.moveMs 를 쓴다.

export interface Step { dx: number; dy: number }

/** 화면 방향 → 월드 델타 */
const DIRS: Record<string, Step> = {
  ArrowUp: { dx: -1, dy: -1 },
  ArrowDown: { dx: 1, dy: 1 },
  ArrowLeft: { dx: -1, dy: 1 },
  ArrowRight: { dx: 1, dy: -1 },
  KeyW: { dx: -1, dy: -1 },
  KeyS: { dx: 1, dy: 1 },
  KeyA: { dx: -1, dy: 1 },
  KeyD: { dx: 1, dy: -1 },
};

export interface WalkControlOptions {
  /** 지금 서 있는 칸. 없으면 이동하지 않는다. */
  origin: () => { x: number; y: number } | null;
  walkable: (x: number, y: number) => boolean;
  move: (x: number, y: number) => void;
  /** 한 칸 가는 데 걸리는 시간(ms). room.moveMs 를 넣는다. */
  stepMs: () => number;
}

/**
 * window 에 키 핸들러를 건다. 돌려주는 함수를 부르면 해제된다.
 * 입력칸에 글을 쓰는 중에는 무시한다 — 채팅에 "w" 를 치다 걸어가면 안 된다.
 */
export function attachWalkControl(o: WalkControlOptions): () => void {
  const held = new Set<string>();
  let timer: number | null = null;

  const typing = (t: EventTarget | null) =>
    t instanceof HTMLElement && (t.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(t.tagName));

  const stepOnce = () => {
    if (held.size === 0) return;
    const from = o.origin();
    if (!from) return;

    // 여러 키를 같이 누르면 합친다 → 대각선도 자연스럽게 나온다.
    let dx = 0, dy = 0;
    for (const code of held) { const d = DIRS[code]; if (d) { dx += d.dx; dy += d.dy; } }
    if (dx === 0 && dy === 0) return;
    dx = Math.sign(dx); dy = Math.sign(dy);

    const tx = from.x + dx, ty = from.y + dy;
    if (o.walkable(tx, ty)) o.move(tx, ty);
  };

  const start = () => {
    if (timer !== null) return;
    stepOnce();                                   // 첫 걸음은 즉시 — 눌렀는데 멈칫하면 안 된다
    timer = window.setInterval(stepOnce, Math.max(80, o.stepMs()));
  };

  const stop = () => {
    if (timer === null) return;
    window.clearInterval(timer);
    timer = null;
  };

  const onDown = (e: KeyboardEvent) => {
    if (typing(e.target) || !(e.code in DIRS)) return;
    e.preventDefault();                            // 화살표로 페이지가 스크롤되지 않게
    if (!held.has(e.code)) { held.add(e.code); stop(); start(); }
  };

  const onUp = (e: KeyboardEvent) => {
    if (!(e.code in DIRS)) return;
    held.delete(e.code);
    if (held.size === 0) stop();
  };

  // 창을 벗어나면 키를 뗀 이벤트가 안 오므로 눌린 채로 남는다 → 계속 걸어간다.
  const onBlur = () => { held.clear(); stop(); };

  window.addEventListener("keydown", onDown);
  window.addEventListener("keyup", onUp);
  window.addEventListener("blur", onBlur);

  return () => {
    stop();
    window.removeEventListener("keydown", onDown);
    window.removeEventListener("keyup", onUp);
    window.removeEventListener("blur", onBlur);
  };
}
