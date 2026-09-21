using System.Net.Sockets;
using System.Net.WebSockets;

namespace Harbor.Server.Net;

/// <summary>
/// 세션이 바이트를 주고받는 통로. **TCP 와 WebSocket 을 같은 세션 코드로 다루기 위해** 있다.
///
/// 프레임 형식은 둘 다 같다 — `[uint32 length][uint16 opcode][MessagePack body]` (`Framing`).
/// WebSocket 은 이미 메시지 경계를 주므로 length 4바이트가 남지만 **그대로 둔다**:
/// 그래야 `Framing` 과 클라이언트 디코더가 한 벌로 끝난다. 4바이트 아끼자고 양쪽에 분기를 만들 이유가 없다.
///
/// 읽기는 **스트림처럼** 동작한다(부분 수신 허용). 세션의 수신 루프가 어차피 버퍼에 쌓아 두고
/// 완전한 프레임만 잘라내므로, 경계가 어디서 끊기든 상관없다.
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>읽은 바이트 수. 0 이면 상대가 끊은 것.</summary>
    ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct);

    /// <summary>프레임 하나를 보낸다. 세션의 송신 루프 하나만 호출한다(동시 호출 없음).</summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct);

    /// <summary>로그용. 예: `tcp 127.0.0.1:52311`</summary>
    string Remote { get; }
}

public sealed class TcpTransport : ITransport
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;

    public TcpTransport(TcpClient tcp)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        Remote = $"tcp {tcp.Client.RemoteEndPoint}";
    }

    public string Remote { get; }

    public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct) => _stream.ReadAsync(buffer, ct);
    public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) => _stream.WriteAsync(frame, ct);

    public ValueTask DisposeAsync()
    {
        _tcp.Close();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// 브라우저용. 브라우저는 raw TCP 를 열 수 없어서 WebSocket 이 유일한 길이다.
/// 프레임 하나 = 바이너리 메시지 하나로 보낸다.
/// </summary>
public sealed class WebSocketTransport : ITransport
{
    private readonly WebSocket _ws;

    public WebSocketTransport(WebSocket ws, string remote)
    {
        _ws = ws;
        Remote = $"ws {remote}";
    }

    public string Remote { get; }

    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct)
    {
        var r = await _ws.ReceiveAsync(buffer, ct);
        if (r.MessageType == WebSocketMessageType.Close) return 0;
        // 텍스트로 보내면 MessagePack 이 깨진 채 들어온다 → 조용히 오작동하느니 여기서 끊는다.
        if (r.MessageType == WebSocketMessageType.Text)
            throw new InvalidDataException("WebSocket 은 바이너리 프레임만 받습니다");
        return r.Count;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        => _ws.SendAsync(frame, WebSocketMessageType.Binary, endOfMessage: true, ct);

    public async ValueTask DisposeAsync()
    {
        if (_ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
            catch (Exception) { /* 이미 끊긴 소켓 */ }
        }
        _ws.Dispose();
    }
}
