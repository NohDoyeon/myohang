using Dapper;
using Npgsql;

namespace Harbor.Server.Data;

/// <summary>
/// 친구 관계. **`SaveStore` 와 달리 메모리에 들고 있지 않고 DB 를 직접 본다.**
///
/// 지갑·가방은 매 틱 바뀌므로 메모리가 진실이고 3초마다 내려쓴다. 친구는 다르다 —
/// 하루에 몇 번 바뀌고, 항상 최신이어야 하며(상대가 수락한 걸 바로 봐야 한다),
/// 목록도 짧다. 이런 데이터를 더티 집합에 얹으면 복잡도만 늘고 얻는 게 없다.
///
/// **한 관계를 두 줄로** 저장한다(A→B, B→A). 그래야 "내 친구 목록"이 `WHERE nick_key = 나` 하나로 끝나고,
/// 신청한 쪽(`sent`)과 받은 쪽(`pending`)이 서로 다른 상태를 가질 수 있다.
/// </summary>
public sealed class Friends
{
    public const int MaxFriends = 100;

    private readonly SaveStore _save;
    public Friends(SaveStore save) => _save = save;

    public sealed class Row
    {
        public string FriendKey { get; set; } = "";
        public string Nick { get; set; } = "";
        public string State { get; set; } = "";
        public int Fame { get; set; }
    }

    private NpgsqlConnection Open() => _save.OpenConnection();

    /// <summary>내 친구·신청 목록. 상대의 표시용 닉과 인기도를 함께 가져온다.</summary>
    public List<Row> List(string nick)
    {
        using var conn = Open();
        return conn.Query<Row>("""
            SELECT f.friend_key AS FriendKey,
                   COALESCE(a.nick, f.friend_key) AS Nick,
                   f.state AS State,
                   COALESCE(p.fame, 0) AS Fame
            FROM friend f
            LEFT JOIN account a ON a.nick_key = f.friend_key
            LEFT JOIN player  p ON p.nick_key = f.friend_key
            WHERE f.nick_key = @me
            ORDER BY f.state, Nick
            """, new { me = Db.Key(nick) }).AsList();
    }

    public int Count(string nick)
    {
        using var conn = Open();
        return conn.ExecuteScalar<int>("SELECT count(*) FROM friend WHERE nick_key = @me", new { me = Db.Key(nick) });
    }

    /// <summary>신청. 상대가 이미 나에게 신청해 둔 상태면 **곧바로 맺어진다**(서로 신청 = 수락).</summary>
    public string Request(string meNick, string otherNick)
    {
        string me = Db.Key(meNick), other = Db.Key(otherNick);
        if (me == other) return "자기 자신은 추가할 수 없어요";

        using var conn = Open();
        if (conn.ExecuteScalar<int>("SELECT count(*) FROM account WHERE nick_key = @other", new { other }) == 0)
            return "그런 닉네임이 없어요";

        var mine = conn.ExecuteScalar<string?>(
            "SELECT state FROM friend WHERE nick_key = @me AND friend_key = @other", new { me, other });
        if (mine == "accepted") return "이미 친구예요";
        if (mine == "sent") return "이미 신청했어요";

        // 상대가 먼저 신청해 둔 경우 → 바로 수락으로 친다. 두 번 눌러야 하는 건 번거롭기만 하다.
        if (mine == "pending") { Accept(meNick, otherNick); return ""; }

        if (Count(meNick) >= MaxFriends) return $"친구는 {MaxFriends}명까지예요";

        using var tx = conn.BeginTransaction();
        conn.Execute("""
            INSERT INTO friend (nick_key, friend_key, state, at) VALUES (@me, @other, 'sent', now())
            ON CONFLICT (nick_key, friend_key) DO UPDATE SET state = 'sent', at = now();
            INSERT INTO friend (nick_key, friend_key, state, at) VALUES (@other, @me, 'pending', now())
            ON CONFLICT (nick_key, friend_key) DO UPDATE SET state = 'pending', at = now();
            """, new { me, other }, tx);
        tx.Commit();
        return "";
    }

    public void Accept(string meNick, string otherNick)
    {
        string me = Db.Key(meNick), other = Db.Key(otherNick);
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        // 양쪽을 함께 바꾼다. 한쪽만 accepted 로 남으면 목록이 서로 다르게 보인다.
        conn.Execute("""
            UPDATE friend SET state = 'accepted', at = now()
            WHERE (nick_key = @me AND friend_key = @other) OR (nick_key = @other AND friend_key = @me)
            """, new { me, other }, tx);
        tx.Commit();
    }

    /// <summary>거절·삭제 — 둘 다 **양쪽 줄을 지운다.** 한쪽만 지우면 상대 목록에 유령이 남는다.</summary>
    public void Remove(string meNick, string otherNick)
    {
        string me = Db.Key(meNick), other = Db.Key(otherNick);
        using var conn = Open();
        conn.Execute("""
            DELETE FROM friend
            WHERE (nick_key = @me AND friend_key = @other) OR (nick_key = @other AND friend_key = @me)
            """, new { me, other });
    }
}
