namespace Harbor.Server.Config;

public sealed class ServerOptions
{
    public int Port { get; set; } = 30000;
    /// <summary>랜딩 페이지 + 로그인 API 포트.</summary>
    public int WebPort { get; set; } = 8080;
    public int TickMs { get; set; } = 60;
    public int MaxSessions { get; set; } = 500;
    /// <summary>
    /// 게임 시작 화면에서 처음 보는 닉으로 그 자리에서 가입시킬지. **테스트 편의용으로만 켠다.**
    /// 원칙은 `account` 테이블의 주인이 웹 하나인 것이다 — 공개 운영에서는 false 로 두고 웹 가입만 받는다.
    /// </summary>
    public bool AllowGameSignup { get; set; }
}
public sealed class DataOptions { public string Root { get; set; } = "data"; }
/// <summary>
/// 영속 계층(PostgreSQL). **접속 문자열은 소스에 넣지 않는다** — 환경변수 `HARBOR_DB` 로 받는다(`.env.example` 참고).
/// appsettings 의 값은 비워 두고, Program.cs 가 환경변수로 채운다.
/// </summary>
public sealed class SaveOptions { public string ConnectionString { get; set; } = ""; public int FlushSeconds { get; set; } = 3; }
public sealed class EconomyOptions
{
    public long StartingRupee { get; set; } = 500;
    /// <summary>하루 한 번, 그날 첫 로그인에 주는 용돈(1일차 기준).</summary>
    public long DailyAllowance { get; set; } = 100;
    /// <summary>연속 출석 하루당 더해지는 보너스. Attendance.MaxBonusDays 에서 멈춘다.</summary>
    public long StreakStep { get; set; } = 50;
    /// <summary>다른 계열의 집으로 이사하는 값. 계열마다 단계 값이 조금씩 달라 그 차액보다는 비싸야 한다(싸면 이사로 이득을 본다).</summary>
    public long RemodelPrice { get; set; } = 1000;
    public long LotteryPrice { get; set; } = 10;
    public long LotteryJackpot { get; set; } = 1000;
    /// <summary>새 유저 시작 가방 (furniId → 수량). 첫 방문에만 지급.</summary>
    public Dictionary<string, int> StarterItems { get; set; } = new();
    /// <summary>개인 방 템플릿 roomId.</summary>
    public string HomeTemplate { get; set; } = "cabin_default_01";
}
