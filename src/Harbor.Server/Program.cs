using Harbor.Server.Config;
using Harbor.Server.Data;
using Harbor.Server.Handlers;
using Harbor.Server.Net;
using Harbor.Server.Rooms;
using Harbor.Server.Web;
using Microsoft.Extensions.Options;

// appsettings.json·data/·web/ 는 빌드 출력 폴더에 복사되므로 실행 방식(dotnet run / exe / VS F5)과 무관하게 거기를 기준으로 잡는다.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection("Server"));
builder.Services.Configure<DataOptions>(builder.Configuration.GetSection("Data"));
builder.Services.Configure<EconomyOptions>(builder.Configuration.GetSection("Economy"));

// DB 접속 문자열은 **환경변수로만** 받는다 — 소스·저장소에 값이 남지 않게 (.env.example 참고).
var dbUrl = Environment.GetEnvironmentVariable("HARBOR_DB") ?? "";
builder.Services.Configure<SaveOptions>(o =>
{
    builder.Configuration.GetSection("Save").Bind(o);
    if (dbUrl.Length > 0) o.ConnectionString = dbUrl;
});

// 옛 SQLite 저장본을 옮기고 끝내는 모드: Harbor.Server.exe --import-sqlite=saves/harbor.db
if (args.FirstOrDefault(a => a.StartsWith("--import-sqlite=", StringComparison.Ordinal)) is { } importArg)
{
    if (dbUrl.Length == 0) { Console.WriteLine("[import] 환경변수 HARBOR_DB 가 없습니다."); return 1; }
    return SqliteImport.Run(importArg["--import-sqlite=".Length..].Trim('"'), dbUrl);
}
if (dbUrl.Length == 0 && string.IsNullOrWhiteSpace(builder.Configuration["Save:ConnectionString"]))
{
    Console.WriteLine("[Harbor] DB 접속 문자열이 없습니다. 환경변수 HARBOR_DB 를 설정하세요 (.env.example 참고).");
    return 1;
}

builder.Services.AddSingleton<DefinitionStore>(sp =>
{
    var store = new DefinitionStore();
    var root = sp.GetRequiredService<IOptions<DataOptions>>().Value.Root;
    store.Load(Path.IsPathRooted(root) ? root : Path.Combine(AppContext.BaseDirectory, root));
    return store;
});
builder.Services.AddSingleton<SaveStore>();
builder.Services.AddSingleton<Accounts>();
builder.Services.AddSingleton<OnlineUsers>();
builder.Services.AddSingleton<TicketStore>();
builder.Services.AddSingleton<RoomManager>();
builder.Services.AddSingleton<Dispatcher>();
builder.Services.AddHostedService<SaveService>();
builder.Services.AddHostedService<TcpHost>();

var opt = builder.Configuration.GetSection("Server").Get<ServerOptions>() ?? new ServerOptions();
builder.WebHost.UseUrls($"http://0.0.0.0:{opt.WebPort}");

var app = builder.Build();
app.UseWebSockets();          // /ws — 브라우저 클라이언트가 붙는 곳 (docs/web-client-plan.md 단계 0)
app.MapHarbor(Path.Combine(AppContext.BaseDirectory, "web"));
app.MapAdmin();               // /admin — HARBOR_ADMIN_TOKEN 이 있을 때만 열린다 (docs/platform-plan.md §7)

var defs = app.Services.GetRequiredService<DefinitionStore>();
var save = app.Services.GetRequiredService<SaveStore>();
var rooms = app.Services.GetRequiredService<RoomManager>();
HandlerRegistration.Register(app.Services.GetRequiredService<Dispatcher>(), rooms, defs,
    app.Services.GetRequiredService<IOptions<EconomyOptions>>().Value, save,
    app.Services.GetRequiredService<OnlineUsers>(),
    app.Services.GetRequiredService<Accounts>(),
    app.Services.GetRequiredService<TicketStore>(), opt);

// 방 계열/단계는 JSON 에만 있어 단위 테스트가 못 본다 → 기동할 때 한 번 훑어 어긋난 곳을 알린다.
// (넓히기 사슬이 끊기면 그 방은 조용히 '최종 단계'가 되어 버린다.)
foreach (var r in defs.Rooms.Values.Where(r => r.Kind != "public"))
{
    if (r.Family.Length == 0 || r.Tier <= 0)
        Console.WriteLine($"[Harbor] ⚠ 방 {r.RoomId}: family/tier 가 비어 있어 이사 목록에 안 나옵니다");
    if (r.Upgrade.Length == 0) continue;
    if (!defs.Rooms.TryGetValue(r.Upgrade, out var next))
        Console.WriteLine($"[Harbor] ⚠ 방 {r.RoomId}: upgrade 대상 '{r.Upgrade}' 가 없습니다 — 더 넓힐 수 없는 방이 됩니다");
    else if (next.Family != r.Family || next.Tier != r.Tier + 1)
        Console.WriteLine($"[Harbor] ⚠ 방 {r.RoomId}(tier {r.Tier}) → {next.RoomId}(family {next.Family}, tier {next.Tier}): 계열/단계가 이어지지 않습니다");
}

foreach (var r in defs.Rooms.Values.Where(r => r.Kind == "public")) rooms.Create(r.RoomId);
int restored = rooms.RestoreSaved();
Console.WriteLine($"[Harbor] rooms={defs.Rooms.Count} furni={defs.Furni.Count} 저장된방={restored} 저장된유저={save.UserCount}");
Console.WriteLine($"[Harbor] 게임 :{opt.Port}  ·  웹 http://localhost:{opt.WebPort}  ·  브라우저 ws://localhost:{opt.WebPort}/ws");
Console.WriteLine($"[Harbor] 저장 PostgreSQL {save.Describe()}");

await app.RunAsync();
return 0;
