using System.Net;
using System.Net.Sockets;
using Harbor.Server.Config;
using Harbor.Server.Data;
using Harbor.Server.Rooms;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Harbor.Server.Net;

public sealed class TcpHost : BackgroundService
{
    private readonly ServerOptions _opt;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger<TcpHost> _log;
    private readonly ILoggerFactory _lf;
    private readonly SaveStore _save;
    private readonly OnlineUsers _online;

    public TcpHost(IOptions<ServerOptions> opt, Dispatcher dispatcher, ILogger<TcpHost> log, ILoggerFactory lf, SaveStore save, OnlineUsers online)
    { _opt = opt.Value; _dispatcher = dispatcher; _log = log; _lf = lf; _save = save; _online = online; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, _opt.Port);
        listener.Start();
        _log.LogInformation("Harbor server listening on :{Port}", _opt.Port);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var tcp = await listener.AcceptTcpClientAsync(ct);
                tcp.NoDelay = true;
                _ = HandleClient(tcp);
            }
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    private async Task HandleClient(TcpClient tcp)
    {
        await using var session = new Session(tcp, _lf.CreateLogger<Session>(), _save);
        _log.LogInformation("session {Id} connected from {EP}", session.Id, tcp.Client.RemoteEndPoint);
        await session.ReceiveLoop(_dispatcher);
        session.Room?.Post(new RoomCommand.Leave(session));
        _online.Release(session.Nick, session.Id);      // 반드시 놓아야 그 닉으로 다시 들어올 수 있다
        _log.LogInformation("session {Id} disconnected", session.Id);
    }
}
