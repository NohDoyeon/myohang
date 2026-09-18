using Dapper;
using Npgsql;

namespace Harbor.Server.Data;

/// <summary>
/// PostgreSQL 스키마와 연결.
///
/// **소유권이 테이블로 나뉘어 있다** — 이게 이 스키마의 핵심이다.
///   · `account` = 웹(가입·비밀번호)이 쓴다. 게임 서버는 **읽기만** 한다.
///   · 그 외 전부 = 게임 서버가 쓴다. 웹은 건드리지 않는다.
/// 이렇게 나눠야 웹(Next.js/Vercel)과 게임 서버가 같은 DB 를 보면서도 서로 덮어쓰지 않는다.
/// 예전 SQLite 스키마는 `account` 한 테이블에 비밀번호와 루피가 섞여 있어 이 분리가 불가능했다.
///
/// 닉은 대소문자를 무시한다. citext 확장에 의존하지 않도록 **`nick_key` = 소문자 닉**을 키로 쓰고,
/// 보여줄 철자는 `nick` 에 따로 둔다.
/// </summary>
public static class Db
{
    /// <summary>스키마를 바꾸면 올리고 Migrate 에 단계를 추가한다. 1 = Postgres 최초(계정/게임 분리).</summary>
    public const int SchemaVersion = 1;

    public static NpgsqlConnection Open(string connectionString)
    {
        var conn = new NpgsqlConnection(connectionString);
        conn.Open();
        return conn;
    }

    /// <summary>닉 → 키(소문자). 저장·조회는 전부 이 값으로 한다.</summary>
    public static string Key(string nick) => nick.Trim().ToLowerInvariant();

