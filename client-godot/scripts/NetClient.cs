using System.Net.Sockets;
using System.Threading.Channels;
using Godot;
using Harbor.Protocol;

namespace HarborClient;

/// <summary>
/// Autoload 싱글턴. TCP 수신은 백그라운드, 디스패치는 _Process(메인 스레드).
/// 연결 실패는 ConnectAsync 예외로, 연결 끊김은 Disconnected 이벤트(메인 스레드, 1회)로 알린다.
/// </summary>
public partial class NetClient : Node
{
    public static NetClient Instance { get; private set; } = null!;
    public event Action<Opcode, ReadOnlyMemory<byte>>? PacketReceived;
    /// <summary>수신 루프 종료 후 메인 스레드에서 1회. 인자 = 사유. 의도적 Close() 는 알리지 않는다.</summary>
    public event Action<string>? Disconnected;
    public bool Online => _stream is not null && !_closed;   // 이름 주의: GodotObject.IsConnected(signal) 와 충돌 방지

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private readonly Channel<(Opcode, ReadOnlyMemory<byte>)> _inbox = Channel.CreateUnbounded<(Opcode, ReadOnlyMemory<byte>)>();
    private CancellationTokenSource? _cts;
    private readonly object _sendLock = new();
    private int _gen;                      // 연결 세대. 이전 연결의 종료 통지를 걸러냄.
    private volatile bool _closed;
    private bool _closeReported;
    private string _closeReason = "";

    public override void _Ready() => Instance = this;

    public async Task ConnectAsync(string host, int port)
    {
        Close();
        int gen = _gen;
        var tcp = new TcpClient { NoDelay = true };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try { await tcp.ConnectAsync(host, port, timeout.Token); }
        catch (OperationCanceledException) { tcp.Dispose(); throw new TimeoutException("연결 시간 초과"); }
        catch { tcp.Dispose(); throw; }

        _tcp = tcp; _stream = tcp.GetStream();
        _closed = false; _closeReported = false; _closeReason = "";
        _cts = new CancellationTokenSource();
        var stream = _stream; var ct = _cts.Token;
        _ = Task.Run(() => ReceiveLoop(stream, gen, ct));
    }

    /// <summary>의도적 종료. Disconnected 는 발생하지 않는다.</summary>
    public void Close()
    {
        _gen++;
        _cts?.Cancel(); _cts = null;
        try { _tcp?.Close(); } catch { /* 이미 닫힘 */ }
        _tcp = null; _stream = null;
    }

    public void Send<T>(Opcode op, T body)
    {
        var stream = _stream;
        if (stream is null || _closed) return;
        var frame = Framing.Encode(op, body);
        try { lock (_sendLock) stream.Write(frame, 0, frame.Length); }   // 프레임이 작아 동기 전송으로 충분(순서 보장)
        catch (Exception ex) { MarkClosed(_gen, ex.Message); }
    }

    private async Task ReceiveLoop(NetworkStream stream, int gen, CancellationToken ct)
    {
        var buf = new byte[Framing.MaxFrame * 2];
        int filled = 0;
        string reason = "서버가 연결을 닫았어요";
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(buf.AsMemory(filled), ct);
                if (n == 0) break;
                filled += n;
                ReadOnlyMemory<byte> view = buf.AsMemory(0, filled);
                while (Framing.TryDecode(ref view, out var op, out var body))
                    _inbox.Writer.TryWrite((op, body.ToArray()));   // 복사 — 버퍼 재사용 안전
                view.CopyTo(buf);
                filled = view.Length;
            }
        }
        catch (OperationCanceledException) { return; }               // 의도적 Close
        catch (Exception ex) { reason = ex.Message; }
        MarkClosed(gen, reason);
    }

    private void MarkClosed(int gen, string reason)
    {
        if (gen != _gen) return;   // 이전 세대 연결 — 무시
        _closeReason = reason; _closed = true;
    }

    public override void _Process(double delta)
    {
        while (_inbox.Reader.TryRead(out var pkt)) PacketReceived?.Invoke(pkt.Item1, pkt.Item2);
        if (_closed && !_closeReported) { _closeReported = true; Disconnected?.Invoke(_closeReason); }
    }

    public override void _ExitTree() => Close();
}
