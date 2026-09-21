using Harbor.Server.Data;
using Harbor.Server.Rooms;
using Microsoft.Extensions.Logging;

namespace Harbor.Server.Net;

/// <summary>
/// 접속 하나의 일생. **TCP 든 WebSocket 이든 여기를 지난다.**
///
/// 두 진입점이 각자 정리 코드를 들고 있으면 반드시 어긋난다. 특히 `OnlineUsers.Release` 를 빠뜨리면
/// **그 닉으로 다시 들어올 수 없게 되고**, 증상은 한참 뒤에 "로그인이 안 돼요"로 나타난다.
/// 그래서 한 곳에 모아 둔다.
/// </summary>
public static class SessionRunner
{
    public static async Task Run(ITransport transport, Dispatcher dispatcher, ILoggerFactory lf,
                                 SaveStore save, OnlineUsers online, ILogger log)
    {
        await using var session = new Session(transport, lf.CreateLogger<Session>(), save);
        log.LogInformation("session {Id} connected from {Remote}", session.Id, transport.Remote);

        await session.ReceiveLoop(dispatcher);

        session.Room?.Post(new RoomCommand.Leave(session));
        online.Release(session.Nick, session.Id);      // 반드시 놓아야 그 닉으로 다시 들어올 수 있다
        log.LogInformation("session {Id} disconnected", session.Id);
    }
}
