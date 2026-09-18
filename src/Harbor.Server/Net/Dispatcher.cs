using Harbor.Protocol;
using Microsoft.Extensions.Logging;

namespace Harbor.Server.Net;

public delegate Task PacketHandler(Session s, ReadOnlyMemory<byte> body);

/// <summary>opcode → 핸들러 테이블.</summary>
public sealed class Dispatcher
{
    private readonly Dictionary<Opcode, PacketHandler> _map = new();
    private readonly ILogger<Dispatcher> _log;
    public Dispatcher(ILogger<Dispatcher> log) => _log = log;

    public void On<T>(Opcode op, Func<Session, T, Task> handler)
        => _map[op] = (s, body) => handler(s, Framing.Deserialize<T>(body));

    public async Task Dispatch(Session s, Opcode op, ReadOnlyMemory<byte> body)
    {
        if (!_map.TryGetValue(op, out var h))
        {
            _log.LogWarning("unhandled opcode {Op} from session {Id}", op, s.Id);
            s.Send(Opcode.S_Error, new S_Error { Code = 1, Message = $"unhandled {op}" });
            return;
        }
        try { await h(s, body); }
        catch (Exception ex)
        {
            _log.LogError(ex, "handler {Op} failed (session {Id})", op, s.Id);
            s.Send(Opcode.S_Error, new S_Error { Code = 2, Message = "internal error" });
        }
    }
}
