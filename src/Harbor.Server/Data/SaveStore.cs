using System.Collections.Concurrent;
using Dapper;
using Harbor.Server.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Harbor.Server.Data;

// 메모리에 있는 모양 그대로의 저장 모델. 테이블과 1:1 은 아니고, 읽기 편한 덩어리 단위다.
public sealed class UserSave
{
    public long Rupee { get; set; }
    public Dictionary<string, int> Inv { get; set; } = new();
    /// <summary>마지막으로 출석 보상을 받은 날 (yyyy-MM-dd). 비어 있으면 아직 못 받음.</summary>
    public string LastAllowance { get; set; } = "";
    /// <summary>연속 출석 일수. LastAllowance 와 짝이다.</summary>
    public int Streak { get; set; }
    /// <summary>아바타 외모 (Harbor.Core.Figure 형식). 비어 있으면 기본 외모.</summary>
    public string Figure { get; set; } = "";
    /// <summary>PBKDF2 소금(base64). 비어 있으면 아직 비밀번호가 없는 계정. **account 테이블 = 웹 소유.**</summary>
    public string Salt { get; set; } = "";
    /// <summary>PBKDF2 해시(base64). 평문은 저장하지 않는다.</summary>
    public string Hash { get; set; } = "";
    /// <summary>보여줄 철자의 닉 (키는 소문자).</summary>
    public string Nick { get; set; } = "";
}

public sealed class ItemSave
{
    public string FurniId { get; set; } = "";
    public short X { get; set; }
    public short Y { get; set; }
    public byte Dir { get; set; }
    public byte WallU { get; set; }
    public byte WallV { get; set; }
    public sbyte Tilt { get; set; }
    public string State { get; set; } = "";
    /// <summary>지금 상태가 시작된 시각(UTC, ISO). 시간이 흐르는 가구(식물 등)가 재시작 후에도 이어지도록.</summary>
    public string StateAt { get; set; } = "";
    public string? Extra { get; set; }
    public string Author { get; set; } = "";
}

/// <summary>루피가 오간 한 줄. 잔액까지 같이 남겨 나중에 대조할 수 있게 한다.</summary>
public sealed record CurrencyEntry(string Nick, long Delta, long Balance, string Reason, DateTime AtUtc);

/// <summary>접속 기록 한 줄 (웹·게임 공통). 관리자 화면이 이걸 읽는다.</summary>
public sealed record LoginEntry(string Nick, string Source, bool Ok, string Detail, string Ip, DateTime AtUtc);

public sealed class RoomSave
{
    public string TemplateId { get; set; } = "";
    public string Name { get; set; } = "";
    public string OwnerNick { get; set; } = "";
    public List<ItemSave> Items { get; set; } = new();
}

/// <summary>
/// 지갑·가방·외모·방 배치의 영속 계층 (PostgreSQL).
/// 진실은 메모리에 있고, 바뀐 것만 표시해 두었다가 주기적으로(SaveService) 테이블에 쓴다.
/// 게임 루프가 가방을 자주 건드리므로 호출마다 DB 를 치지 않는다.
///
/// **쓰기 소유권**: 이 클래스는 `player`·`inventory`·`room`·`room_item`·`currency_log` 를 쓴다.
/// `account`(비밀번호)는 **웹이 소유**하므로 여기서는 `SetPassword` 로만, 그것도 웹 로그인 경로에서만 건드린다.
/// </summary>
public sealed class SaveStore
{
    private readonly string _conn;
    private readonly ILogger<SaveStore> _log;

    private readonly ConcurrentDictionary<string, UserSave> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RoomSave> _rooms = new(StringComparer.Ordinal);

    // 바뀐 것만 쓴다. 락은 짧게 잡고 Flush 는 복사본으로 돈다.
    private readonly object _dirtyLock = new();
    private readonly HashSet<string> _dirtyUsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirtyAccounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirtyRooms = new(StringComparer.Ordinal);
    private readonly List<CurrencyEntry> _currencyQueue = new();
    private readonly List<LoginEntry> _loginQueue = new();