    public static void Migrate(NpgsqlConnection conn)
    {
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS schema_info (
                version     INTEGER NOT NULL
            );

            -- 계정 = 로그인 정보. **웹이 소유한다.** 게임 서버는 비밀번호 확인을 위해 읽기만 한다.
            -- role: user / design / dev / admin — 관리자 화면의 권한 구분. 기본은 user.
            CREATE TABLE IF NOT EXISTS account (
                nick_key    TEXT PRIMARY KEY,
                nick        TEXT NOT NULL,
                salt        TEXT NOT NULL DEFAULT '',
                hash        TEXT NOT NULL DEFAULT '',
                role        TEXT NOT NULL DEFAULT 'user',
                created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
            );

            -- 접속 기록. 웹 관리자 화면에서 "누가 언제 어디서 들어왔나"를 본다.
            -- 계정이 지워져도 기록은 남아야 하므로 FK 를 걸지 않는다.
            CREATE TABLE IF NOT EXISTS login_log (
                id       BIGSERIAL PRIMARY KEY,
                nick_key TEXT NOT NULL,
                source   TEXT NOT NULL,          -- 'web' | 'game'
                ok       BOOLEAN NOT NULL,
                detail   TEXT NOT NULL DEFAULT '',  -- 실패 사유 등
                ip       TEXT NOT NULL DEFAULT '',
                at       TIMESTAMPTZ NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_login_log_nick ON login_log(nick_key, id DESC);
            CREATE INDEX IF NOT EXISTS idx_login_log_at ON login_log(at DESC);

            -- 공지사항. 웹이 소유하고, 게임은 접속 시 최신 한 건을 읽어 보여줄 수 있다.
            CREATE TABLE IF NOT EXISTS notice (
                id           BIGSERIAL PRIMARY KEY,
                title        TEXT NOT NULL,
                body         TEXT NOT NULL DEFAULT '',
                kind         TEXT NOT NULL DEFAULT 'notice',  -- notice | update | maintenance
                author_key   TEXT NOT NULL DEFAULT '',
                published_at TIMESTAMPTZ NULL,                -- NULL = 초안
                created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS idx_notice_published ON notice(published_at DESC);

            -- 플레이어 = 지갑·외모·출석·인기도. **게임 서버가 소유한다.**
            -- fame = 내 화분에 남들이 꽂아 준 캣닢의 누적 개수. 화분을 팔아도 줄지 않는다(명예는 남는다).
            CREATE TABLE IF NOT EXISTS player (
                nick_key        TEXT PRIMARY KEY REFERENCES account(nick_key) ON DELETE CASCADE,
                rupee           BIGINT NOT NULL DEFAULT 0,
                figure          TEXT NOT NULL DEFAULT '',
                last_allowance  TEXT NOT NULL DEFAULT '',
                streak          INTEGER NOT NULL DEFAULT 0,
                fame            INTEGER NOT NULL DEFAULT 0,
                updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
            );

            -- 누가 누구에게 캣닢을 꽂아 줬는지. 인기도의 근거이자 "누가 다녀갔나"의 기록이다.
            CREATE TABLE IF NOT EXISTS gift_log (
                id        BIGSERIAL PRIMARY KEY,
                giver_key TEXT NOT NULL,
                owner_key TEXT NOT NULL,
                room_key  TEXT NOT NULL DEFAULT '',
                at        TIMESTAMPTZ NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_gift_log_owner ON gift_log(owner_key, id DESC);
            CREATE INDEX IF NOT EXISTS idx_gift_log_giver ON gift_log(giver_key, id DESC);

            -- 가방: 계정이 가진 가구 종류와 수량.
            CREATE TABLE IF NOT EXISTS inventory (
                nick_key    TEXT NOT NULL REFERENCES account(nick_key) ON DELETE CASCADE,
                furni_id    TEXT NOT NULL,
                qty         INTEGER NOT NULL,
                PRIMARY KEY (nick_key, furni_id)
            );

            -- 방: 개인 방은 'u:닉', 공용 방은 't:템플릿'.
            CREATE TABLE IF NOT EXISTS room (
                room_key    TEXT PRIMARY KEY,
                template_id TEXT NOT NULL,
                name        TEXT NOT NULL,
                owner_nick  TEXT NOT NULL DEFAULT '',
                wall_style  TEXT NOT NULL DEFAULT '',   -- 비면 템플릿 기본값
                floor_style TEXT NOT NULL DEFAULT '',
                updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
            );

            -- 방에 놓인 가구 하나하나(인스턴스). 가구 '정의'는 data/furni/*.json.
            CREATE TABLE IF NOT EXISTS room_item (
                id          BIGSERIAL PRIMARY KEY,
                room_key    TEXT NOT NULL REFERENCES room(room_key) ON DELETE CASCADE,
                furni_id    TEXT NOT NULL,
                x           INTEGER NOT NULL,
                y           INTEGER NOT NULL,
                dir         INTEGER NOT NULL,
                wall_u      INTEGER NOT NULL DEFAULT 0,
                wall_v      INTEGER NOT NULL DEFAULT 0,
                tilt        INTEGER NOT NULL DEFAULT 0,
                state       TEXT NOT NULL DEFAULT '',
                state_at    TEXT NOT NULL DEFAULT '',
                extra       TEXT NULL,
                author      TEXT NOT NULL DEFAULT ''
            );

            -- 루피가 오간 내역. 재화 사고는 기록 없이는 복구도 추적도 못 한다.
            CREATE TABLE IF NOT EXISTS currency_log (
                id       BIGSERIAL PRIMARY KEY,
                nick_key TEXT NOT NULL,
                delta    BIGINT NOT NULL,
                balance  BIGINT NOT NULL,
                reason   TEXT NOT NULL,
                at       TIMESTAMPTZ NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_room_item_room ON room_item(room_key);
            CREATE INDEX IF NOT EXISTS idx_room_owner ON room(owner_nick);
            CREATE INDEX IF NOT EXISTS idx_currency_log_nick ON currency_log(nick_key, id);
            """);

        AddMissingColumns(conn);

        if (conn.ExecuteScalar<int>("SELECT COUNT(*) FROM schema_info") == 0)
            conn.Execute("INSERT INTO schema_info(version) VALUES (@v)", new { v = SchemaVersion });

        LockDownForDataApi(conn);
    }

    /// <summary>
    /// **`CREATE TABLE IF NOT EXISTS` 는 이미 있는 테이블에 컬럼을 더해 주지 않는다.**
    /// 위 정의에 컬럼을 추가해 놓고 이걸 빠뜨리면, 이미 DB 가 만들어진 환경에서만 조용히 터진다
    /// (새로 만든 DB 에서는 멀쩡하므로 더 늦게 발견된다). 컬럼을 늘릴 때마다 여기에도 한 줄 추가할 것.
    /// `ADD COLUMN IF NOT EXISTS` 라 여러 번 실행해도 안전하다.
    /// </summary>
    private static void AddMissingColumns(NpgsqlConnection conn) => conn.Execute("""
        ALTER TABLE account ADD COLUMN IF NOT EXISTS role TEXT NOT NULL DEFAULT 'user';
        ALTER TABLE player  ADD COLUMN IF NOT EXISTS fame INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE room    ADD COLUMN IF NOT EXISTS wall_style  TEXT NOT NULL DEFAULT '';
        ALTER TABLE room    ADD COLUMN IF NOT EXISTS floor_style TEXT NOT NULL DEFAULT '';
        """);

    /// <summary>
    /// **Supabase 안전장치.** Supabase 는 `public` 스키마의 테이블을 REST API(Data API)로 자동 노출하고,
    /// `anon` 키는 설계상 공개된다. RLS 를 켜지 않으면 **바깥에서 계정·지갑 테이블을 그대로 읽고 쓸 수 있다.**
    ///
    /// 정책(policy)을 하나도 만들지 않은 채 RLS 만 켜면 anon/authenticated 는 전부 거부된다.
    /// 우리 서버는 테이블 소유자(postgres)로 붙으므로 RLS 를 우회한다 — 즉 **바깥만 막히고 게임은 그대로 돈다.**
    /// 나중에 웹이 `anon` 키로 직접 읽어야 할 테이블이 생기면 그때 그 테이블에만 정책을 추가한다.
    ///
    /// Supabase 가 아닌 Postgres 에서도 무해하다(여러 번 실행해도 안전).
    /// </summary>
    private static void LockDownForDataApi(NpgsqlConnection conn)
    {
        foreach (var t in new[] { "schema_info", "account", "player", "inventory", "room", "room_item", "currency_log", "login_log", "notice", "gift_log" })
            conn.Execute($"ALTER TABLE {t} ENABLE ROW LEVEL SECURITY;");
    }
}
