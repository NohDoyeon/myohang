using Harbor.Server.Config;
using Harbor.Server.Data;
using Harbor.Server.Net;
using Harbor.Server.Rooms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace Harbor.Server.Web;

public sealed record LoginRequest(string Nick, string Password);

/// <summary>
/// 랜딩 페이지 + 로그인 API. 브라우저에서 로그인하면 **일회용 입장권**을 주고,
/// 게임 클라이언트는 비밀번호가 아니라 그 입장권으로 접속한다.
/// </summary>
public static class WebApi
{
    public static void MapHarbor(this WebApplication app, string webRoot)
    {
        if (Directory.Exists(webRoot))
        {
            var files = new PhysicalFileProvider(webRoot);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
        }

        app.MapPost("/api/login", (LoginRequest req, HttpContext http, Accounts accounts, TicketStore tickets, OnlineUsers online, SaveStore save) =>
        {
            var nick = (req.Nick ?? "").Trim();
            var ip = http.Connection.RemoteIpAddress?.ToString() ?? "";
            // 가입은 여기(웹)에서만 일어난다 — account 테이블의 주인은 웹이다.
            var result = accounts.Authenticate(nick, req.Password ?? "", out bool created, allowCreate: true);
            if (result != Accounts.Result.Ok)
            {
                save.LogLogin(nick, "web", false, Accounts.Message(result), ip);
                return Results.BadRequest(new { ok = false, reason = Accounts.Message(result) });
            }

            // 이미 게임에 접속 중이면 입장권을 줘도 어차피 막힌다 — 여기서 미리 알려준다.
            if (online.IsOnline(nick))
            {
                save.LogLogin(nick, "web", false, "이미 접속 중", ip);
                return Results.Conflict(new { ok = false, reason = "이미 게임에 접속 중인 닉네임이에요" });
            }
            save.LogLogin(nick, "web", true, created ? "가입" : "", ip);

            return Results.Ok(new { ok = true, nick, created, ticket = tickets.Issue(nick), expiresInSeconds = (int)TicketStore.Lifetime.TotalSeconds });
        });

        // ----- 브라우저용 게임 연결 -----
        //
        // 브라우저는 raw TCP 를 열 수 없다. 그래서 웹 클라이언트는 TCP 30000 대신 여기로 붙는다.
        // **프로토콜은 완전히 같다** — 같은 프레임, 같은 opcode, 같은 Dispatcher, 같은 Session.
        // 로그인도 그대로 `C_Login` 으로 한다(주소에 입장권을 싣지 않는다 — URL 은 로그·히스토리에 남는다).
        //
        // ⚠ 공개할 때는 반드시 **wss**(TLS)여야 한다. HTTPS 페이지는 ws:// 를 거부하고,
        //    무엇보다 지금 연결은 평문이라 비밀번호가 그대로 흐른다 → docs/web-client-plan.md §3
        app.Map("/ws", async (HttpContext http, Dispatcher dispatcher, ILoggerFactory lf,
                              SaveStore save, OnlineUsers online) =>
        {
            if (!http.WebSockets.IsWebSocketRequest)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                await http.Response.WriteAsync("WebSocket 전용 주소입니다");
                return;
            }

            using var ws = await http.WebSockets.AcceptWebSocketAsync();
            var log = lf.CreateLogger("Harbor.Ws");
            var remote = http.Connection.RemoteIpAddress?.ToString() ?? "?";
            await SessionRunner.Run(new WebSocketTransport(ws, remote), dispatcher, lf, save, online, log);
        });

        // gamePort 는 랜딩 페이지가 입장권에 서버 주소를 실어 주기 위해 쓴다.
        // (주소가 없으면 테스터 PC 의 클라이언트가 127.0.0.1 = 자기 자신에게 접속을 시도한다.)
        app.MapGet("/api/status", (RoomManager rooms, OnlineUsers online, IOptions<ServerOptions> opt) => Results.Ok(new
        {
            online = online.Count,
            rooms = rooms.All.Count(),
            publicRooms = rooms.All.Count(r => r.Def.Kind == "public"),
            gamePort = opt.Value.Port,
        }));
    }
}