    public SaveStore(IOptions<SaveOptions> opt, ILogger<SaveStore> log)
    {
        _log = log;
        _conn = opt.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(_conn))
            throw new InvalidOperationException("DB 접속 문자열이 없습니다. 환경변수 HARBOR_DB 를 설정하세요 (.env.example 참고).");
        Load();
    }

    public int UserCount => _users.Count;
    public int RoomCount => _rooms.Count;

    /// <summary>로그에 찍어도 되는 접속 대상 설명 — 비밀번호는 빼고 호스트/DB 만.</summary>
    public string Describe()
    {
        try
        {
            var b = new NpgsqlConnectionStringBuilder(_conn);
            return $"{b.Host}:{b.Port}/{b.Database}";
        }
        catch { return "(접속 문자열 형식 오류)"; }
    }

    /// <summary>개인 방은 주인 닉, 공용 방은 템플릿 id 로 키를 잡는다 (룸 인스턴스 id 는 재시작마다 바뀌므로).</summary>
    public static string RoomKey(string ownerNick, string templateId)
        => ownerNick.Length > 0 ? "u:" + ownerNick.ToLowerInvariant() : "t:" + templateId;

    // ---------------- 유저 ----------------
    public UserSave? GetUser(string nick) => _users.GetValueOrDefault(Db.Key(nick));

    /// <summary>지갑·가방·외모·출석을 덮어쓴다. 비밀번호(account)는 건드리지 않는다.</summary>
    public void UpdateUser(string nick, long rupee, IEnumerable<KeyValuePair<string, int>> inv, string lastAllowance, string figure, int streak = 0)
    {
        if (string.IsNullOrWhiteSpace(nick)) return;
        string key = Db.Key(nick);
        var prev = _users.GetValueOrDefault(key);
        _users[key] = new UserSave
        {
            Nick = prev?.Nick is { Length: > 0 } n ? n : nick,
            Rupee = rupee, Inv = new Dictionary<string, int>(inv), LastAllowance = lastAllowance, Figure = figure, Streak = streak,
            Salt = prev?.Salt ?? "", Hash = prev?.Hash ?? "",
        };
        lock (_dirtyLock) _dirtyUsers.Add(key);
    }

    /// <summary>
    /// 계정의 비밀번호를 설정한다(가입 또는 변경). **`account` 테이블은 웹 소유**이므로
    /// 게임 접속 경로에서는 호출되지 않는다(Accounts.Authenticate 의 allowCreate 참고).
    /// </summary>
    public void SetPassword(string nick, string salt, string hash)
    {
        if (string.IsNullOrWhiteSpace(nick)) return;
        string key = Db.Key(nick);
        var u = _users.GetValueOrDefault(key) ?? new UserSave();
        u.Nick = u.Nick.Length > 0 ? u.Nick : nick.Trim();
        u.Salt = salt; u.Hash = hash;
        _users[key] = u;
        lock (_dirtyLock) { _dirtyAccounts.Add(key); _dirtyUsers.Add(key); }
    }

    // ---------------- 방 ----------------
    public RoomSave? GetRoom(string key) => _rooms.GetValueOrDefault(key);
    public IEnumerable<KeyValuePair<string, RoomSave>> AllRooms() => _rooms.ToArray();

    public void UpdateRoom(string key, string templateId, string name, string ownerNick, List<ItemSave> items)
    {
        _rooms[key] = new RoomSave { TemplateId = templateId, Name = name, OwnerNick = ownerNick, Items = items };
        lock (_dirtyLock) _dirtyRooms.Add(key);
    }

    /// <summary>루피 변동 한 줄을 원장에 남긴다(다음 flush 에 기록). delta 0 은 무시.</summary>
    public void LogCurrency(string nick, long delta, long balance, string reason)
    {
        if (string.IsNullOrWhiteSpace(nick) || delta == 0) return;
        lock (_dirtyLock) _currencyQueue.Add(new CurrencyEntry(Db.Key(nick), delta, balance, reason, DateTime.UtcNow));
    }

    /// <summary>
    /// 접속 기록. 웹·게임 양쪽에서 부른다. **실패도 남긴다** — 비밀번호를 계속 틀리는 흐름이 보여야 한다.
    /// 게임 루프를 막지 않도록 큐에 쌓고 flush 때 함께 쓴다.
    /// </summary>
    public void LogLogin(string nick, string source, bool ok, string detail = "", string ip = "")
    {
        if (string.IsNullOrWhiteSpace(nick)) return;
        lock (_dirtyLock) _loginQueue.Add(new LoginEntry(Db.Key(nick), source, ok, detail, ip, DateTime.UtcNow));
    }

    // ---------------- 읽기 ----------------
    private void Load()
    {
        using var conn = Db.Open(_conn);
        Db.Migrate(conn);

        // 계정은 있지만 아직 플레이한 적 없는 사람(웹 가입만 한 경우)도 들어와야 하므로 LEFT JOIN.
        foreach (var a in conn.Query("""
            SELECT a.nick_key, a.nick, a.salt, a.hash,
                   COALESCE(p.rupee, 0) AS rupee, COALESCE(p.figure, '') AS figure,
                   COALESCE(p.last_allowance, '') AS last_allowance, COALESCE(p.streak, 0) AS streak
            FROM account a LEFT JOIN player p ON p.nick_key = a.nick_key
            """))
        {
            _users[(string)a.nick_key] = new UserSave
            {
                Nick = (string)a.nick, Salt = (string)a.salt, Hash = (string)a.hash,
                Rupee = (long)a.rupee, Figure = (string)a.figure,
                LastAllowance = (string)a.last_allowance, Streak = (int)a.streak,
            };
        }
        foreach (var i in conn.Query("SELECT nick_key, furni_id, qty FROM inventory"))
            if (_users.TryGetValue((string)i.nick_key, out var u) && (int)i.qty > 0)
                u.Inv[(string)i.furni_id] = (int)i.qty;

        foreach (var r in conn.Query("SELECT room_key, template_id, name, owner_nick FROM room"))
            _rooms[(string)r.room_key] = new RoomSave { TemplateId = (string)r.template_id, Name = (string)r.name, OwnerNick = (string)r.owner_nick };
        foreach (var it in conn.Query("SELECT room_key, furni_id, x, y, dir, wall_u, wall_v, tilt, state, state_at, extra, author FROM room_item ORDER BY id"))
            if (_rooms.TryGetValue((string)it.room_key, out var room))
                room.Items.Add(new ItemSave
                {
                    FurniId = (string)it.furni_id, X = (short)(int)it.x, Y = (short)(int)it.y, Dir = (byte)(int)it.dir,
                    WallU = (byte)(int)it.wall_u, WallV = (byte)(int)it.wall_v, Tilt = (sbyte)(int)it.tilt,
                    State = (string)it.state, StateAt = (string?)it.state_at ?? "", Extra = (string?)it.extra, Author = (string)it.author,
                });

        _log.LogInformation("db loaded: users={U} rooms={R} from {Where}", _users.Count, _rooms.Count, Describe());
    }

    // ---------------- 쓰기 ----------------
    /// <summary>바뀐 행만 쓴다. force 는 바뀐 게 없어도 트랜잭션을 열게 할 뿐, 전체 재작성이 아니다.</summary>
    public void Flush(bool force = false)
    {
        string[] users, accounts, rooms;
        CurrencyEntry[] currency;
        LoginEntry[] logins;
        lock (_dirtyLock)
        {
            if (_dirtyUsers.Count == 0 && _dirtyAccounts.Count == 0 && _dirtyRooms.Count == 0
                && _currencyQueue.Count == 0 && _loginQueue.Count == 0 && !force) return;
            users = _dirtyUsers.ToArray(); _dirtyUsers.Clear();
            accounts = _dirtyAccounts.ToArray(); _dirtyAccounts.Clear();
            rooms = _dirtyRooms.ToArray(); _dirtyRooms.Clear();
            currency = _currencyQueue.ToArray(); _currencyQueue.Clear();
            logins = _loginQueue.ToArray(); _loginQueue.Clear();
        }
        if (users.Length == 0 && accounts.Length == 0 && rooms.Length == 0 && currency.Length == 0 && logins.Length == 0) return;

        try
        {
            using var conn = Db.Open(_conn);
            using var tx = conn.BeginTransaction();

            // account — 웹이 소유하지만, 웹 API 도 지금은 이 프로세스 안에 있다(가입/비밀번호 변경 경로).
            foreach (var key in accounts)
            {
                if (!_users.TryGetValue(key, out var u)) continue;
                conn.Execute("""
                    INSERT INTO account (nick_key, nick, salt, hash, updated_at)
                    VALUES (@key, @nick, @salt, @hash, now())
                    ON CONFLICT (nick_key) DO UPDATE SET
                        nick = EXCLUDED.nick, salt = EXCLUDED.salt, hash = EXCLUDED.hash, updated_at = now()
                    """,
                    new { key, nick = u.Nick.Length > 0 ? u.Nick : key, salt = u.Salt, hash = u.Hash }, tx);
            }

            foreach (var key in users)
            {
                if (!_users.TryGetValue(key, out var u)) continue;
                // 플레이어 행은 계정이 있어야 존재할 수 있다(FK). 게임에서 처음 만난 닉이면 계정 껍데기를 먼저 만든다.
                conn.Execute("""
                    INSERT INTO account (nick_key, nick, updated_at) VALUES (@key, @nick, now())
                    ON CONFLICT (nick_key) DO NOTHING
                    """, new { key, nick = u.Nick.Length > 0 ? u.Nick : key }, tx);

                conn.Execute("""
                    INSERT INTO player (nick_key, rupee, figure, last_allowance, streak, updated_at)
                    VALUES (@key, @rupee, @figure, @lastAllowance, @streak, now())
                    ON CONFLICT (nick_key) DO UPDATE SET
                        rupee = EXCLUDED.rupee, figure = EXCLUDED.figure,
                        last_allowance = EXCLUDED.last_allowance, streak = EXCLUDED.streak, updated_at = now()
                    """,
                    new { key, rupee = u.Rupee, figure = u.Figure, lastAllowance = u.LastAllowance, streak = u.Streak }, tx);

                conn.Execute("DELETE FROM inventory WHERE nick_key = @key", new { key }, tx);
                foreach (var (furniId, qty) in u.Inv)
                    if (qty > 0)
                        conn.Execute("INSERT INTO inventory (nick_key, furni_id, qty) VALUES (@key, @furniId, @qty)", new { key, furniId, qty }, tx);
            }

            foreach (var key in rooms)
            {
                if (!_rooms.TryGetValue(key, out var r)) continue;
                conn.Execute("""
                    INSERT INTO room (room_key, template_id, name, owner_nick, updated_at)
                    VALUES (@key, @templateId, @name, @ownerNick, now())
                    ON CONFLICT (room_key) DO UPDATE SET
                        template_id = EXCLUDED.template_id, name = EXCLUDED.name,
                        owner_nick = EXCLUDED.owner_nick, updated_at = now()
                    """,
                    new { key, templateId = r.TemplateId, name = r.Name, ownerNick = r.OwnerNick }, tx);

                conn.Execute("DELETE FROM room_item WHERE room_key = @key", new { key }, tx);
                foreach (var it in r.Items)
                    conn.Execute("""
                        INSERT INTO room_item (room_key, furni_id, x, y, dir, wall_u, wall_v, tilt, state, state_at, extra, author)
                        VALUES (@key, @furniId, @x, @y, @dir, @wallU, @wallV, @tilt, @state, @stateAt, @extra, @author)
                        """,
                        new
                        {
                            key, furniId = it.FurniId, x = (int)it.X, y = (int)it.Y, dir = (int)it.Dir,
                            wallU = (int)it.WallU, wallV = (int)it.WallV, tilt = (int)it.Tilt,
                            state = it.State, stateAt = it.StateAt, extra = it.Extra, author = it.Author,
                        }, tx);
            }

            foreach (var c in currency)
                conn.Execute("INSERT INTO currency_log (nick_key, delta, balance, reason, at) VALUES (@Nick, @Delta, @Balance, @Reason, @AtUtc)", c, tx);

            foreach (var l in logins)
                conn.Execute("INSERT INTO login_log (nick_key, source, ok, detail, ip, at) VALUES (@Nick, @Source, @Ok, @Detail, @Ip, @AtUtc)", l, tx);

            tx.Commit();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "db flush 실패 — 다음 주기에 다시 시도합니다");
            lock (_dirtyLock)                       // 잃지 않도록 되돌려 놓는다
            {
                foreach (var n in users) _dirtyUsers.Add(n);
                foreach (var n in accounts) _dirtyAccounts.Add(n);
                foreach (var k in rooms) _dirtyRooms.Add(k);
                _currencyQueue.InsertRange(0, currency);
                _loginQueue.InsertRange(0, logins);
            }
        }
    }
}
