using System.Net.WebSockets;
using System.Threading.Channels;
using Harbor.Protocol;
using Harbor.Server.Data;
using Harbor.Server.Rooms;
using Microsoft.Extensions.Logging;

namespace Harbor.Server.Net;

/// <summary>
/// 접속 1개. 수신 루프 → Dispatcher, 송신은 Channel 직렬화.
/// **전송 수단은 `ITransport` 가 감춘다** — TCP(데스크톱 클라)든 WebSocket(브라우저)이든 여기는 같다.
/// 지갑/가방은 MVP 메모리 상주(영속 계층 전). 세션 스레드와 룸 루프 스레드 양쪽에서 만지므로 lock 으로 보호.
/// </summary>
public sealed class Session : IAsyncDisposable
{
    private static long _nextId;
    public long Id { get; } = Interlocked.Increment(ref _nextId);
    public long UserId { get; set; }
    public string Nick { get; set; } = "";
    public RoomInstance? Room { get; set; }
    public long HomeRoomId { get; set; }

    private readonly ITransport _transport;
    private readonly Channel<byte[]> _outbox = Channel.CreateUnbounded<byte[]>(new() { SingleReader = true });
    private readonly ILogger _log;
    private readonly CancellationTokenSource _cts = new();

    // ----- 경제 (메모리 + SaveStore 로 write-through) -----
    private readonly object _eco = new();
    private long _rupee;
    private readonly Dictionary<string, int> _inv = new();
    private readonly SaveStore? _save;

    private string _lastAllowance = "";
    private int _streak;
    private string _figure = Harbor.Core.Figure.Default;

    /// <summary>아바타 외모. 형식이 깨진 값은 기본 외모로 되돌린다.</summary>
    public string Figure
    {
        get { lock (_eco) return _figure; }
        set { lock (_eco) { _figure = Harbor.Core.Figure.Sanitize(value); Persist(); } }
    }

    // 루피가 움직일 땐 항상 이유를 남긴다 — currency_log 가 나중에 복구·추적의 유일한 근거가 된다.
    public long Rupee { get { lock (_eco) return _rupee; } }

    public long RupeeSet(long value, string reason)
    {
        lock (_eco) { long delta = value - _rupee; _rupee = Math.Max(0, value); Persist(); Log(delta, reason); return _rupee; }
    }

    public bool RupeeTrySpend(long amount, string reason)
    {
        lock (_eco)
        {
            if (amount < 0 || _rupee < amount) return false;
            _rupee -= amount; Persist(); Log(-amount, reason);
            return true;
        }
    }

    /// <summary>가감 후 잔액. 읽고-쓰기 경합을 막기 위해 setter 대신 이걸 쓴다.</summary>
    public long RupeeAdd(long delta, string reason)
    {
        lock (_eco) { long before = _rupee; _rupee = Math.Max(0, _rupee + delta); Persist(); Log(_rupee - before, reason); return _rupee; }
    }

    /// <summary>_eco 락 안에서 호출. 원장은 별도 락이라 교착 없음.</summary>
    private void Log(long delta, string reason)
    {
        if (delta != 0) _save?.LogCurrency(Nick, delta, _rupee, reason);
    }
    /// <summary>마지막 용돈 수령일 (yyyy-MM-dd).</summary>
    public string LastAllowance { get { lock (_eco) return _lastAllowance; } set { lock (_eco) { _lastAllowance = value ?? ""; Persist(); } } }
    /// <summary>연속 출석 일수. 하루 빠지면 1 부터 다시 (Harbor.Core.Attendance).</summary>
    public int Streak { get { lock (_eco) return _streak; } set { lock (_eco) { _streak = Math.Max(0, value); Persist(); } } }
    public int InvQty(string furniId) { lock (_eco) return _inv.GetValueOrDefault(furniId); }
    /// <summary>수량 가감 후 현재 수량.</summary>
    public int InvAdd(string furniId, int delta)
    {
        lock (_eco)
        {
            int q = Math.Max(0, _inv.GetValueOrDefault(furniId) + delta);
            if (q == 0) _inv.Remove(furniId); else _inv[furniId] = q;
            Persist();
            return q;
        }
    }
    public bool InvTryTake(string furniId, out int remaining) => InvTryTakeMany(furniId, 1, out remaining);

