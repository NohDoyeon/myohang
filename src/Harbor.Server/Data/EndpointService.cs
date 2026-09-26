using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Harbor.Server.Data;

/// <summary>
/// **게임 서버가 자기 공개 주소를 스스로 알린다.**
///
/// 문제: 무료 `trycloudflare.com` 터널은 켤 때마다 주소가 바뀐다. 웹이 그 주소를 빌드 타임
/// 환경변수(`NEXT_PUBLIC_GAME_WS`)로만 알면, 터널을 다시 열 때마다 사람이 Vercel 설정을 고치고
/// **재배포**해야 한다 — 그 사이 테스터는 들어오지 못한다.
///
/// 해법: 주소를 아는 쪽(서버)이 적고, 필요한 쪽(웹)이 읽는다. 둘이 이미 같은 DB 를 본다.
///   `tools/run-public.sh` 가 cloudflared 출력에서 주소를 읽어 `HARBOR_PUBLIC_WS` 로 넘긴다
///   → 이 서비스가 `server_endpoint` 에 적는다
///   → 웹의 `/api/endpoint` 가 읽어 브라우저에 준다.
///
/// 30초마다 `updated_at` 을 갱신한다(심장박동). 서버가 강제 종료(taskkill /F)되면 정리할 기회가
/// 없으므로, 웹은 `online` 만 믿지 않고 **얼마나 오래됐는지**로 판단한다.
///
/// `HARBOR_PUBLIC_WS` 가 없으면 **아무것도 하지 않는다.** 로컬 개발용으로 띄운 서버가 실제 공개
/// 주소를 빈 값으로 덮어쓰면, 밖에서는 아무도 못 들어오는데 이유는 안 보이는 상태가 된다.
/// </summary>
public sealed class EndpointService : BackgroundService
{
    /// <summary>심장박동 주기. 웹은 이 값의 3배(90초)까지를 "살아 있음"으로 본다.</summary>
    private static readonly TimeSpan Beat = TimeSpan.FromSeconds(30);

    private readonly SaveStore _save;
    private readonly ILogger<EndpointService> _log;
    private readonly string _wsUrl;

    public EndpointService(SaveStore save, ILogger<EndpointService> log)
    {
        _save = save;
        _log = log;
        _wsUrl = (Environment.GetEnvironmentVariable("HARBOR_PUBLIC_WS") ?? "").Trim();
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (_wsUrl.Length == 0)
        {
            _log.LogInformation("HARBOR_PUBLIC_WS 가 없어 공개 주소를 알리지 않습니다 (밖에서 붙으려면 tools/run-public.sh).");
            return;
        }

        _log.LogInformation("공개 주소를 알립니다: {url}", _wsUrl);
        Publish(online: true);

        using var timer = new PeriodicTimer(Beat);
        try { while (await timer.WaitForNextTickAsync(ct)) Publish(online: true); }
        catch (OperationCanceledException) { /* 종료 */ }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);
        // 곱게 내려간 경우에만 돈다. 강제 종료면 심장박동이 멈추는 것으로 웹이 알아챈다.
        if (_wsUrl.Length > 0) Publish(online: false);
    }

    /// <summary>
    /// 알림 실패는 게임을 멈출 이유가 아니다 — 이미 접속한 사람은 그대로 놀고 있다.
    /// 로그만 남기고 다음 박동에 다시 시도한다.
    /// </summary>
    private void Publish(bool online)
    {
        try
        {
            using var conn = _save.OpenConnection();
            conn.Execute("""
                INSERT INTO server_endpoint (name, ws_url, online, updated_at)
                VALUES ('game', @url, @online, now())
                ON CONFLICT (name) DO UPDATE
                    SET ws_url = EXCLUDED.ws_url, online = EXCLUDED.online, updated_at = now()
                """, new { url = _wsUrl, online });
        }
        catch (Exception e)
        {
            _log.LogWarning("공개 주소를 알리지 못했습니다: {msg}", e.Message);
        }
    }
}
