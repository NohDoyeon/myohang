// 입장권을 첫 화면에서 게임 화면으로 넘기는 통로.
//
// **sessionStorage 를 쓴다.** 주소창(`/play?t=…`)에 실으면 서버 접근 로그·브라우저 히스토리·
// 프록시·`Referer` 헤더에 그대로 남는다. 입장권은 5분간 그 계정으로 입장할 수 있는 열쇠다.
//
// sessionStorage 는 탭을 닫으면 사라지고 다른 탭과 공유되지 않는다 — 입장권의 수명과 잘 맞는다.
// 사생활 모드나 저장이 막힌 환경에서는 접근 자체가 예외를 던지므로 항상 감싼다.

export const TICKET_KEY = "myohang.ticket";

export function takeTicket(): string | null {
  try {
    const t = sessionStorage.getItem(TICKET_KEY);
    // **한 번 쓰면 지운다.** 남겨 두면 새로고침 때 만료된 입장권으로 접속을 시도하게 된다.
    if (t) sessionStorage.removeItem(TICKET_KEY);
    return t;
  } catch {
    return null;
  }
}
