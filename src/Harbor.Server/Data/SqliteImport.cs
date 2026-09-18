using Dapper;
using Microsoft.Data.Sqlite;

namespace Harbor.Server.Data;

/// <summary>
/// 옛 SQLite 저장본(`saves/harbor.db`)을 PostgreSQL 로 한 번 옮긴다.
/// `Harbor.Server.exe --import-sqlite=<경로>` 로 실행하고, 끝나면 서버는 뜨지 않는다.
///
/// 옛 스키마는 `account` 한 테이블에 비밀번호와 루피가 섞여 있었다. 여기서 **account(웹) / player(게임)** 로 쪼갠다.
/// 여러 번 돌려도 안전하다(전부 UPSERT). 다만 **가방과 방 가구는 옮기는 쪽 값으로 덮어쓴다.**
/// </summary>
public static class SqliteImport
{
    public static int Run(string sqlitePath, string pgConnectionString)
    {
        if (!File.Exists(sqlitePath))
        {
            Console.WriteLine($"[import] 파일이 없습니다: {sqlitePath}");
            return 1;
        }

        using var lite = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sqlitePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        lite.Open();

        using var pg = Db.Open(pgConnectionString);
        Db.Migrate(pg);
        using var tx = pg.BeginTransaction();

        int accounts = 0, players = 0, inv = 0, rooms = 0, items = 0, ledger = 0;

        foreach (var a in lite.Query("SELECT nick, salt, hash, rupee, figure, last_allowance, streak FROM account"))
        {
            string nick = (string)a.nick, key = Db.Key(nick);
            pg.Execute("""
                INSERT INTO account (nick_key, nick, salt, hash, updated_at) VALUES (@key, @nick, @salt, @hash, now())
                ON CONFLICT (nick_key) DO UPDATE SET nick = EXCLUDED.nick, salt = EXCLUDED.salt, hash = EXCLUDED.hash, updated_at = now()
                """, new { key, nick, salt = (string)a.salt, hash = (string)a.hash }, tx);
            accounts++;

            pg.Execute("""
                INSERT INTO player (nick_key, rupee, figure, last_allowance, streak, updated_at)
                VALUES (@key, @rupee, @figure, @last, @streak, now())
                ON CONFLICT (nick_key) DO UPDATE SET
                    rupee = EXCLUDED.rupee, figure = EXCLUDED.figure,
                    last_allowance = EXCLUDED.last_allowance, streak = EXCLUDED.streak, updated_at = now()
                """,
                new
                {
                    key, rupee = (long)a.rupee, figure = (string)a.figure,
                    last = (string)a.last_allowance, streak = (int)(long)a.streak,
                }, tx);
            players++;
        }

        foreach (var i in lite.Query("SELECT nick, furni_id, qty FROM inventory"))
        {
            string key = Db.Key((string)i.nick);
            pg.Execute("""
                INSERT INTO inventory (nick_key, furni_id, qty) VALUES (@key, @furniId, @qty)
                ON CONFLICT (nick_key, furni_id) DO UPDATE SET qty = EXCLUDED.qty
                """, new { key, furniId = (string)i.furni_id, qty = (int)(long)i.qty }, tx);
            inv++;
        }

        foreach (var r in lite.Query("SELECT room_key, template_id, name, owner_nick FROM room"))
        {
            string key = (string)r.room_key;
            pg.Execute("""
                INSERT INTO room (room_key, template_id, name, owner_nick, updated_at)
                VALUES (@key, @templateId, @name, @ownerNick, now())
                ON CONFLICT (room_key) DO UPDATE SET
                    template_id = EXCLUDED.template_id, name = EXCLUDED.name, owner_nick = EXCLUDED.owner_nick, updated_at = now()
                """, new { key, templateId = (string)r.template_id, name = (string)r.name, ownerNick = (string)r.owner_nick }, tx);
            rooms++;
            pg.Execute("DELETE FROM room_item WHERE room_key = @key", new { key }, tx);   // 옮기는 쪽이 진실
        }

        foreach (var it in lite.Query("SELECT room_key, furni_id, x, y, dir, wall_u, wall_v, tilt, state, state_at, extra, author FROM room_item ORDER BY id"))
        {
            pg.Execute("""
                INSERT INTO room_item (room_key, furni_id, x, y, dir, wall_u, wall_v, tilt, state, state_at, extra, author)
                VALUES (@key, @furniId, @x, @y, @dir, @wallU, @wallV, @tilt, @state, @stateAt, @extra, @author)
                """,
                new
                {
                    key = (string)it.room_key, furniId = (string)it.furni_id,
                    x = (int)(long)it.x, y = (int)(long)it.y, dir = (int)(long)it.dir,
                    wallU = (int)(long)it.wall_u, wallV = (int)(long)it.wall_v, tilt = (int)(long)it.tilt,
                    state = (string)it.state, stateAt = (string?)it.state_at ?? "",
                    extra = (string?)it.extra, author = (string)it.author,
                }, tx);
            items++;
        }

        // 원장은 히스토리다 — 이미 옮겼다면 다시 넣지 않는다.
        long already = pg.ExecuteScalar<long>("SELECT COUNT(*) FROM currency_log", transaction: tx);
        if (already == 0)
        {
            foreach (var c in lite.Query("SELECT nick, delta, balance, reason, at FROM currency_log ORDER BY id"))
            {
                DateTime.TryParse((string)c.at, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at);
                pg.Execute("INSERT INTO currency_log (nick_key, delta, balance, reason, at) VALUES (@key, @delta, @balance, @reason, @at)",
                    new
                    {
                        key = Db.Key((string)c.nick), delta = (long)c.delta, balance = (long)c.balance,
                        reason = (string)c.reason, at = at == default ? DateTime.UtcNow : at.ToUniversalTime(),
                    }, tx);
                ledger++;
            }
        }

        tx.Commit();
        Console.WriteLine($"[import] 계정 {accounts} · 플레이어 {players} · 가방 {inv}줄 · 방 {rooms} · 가구 {items} · 원장 {ledger}줄" +
                          (already > 0 ? " (원장은 이미 있어 건너뜀)" : ""));
        return 0;
    }
}
