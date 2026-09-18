using Harbor.Core;
using Harbor.Protocol;
using Harbor.Server.Config;
using Harbor.Server.Data;
using Harbor.Server.Net;
using Harbor.Server.Rooms;

namespace Harbor.Server.Handlers;

/// <summary>opcode 핸들러 등록. 룸 관련은 RoomCommand 로 위임. 경제(가방/지갑/상점)는 세션 상태(메모리).</summary>
public static class HandlerRegistration
{
    private static long _nextUserId;

    public static void Register(Dispatcher d, RoomManager rooms, DefinitionStore defs, EconomyOptions eco, SaveStore save,
                                OnlineUsers online, Accounts accounts, TicketStore tickets, ServerOptions srv)
    {
        // ----- Auth -----
        // Token 에는 웹에서 받은 일회용 입장권이 오거나, 클라이언트 시작 화면에서 친 비밀번호가 온다.
        // 닉으로 방·지갑을 찾으므로 검증이 없으면 남의 닉을 입력하는 것만으로 남의 방을 가져간다.
        d.On<C_Login>(Opcode.C_Login, (s, p) =>
        {
            var nick = p.Login.Trim();
            var secret = p.Token ?? "";

            bool byTicket = false;
            if (tickets.Redeem(secret) is { } ticketNick) { nick = ticketNick; byTicket = true; }   // 웹 로그인으로 이미 인증됨
            else
            {
                // 원칙은 "가입은 웹에서만"(account 테이블의 주인이 하나여야 한다).
                // AllowGameSignup 은 테스트 편의용 예외 — 공개 운영에서는 끈다.
                var r = accounts.Authenticate(nick, secret, out _, allowCreate: srv.AllowGameSignup);
                if (r != Accounts.Result.Ok)
                {
                    save.LogLogin(nick, "game", false, Accounts.Message(r));
                    s.Send(Opcode.S_LoginResult, new S_LoginResult { Ok = false, Reason = Accounts.Message(r) });
                    return Task.CompletedTask;
                }
            }

            if (!online.TryClaim(nick, s.Id))
            {
                save.LogLogin(nick, "game", false, "이미 접속 중");
                s.Send(Opcode.S_LoginResult, new S_LoginResult { Ok = false, Reason = "이미 접속 중인 닉네임이에요" });
                return Task.CompletedTask;
            }
            // 어떤 경로로 들어왔는지 남긴다 — 웹 로그인이 실제로 동작하는지 로그만 보고 알 수 있어야 한다.
            save.LogLogin(nick, "game", true, byTicket ? "입장권" : "비밀번호");

            s.UserId = Interlocked.Increment(ref _nextUserId);
            s.Nick = nick;
            var account = save.GetUser(nick);
            bool isNew = account is null || account.LastAllowance.Length == 0;       // 계정은 있어도 아직 플레이 전일 수 있다

            // 개인 방: 같은 닉이면 이전 방(가구 포함) 재사용 — 영속 계층 전의 임시 방책
            var home = rooms.FindHomeByNick(nick) ?? rooms.Create(eco.HomeTemplate, s.UserId, nick, $"{nick}의 방");
            home.OwnerId = s.UserId;
            s.HomeRoomId = home.Id;

            // 저장된 지갑·가방이 있으면 복원, 없으면 첫 방문으로 보고 시작 자금·가구 지급
            string notice;
            var todayDate = DateOnly.FromDateTime(DateTime.Now);
            string today = todayDate.ToString("yyyy-MM-dd");
            if (!isNew && account is { } prev)
            {
                s.RestoreEconomy(prev.Rupee, prev.Inv, prev.LastAllowance, prev.Figure, prev.Streak);
                // 출석: 어제 왔으면 이어지고, 하루라도 건너뛰면 1일차부터. 보너스는 7일차에서 멈춘다.
                DateOnly? last = DateOnly.TryParseExact(prev.LastAllowance, "yyyy-MM-dd", out var lastDate) ? lastDate : null;
                int streak = Attendance.NextStreak(last, todayDate, prev.Streak);
                s.Streak = streak;
                if (prev.LastAllowance == today) notice = $"다시 오셨네요. 오늘 출석({Attendance.Describe(streak)})은 이미 하셨어요.";
                else
                {
                    long reward = Attendance.Reward(eco.DailyAllowance, eco.StreakStep, streak);
                    long now = s.RupeeAdd(reward, $"출석 {streak}일차");
                    s.LastAllowance = today;
                    notice = $"{Attendance.Describe(streak)}! 출석 보상 {reward:N0} 루피를 받았어요. (잔액 {now:N0})";
                }
            }
            else
            {
                s.RupeeSet(eco.StartingRupee, "시작 자금");
                s.LastAllowance = today;                      // 시작 자금을 받은 날은 출석도 한 셈
                s.Streak = 1;
                foreach (var (id, qty) in eco.StarterItems) if (defs.Furni.ContainsKey(id)) s.InvAdd(id, qty);
                notice = $"처음 오셨네요. 시작 자금 {eco.StartingRupee:N0} 루피와 가구 몇 개를 드렸어요.";
            }

            s.Send(Opcode.S_LoginResult, new S_LoginResult { Ok = true, UserId = s.UserId, HomeRoomId = home.Id, Notice = notice, Nick = nick });
            s.SendWallet();
            s.Send(Opcode.S_Inventory, Inventory(s, defs));
            s.Send(Opcode.S_Catalog, Catalog(defs));
            return Task.CompletedTask;
        });
        d.On<Empty>(Opcode.C_Ping, (s, _) => { s.Send(Opcode.S_Pong, new Empty()); return Task.CompletedTask; });

        // ----- Room -----
        d.On<C_EnterRoom>(Opcode.C_EnterRoom, (s, p) =>
        {
            if (s.UserId == 0) return Task.CompletedTask;
            var room = rooms.Get(p.RoomId) ?? rooms.Get(s.HomeRoomId) ?? rooms.All.FirstOrDefault() ?? rooms.Create(eco.HomeTemplate);
            if (s.Room == room) return Task.CompletedTask;
            s.Room?.Post(new RoomCommand.Leave(s));
            room.Post(new RoomCommand.Enter(s));
            return Task.CompletedTask;
        });
        d.On<Empty>(Opcode.C_LeaveRoom, (s, _) => { s.Room?.Post(new RoomCommand.Leave(s)); return Task.CompletedTask; });
        d.On<Empty>(Opcode.C_RoomList, (s, _) =>
        {
            var list = rooms.All
                .OrderBy(r => r.Def.Kind == "public" ? 0 : r.OwnerId == s.UserId ? 1 : 2)
                .ThenBy(r => r.Id)
                .Select(r => r.ToInfo()).ToList();
            s.Send(Opcode.S_RoomList, new S_RoomList { Rooms = list });
            return Task.CompletedTask;
        });
        d.On<C_Move>(Opcode.C_Move, (s, p) => { s.Room?.Post(new RoomCommand.Move(s, p.X, p.Y)); return Task.CompletedTask; });
        d.On<C_Action>(Opcode.C_Action, (s, p) => { s.Room?.Post(new RoomCommand.Action(s, p.Action)); return Task.CompletedTask; });
        d.On<C_SetFigure>(Opcode.C_SetFigure, (s, p) =>
        {
            if (s.UserId == 0) return Task.CompletedTask;
            if (!Harbor.Core.Figure.IsValid(p.Figure)) { s.Send(Opcode.S_Error, new S_Error { Code = 40, Message = "bad figure" }); return Task.CompletedTask; }
            s.Figure = p.Figure;
            s.Room?.Post(new RoomCommand.Figure(s));
            return Task.CompletedTask;
        });
        d.On<C_Chat>(Opcode.C_Chat, (s, p) => { if (!string.IsNullOrWhiteSpace(p.Text)) s.Room?.Post(new RoomCommand.Chat(s, p.Text, p.Kind)); return Task.CompletedTask; });

        // ----- Items -----
        d.On<C_PlaceItem>(Opcode.C_PlaceItem, (s, p) => { s.Room?.Post(new RoomCommand.PlaceItem(s, p.FurniId, p.X, p.Y, p.Dir, p.WallU, p.WallV, p.Tilt)); return Task.CompletedTask; });
        d.On<C_PickItem>(Opcode.C_PickItem, (s, p) => { s.Room?.Post(new RoomCommand.PickItem(s, p.ItemId)); return Task.CompletedTask; });
        d.On<C_UseItem>(Opcode.C_UseItem, (s, p) => { s.Room?.Post(new RoomCommand.UseItem(s, p.ItemId)); return Task.CompletedTask; });
        d.On<C_OfferItem>(Opcode.C_OfferItem, (s, p) => { s.Room?.Post(new RoomCommand.OfferItem(s, p.ItemId)); return Task.CompletedTask; });
        d.On<C_SetRoomStyle>(Opcode.C_SetRoomStyle, (s, p) => { s.Room?.Post(new RoomCommand.SetStyle(s, p.Wall ?? "", p.Floor ?? "")); return Task.CompletedTask; });
        d.On<C_PostitWrite>(Opcode.C_PostitWrite, (s, p) => { s.Room?.Post(new RoomCommand.PostitWrite(s, p.ItemId, p.Body ?? "")); return Task.CompletedTask; });

        // ----- Economy -----
        d.On<Empty>(Opcode.C_Catalog, (s, _) => { s.Send(Opcode.S_Catalog, Catalog(defs)); return Task.CompletedTask; });
        d.On<Empty>(Opcode.C_Inventory, (s, _) => { s.Send(Opcode.S_Inventory, Inventory(s, defs)); return Task.CompletedTask; });
        d.On<C_BuyCatalog>(Opcode.C_BuyCatalog, (s, p) =>
        {
            if (s.UserId == 0) return Task.CompletedTask;
            int qty = Math.Clamp(p.Qty, 1, 10);
            if (!defs.Furni.TryGetValue(p.FurniId, out var def)) { s.Send(Opcode.S_Error, new S_Error { Code = 20, Message = "unknown furni" }); return Task.CompletedTask; }
            // 값이 0 인 것은 상점 물건이 아니라 수확물이다 — 그냥 두면 공짜로 받아 팔 수 있다.
            if (def.Price.Currency != "rupee" || def.Price.Amount <= 0) { s.Send(Opcode.S_Error, new S_Error { Code = 31, Message = "not for sale" }); return Task.CompletedTask; }
            if (!s.RupeeTrySpend(def.Price.Amount * qty, $"상점 구매 {def.FurniId}×{qty}")) { s.Send(Opcode.S_Error, new S_Error { Code = 30, Message = "not enough rupee" }); return Task.CompletedTask; }
            s.SendWallet();
            s.SendInventoryUpdate(def, s.InvAdd(def.FurniId, qty));
            return Task.CompletedTask;
        });

        // ----- 수확물 팔기 -----
        // 값을 매기는 것은 Harbor.Core.Trade — 묶음을 먼저 채우므로 모아서 팔수록 이득이다.
        d.On<C_SellItem>(Opcode.C_SellItem, (s, p) =>
        {
            if (s.UserId == 0) return Task.CompletedTask;
            if (!defs.Furni.TryGetValue(p.FurniId, out var def)) { s.Send(Opcode.S_Error, new S_Error { Code = 20, Message = "unknown furni" }); return Task.CompletedTask; }
            if (def.Sell is not { Price: > 0 } sell) { s.Send(Opcode.S_Error, new S_Error { Code = 32, Message = "not sellable" }); return Task.CompletedTask; }

            int have = s.InvQty(def.FurniId);
            int qty = Math.Clamp(p.Qty, 1, Math.Max(have, 1));
            if (have < qty || !s.InvTryTakeMany(def.FurniId, qty, out int remaining))
            { s.Send(Opcode.S_Error, new S_Error { Code = 24, Message = "not in inventory" }); return Task.CompletedTask; }

            var quote = Trade.QuoteSell(qty, sell.Price, sell.BundleQty, sell.BundlePrice);
            long now = s.RupeeAdd(quote.Total, $"판매 {def.FurniId}×{qty}");
            s.SendWallet();
            s.SendInventoryUpdate(def, remaining);
            s.Notice(quote.Bundles > 0
                ? $"{def.DisplayName} {qty}개를 팔아 {quote.Total:N0} 루피. ({sell.BundleQty}개 묶음 {quote.Bundles}개 포함, 잔액 {now:N0})"
                : $"{def.DisplayName} {qty}개를 팔아 {quote.Total:N0} 루피. (잔액 {now:N0})");
            return Task.CompletedTask;
        });

        // ----- 방 넓히기 -----
        // 값을 먼저 받고, 실제 교체는 룸 루프에서 한다(그 순간의 가구를 그대로 넘기기 위해). 실패하면 되돌려 준다.
        d.On<Empty>(Opcode.C_UpgradeRoom, (s, _) =>
        {
            if (s.UserId == 0 || s.Room is not { } room) return Task.CompletedTask;
            if (room.Def.Kind == "public" || room.OwnerId != s.UserId) { s.Send(Opcode.S_Error, new S_Error { Code = 34, Message = "not your room" }); return Task.CompletedTask; }
            if (room.NextTemplate is not { } next) { s.Send(Opcode.S_Error, new S_Error { Code = 33, Message = "already biggest" }); return Task.CompletedTask; }
            long price = room.Def.UpgradePrice;
            if (!s.RupeeTrySpend(price, $"방 넓히기 {next.RoomId}")) { s.Send(Opcode.S_Error, new S_Error { Code = 30, Message = "not enough rupee" }); return Task.CompletedTask; }
            s.SendWallet();
            rooms.Upgrade(room, s, $"방을 넓혔어요 — {next.Name}. 가구는 그대로 있어요.", price);
            return Task.CompletedTask;
        });

        // ----- 이사 (다른 계열의 집으로) -----
        d.On<Empty>(Opcode.C_HouseList, (s, _) =>
        {
            if (s.UserId == 0) return Task.CompletedTask;
            var home = rooms.Get(s.HomeRoomId);
            int tier = Math.Max(1, home?.Def.Tier ?? 1);
            string family = home?.Def.Family ?? "";
            s.Send(Opcode.S_HouseList, new S_HouseList
            {
                Houses = rooms.HouseStyles(tier).Select(r => new HouseStyle
                {
                    TemplateId = r.RoomId, Name = r.Name, Family = r.Family, Tier = r.Tier,
                    Tiles = Tiles(r), Price = eco.RemodelPrice, Current = r.Family == family,
                }).ToList()
            });
            return Task.CompletedTask;
        });
        d.On<C_RemodelRoom>(Opcode.C_RemodelRoom, (s, p) =>
        {
            if (s.UserId == 0 || s.Room is not { } room) return Task.CompletedTask;
            if (room.Def.Kind == "public" || room.OwnerId != s.UserId) { s.Send(Opcode.S_Error, new S_Error { Code = 34, Message = "not your room" }); return Task.CompletedTask; }
            if (!defs.Rooms.TryGetValue(p.TemplateId, out var asked) || asked.Family.Length == 0 || asked.Kind == "public")
            { s.Send(Opcode.S_Error, new S_Error { Code = 20, Message = "unknown room template" }); return Task.CompletedTask; }
            if (asked.Family == room.Def.Family) { s.Send(Opcode.S_Error, new S_Error { Code = 35, Message = "already this house" }); return Task.CompletedTask; }

            // 단계는 클라가 고른 값을 믿지 않고 서버가 다시 고른다 (지금 단계와 같은 단계, 없으면 그 계열의 마지막 단계).
            var target = rooms.HouseStyles(Math.Max(1, room.Def.Tier)).FirstOrDefault(r => r.Family == asked.Family);
            if (target is null) { s.Send(Opcode.S_Error, new S_Error { Code = 20, Message = "unknown room template" }); return Task.CompletedTask; }
            if (!s.RupeeTrySpend(eco.RemodelPrice, $"이사 {target.RoomId}")) { s.Send(Opcode.S_Error, new S_Error { Code = 30, Message = "not enough rupee" }); return Task.CompletedTask; }
            s.SendWallet();
            rooms.Remodel(room, s, target.RoomId, $"{target.Name}(으)로 이사했어요.", eco.RemodelPrice);
            return Task.CompletedTask;
        });

        // ----- 복권 -----
        d.On<Empty>(Opcode.C_LotteryDraw, (s, _) =>
        {
            if (s.UserId == 0) return Task.CompletedTask;
            if (!s.RupeeTrySpend(eco.LotteryPrice, "복권 구매")) { s.Send(Opcode.S_Error, new S_Error { Code = 30, Message = "not enough rupee" }); return Task.CompletedTask; }
            var prize = Lottery.Draw(Random.Shared.NextDouble(), eco.LotteryJackpot, eco.LotteryPrice);
            if (prize.Amount > 0) s.RupeeAdd(prize.Amount, $"복권 당첨 {Lottery.RankName(prize.Rank)}");
            s.SendWallet();
            s.Send(Opcode.S_LotteryResult, new S_LotteryResult
            {
                Rank = prize.Rank,
                Prize = prize.Amount,
                Price = eco.LotteryPrice,
                Message = prize.Rank switch
                {
                    1 => $"1등! {prize.Amount:N0} 루피 당첨!",
                    2 => $"2등! {prize.Amount:N0} 루피 당첨!",
                    3 => $"3등, {prize.Amount:N0} 루피 당첨.",
                    4 => $"4등, 본전 {prize.Amount:N0} 루피.",
                    _ => "꽝. 다음 기회에…",
                },
            });
            return Task.CompletedTask;
        });
    }