    /// <summary>여러 개를 한 번에 뺀다 — 모자라면 하나도 빼지 않는다(판매처럼 수량이 걸린 처리용).</summary>
    public bool InvTryTakeMany(string furniId, int count, out int remaining)
    {
        lock (_eco)
        {
            int q = _inv.GetValueOrDefault(furniId);
            if (count <= 0 || q < count) { remaining = q; return false; }
            remaining = q - count;
            if (remaining == 0) _inv.Remove(furniId); else _inv[furniId] = remaining;
            Persist();
            return true;
        }
    }
    public List<(string furniId, int qty)> InvSnapshot() { lock (_eco) return _inv.Select(kv => (kv.Key, kv.Value)).ToList(); }

    /// <summary>_eco 락 안에서만 호출. 로그인 전(닉 없음)엔 무시.</summary>
    private void Persist()
    {
        if (_save is null || Nick.Length == 0) return;
        _save.UpdateUser(Nick, _rupee, _inv, _lastAllowance, _figure, _streak);
    }

    /// <summary>로그인 시 저장된 지갑·가방·외모·출석을 통째로 복원.</summary>
    public void RestoreEconomy(long rupee, IEnumerable<KeyValuePair<string, int>> inv, string lastAllowance, string figure, int streak = 0)
    {
        lock (_eco)
        {
            _rupee = rupee;
            _lastAllowance = lastAllowance ?? "";
            _streak = Math.Max(0, streak);
            _figure = Harbor.Core.Figure.Sanitize(figure);
            _inv.Clear();
            foreach (var kv in inv) if (kv.Value > 0) _inv[kv.Key] = kv.Value;
            Persist();
        }
    }

    public void SendInventoryUpdate(FurniDef def, int qty)
        => Send(Opcode.S_InventoryUpdate, new S_InventoryUpdate
        {
            FurniId = def.FurniId, Qty = qty, Name = def.DisplayName, Wall = def.Wall, Interaction = def.Interaction.Type,
            SellPrice = def.Sell?.Price ?? 0, SellBundleQty = def.Sell?.BundleQty ?? 0, SellBundlePrice = def.Sell?.BundlePrice ?? 0,
        });
    /// <summary>오류가 아닌 안내(판매 결과, 방 넓히기 등). 클라는 토스트+채팅 로그로 보여준다.</summary>
    public void Notice(string text) => Send(Opcode.S_Notice, new S_Notice { Text = text });
    public void SendWallet() => Send(Opcode.S_WalletUpdate, new S_WalletUpdate { Rupee = Rupee, Cash = 0 });

    public string Remote => _transport.Remote;
    public DateTime ConnectedUtc { get; } = DateTime.UtcNow;

    /// <summary>관리자 킥·서버 종료용. 수신 루프가 끝나면서 `SessionRunner` 가 방 퇴장·Release 까지 정리한다.</summary>
    public void Close() => _cts.Cancel();

    public Session(ITransport transport, ILogger log, SaveStore? save = null)
    {
        _transport = transport; _log = log; _save = save;
        _ = Task.Run(SendLoop);
    }

    public void Send<T>(Opcode op, T body) => _outbox.Writer.TryWrite(Framing.Encode(op, body));

    public async Task ReceiveLoop(Dispatcher dispatcher)
    {
        var buf = new byte[Framing.MaxFrame * 2];
        int filled = 0;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                int n = await _transport.ReceiveAsync(buf.AsMemory(filled), _cts.Token);
                if (n == 0) break;
                filled += n;
                ReadOnlyMemory<byte> view = buf.AsMemory(0, filled);
                while (Framing.TryDecode(ref view, out var op, out var body))
                    await dispatcher.Dispatch(this, op, body);
                view.CopyTo(buf);          // 잔여 바이트 앞으로 당김
                filled = view.Length;
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidDataException or WebSocketException)
        {
            _log.LogDebug("session {Id} closed: {Msg}", Id, ex.Message);
        }
    }

    private async Task SendLoop()
    {
        try
        {
            await foreach (var frame in _outbox.Reader.ReadAllAsync(_cts.Token))
                await _transport.SendAsync(frame, _cts.Token);
        }
        catch (Exception) { /* 소켓 종료 */ }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _outbox.Writer.TryComplete();
        await _transport.DisposeAsync();
    }
}
