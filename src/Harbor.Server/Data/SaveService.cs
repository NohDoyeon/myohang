using Harbor.Server.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Harbor.Server.Data;

/// <summary>주기적으로 저장 파일을 flush 하고, 종료 시 한 번 더 확실히 쓴다.</summary>
public sealed class SaveService : BackgroundService
{
    private readonly SaveStore _save;
    private readonly SaveOptions _opt;
    private readonly ILogger<SaveService> _log;

    public SaveService(SaveStore save, IOptions<SaveOptions> opt, ILogger<SaveService> log)
    { _save = save; _opt = opt.Value; _log = log; }

    /// <summary>
    /// 짧은 주기로 돌되 실제 쓰기는 바뀐 게 있을 때만(Flush 내부에서 dirty 검사).
    /// 서버가 강제 종료(taskkill /F)되면 StopAsync 가 못 도므로, 주기가 곧 최대 손실 구간이다.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _opt.FlushSeconds)));
        try { while (await timer.WaitForNextTickAsync(ct)) _save.Flush(); }
        catch (OperationCanceledException) { /* 종료 */ }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);
        _save.Flush(force: true);
        _log.LogInformation("save flushed on shutdown");
    }
}
