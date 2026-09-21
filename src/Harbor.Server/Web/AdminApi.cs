using System.Security.Cryptography;
using System.Text;
using Harbor.Protocol;
using Harbor.Server.Data;
using Harbor.Server.Net;
using Harbor.Server.Rooms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Harbor.Server.Web;

/// <summary>
/// 운영용 관리 API. **살아 있는 상태만** 다룬다 — 계정·공지·로그는 웹(Next.js)이 Postgres 를 직접 본다.
///
/// 왜 게임 서버에 있어야 하는가: 접속 중인 사람의 지갑·가방은 **메모리가 진실**이고
/// `SaveStore` 가 3초마다 내려쓴다. 관리자가 DB 를 직접 고치면 **다음 플러시에 덮어써진다** —
/// 오류도 로그도 없이 "분명 고쳤는데 되돌아가요"로 나타난다. → docs/platform-plan.md §6
///
/// 인증: `HARBOR_ADMIN_TOKEN` 을 `Authorization: Bearer …` 로. **토큰이 없으면 전체를 닫는다.**
/// 이 토큰은 브라우저로 내려보내지 않는다 — Next.js **서버 라우트**에서만 부른다.
/// </summary>
public static class AdminApi
{
    public sealed record GrantReq(string Nick, long Rupee, string? FurniId, int Qty, string Reason);
    public sealed record NickReq(string Nick, string? Reason);
    public sealed record TextReq(string Text);