    /// <summary>걸을 수 있는 칸 수 — 집이 얼마나 넓은지 비교해 보여주기 위한 값.</summary>
    private static int Tiles(RoomDef r) => r.Heightmap.Sum(row => row.Count(c => char.ToLowerInvariant(c) != 'x'));

    /// <summary>값이 0 인 정의(수확물)는 상점에 올리지 않는다 — 파는 물건이지 사는 물건이 아니다.</summary>
    private static S_Catalog Catalog(DefinitionStore defs) => new()
    {
        Items = defs.Furni.Values.Where(f => f.Price.Amount > 0).OrderBy(f => f.Category).ThenBy(f => f.Price.Amount)
            .Select(f => new CatalogEntry { FurniId = f.FurniId, Name = f.DisplayName, Category = f.Category, Price = f.Price.Amount, Currency = f.Price.Currency, Wall = f.Wall, Interaction = f.Interaction.Type })
            .ToList()
    };

    private static S_Inventory Inventory(Session s, DefinitionStore defs)
    {
        var list = new List<InventoryEntry>();
        foreach (var (furniId, qty) in s.InvSnapshot())
        {
            var def = defs.Furni.GetValueOrDefault(furniId);
            list.Add(new InventoryEntry
            {
                FurniId = furniId, Qty = qty, Name = def?.DisplayName ?? furniId, Wall = def?.Wall ?? false,
                Interaction = def?.Interaction.Type ?? "",
                SellPrice = def?.Sell?.Price ?? 0, SellBundleQty = def?.Sell?.BundleQty ?? 0, SellBundlePrice = def?.Sell?.BundlePrice ?? 0,
            });
        }
        return new S_Inventory { Items = list };
    }
}