    public static void MapAdmin(this WebApplication app)
    {
        var token = (Environment.GetEnvironmentVariable("HARBOR_ADMIN_TOKEN") ?? "").Trim();
        var expected = token.Length > 0 ? Encoding.UTF8.GetBytes(token) : null;

        if (expected is null)
        {
            Console.WriteLine("[Harbor] HARBOR_ADMIN_TOKEN 이 없어 /admin 을 열지 않습니다.");
            return;                       // 조용히 열려 있는 것보다 닫혀 있는 편이 낫다
        }

        // 기동 시각은 **여기서** 잡는다. static 필드에 두면 beforefieldinit 때문에
        // 첫 요청 때 초기화되어 가동 시간이 항상 0 부터 시작한다(오류는 안 나고 값만 틀린다).
        var started = DateTime.UtcNow;

        var admin = app.MapGroup("/admin");
        admin.AddEndpointFilter(async (ctx, next) =>
            Authorized(ctx.HttpContext, expected) ? await next(ctx) : Results.Unauthorized());

        // ---------- 보기 ----------
        admin.MapGet("/status", (RoomManager rooms, OnlineUsers online, SaveStore save) => Results.Ok(new
        {
            online = online.Count,
            rooms = rooms.All.Count(),
            users = save.UserCount,
            uptimeSeconds = (int)(DateTime.UtcNow - started).TotalSeconds,
            storage = save.Describe(),
        }));

        admin.MapGet("/online", (OnlineUsers online) => Results.Ok(online.Sessions.Select(s => new
        {
            nick = s.Nick,
            room = s.Room?.Name ?? "",
            roomKind = s.Room?.Def.Kind ?? "",
            rupee = s.Rupee,
            remote = s.Remote,                       // tcp … / ws … — 어느 클라이언트인지 바로 보인다
            connectedUtc = s.ConnectedUtc,
        }).OrderBy(u => u.nick)));

        admin.MapGet("/room", (string nick, RoomManager rooms) =>
        {
            var room = rooms.FindHomeByNick(nick ?? "");
            if (room is null) return Results.NotFound(new { ok = false, reason = "그 닉의 방이 없습니다" });
            return Results.Ok(new { room.Id, room.Name, template = room.Def.RoomId, owner = room.OwnerNick, users = room.UserCount });
        });

        // ---------- 명령 ----------
        admin.MapPost("/broadcast", (TextReq req, OnlineUsers online) =>
        {
            var text = (req.Text ?? "").Trim();
            if (text.Length is 0 or > 200) return Results.BadRequest(new { ok = false, reason = "1~200자" });
            int n = 0;
            foreach (var s in online.Sessions) { s.Notice(text); n++; }   // 기존 S_Notice 를 쓴다 — 새 opcode 불필요
            return Results.Ok(new { ok = true, sent = n });
        });

        admin.MapPost("/kick", async (NickReq req, OnlineUsers online) =>
        {
            var s = online.Find((req.Nick ?? "").Trim());
            if (s is null) return Results.NotFound(new { ok = false, reason = "접속 중이 아닙니다" });
            s.Notice(req.Reason is { Length: > 0 } r ? $"운영자: {r}" : "운영자에 의해 접속이 종료되었습니다.");
            await Task.Delay(200);        // 안내가 실제로 나가도록 잠깐 둔다 — 바로 끊으면 송신 루프째 취소된다
            s.Close();
            return Results.Ok(new { ok = true, nick = s.Nick });
        });

        // 지급은 **반드시 세션/SaveStore 를 거친다**. 이유를 함께 남겨야 currency_log 로 나중에 따질 수 있다.
        admin.MapPost("/grant", (GrantReq req, OnlineUsers online, SaveStore save, DefinitionStore defs) =>
        {
            var nick = (req.Nick ?? "").Trim();
            var reason = (req.Reason ?? "").Trim();
            if (nick.Length == 0) return Results.BadRequest(new { ok = false, reason = "닉이 필요합니다" });
            if (reason.Length == 0) return Results.BadRequest(new { ok = false, reason = "사유가 필요합니다" });
            if (req.FurniId is { Length: > 0 } fid && !defs.Furni.ContainsKey(fid))
                return Results.BadRequest(new { ok = false, reason = $"없는 아이템: {fid}" });

            if (online.Find(nick) is { } s)
            {
                long bal = req.Rupee != 0 ? s.RupeeAdd(req.Rupee, $"운영:{reason}") : s.Rupee;
                if (req.FurniId is { Length: > 0 } f && req.Qty != 0)
                {
                    int qty = s.InvAdd(f, req.Qty);
                    s.SendInventoryUpdate(defs.Furni[f], qty);
                }
                if (req.Rupee != 0) s.SendWallet();
                s.Notice($"운영자 지급: {reason}");
                return Results.Ok(new { ok = true, online = true, rupee = bal });
            }

            // 접속 중이 아니면 저장본을 고친다. 로그인하면 여기서 복원되므로 결과가 같다.
            // (접속과 겹칠 위험은 위에서 걸렀다 — 그 찰나에 로그인하면 지급이 밀릴 수 있으나 유실되진 않는다.)
            var u = save.GetUser(nick);
            if (u is null) return Results.NotFound(new { ok = false, reason = "없는 닉입니다" });
            var inv = new Dictionary<string, int>(u.Inv);
            if (req.FurniId is { Length: > 0 } fo && req.Qty != 0)
            {
                int q = Math.Max(0, inv.GetValueOrDefault(fo) + req.Qty);
                if (q == 0) inv.Remove(fo); else inv[fo] = q;
            }
            long after = Math.Max(0, u.Rupee + req.Rupee);
            save.UpdateUser(nick, after, inv, u.LastAllowance, u.Figure, u.Streak);
            if (req.Rupee != 0) save.LogCurrency(nick, after - u.Rupee, after, $"운영:{reason}");
            return Results.Ok(new { ok = true, online = false, rupee = after });
        });

        admin.MapPost("/reload-defs", (DefinitionStore defs) =>
        {
            // 깨진 JSON 하나가 서버를 죽이지 않도록, 실패해도 기존 정의를 유지한다.
            try
            {
                int before = defs.Furni.Count;
                defs.Reload();
                return Results.Ok(new { ok = true, furni = defs.Furni.Count, rooms = defs.Rooms.Count, wasFurni = before });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { ok = false, reason = "정의를 읽지 못했습니다(기존 유지)", detail = ex.Message });
            }
        });

        Console.WriteLine("[Harbor] /admin 열림 (Bearer 토큰 필요)");
    }

    /// <summary>길이가 달라도 시간이 새지 않도록 해시를 비교한다.</summary>
    private static bool Authorized(HttpContext http, byte[] expected)
    {
        string header = http.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var given = Encoding.UTF8.GetBytes(header[prefix.Length..].Trim());
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(given), SHA256.HashData(expected));
    }
}
