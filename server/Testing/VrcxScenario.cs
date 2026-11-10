using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VRCX.API.Jobs;
using VRCX.API.Services;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.VrcxDb;
using VRCX.Testing.Desktop;
using VRCX.Testing.FakeVrchat;
using VRCX.Testing.Infrastructure;
using VRCX.Testing.Live;
using VRCX.Testing.Pipeline;

namespace VRCX.Testing;

public sealed record ScenarioOptions
{
    /// <summary>Serve on Kestrel at this port (0 picks one) instead of an in-memory TestServer. For browser tests.</summary>
    public int? KestrelPort { get; init; }

    /// <summary>Where the scenario clock starts. Defaults to the real now, so timestamps agree with code that reads DateTimeOffset.UtcNow.</summary>
    public DateTimeOffset? StartTime { get; init; }

    /// <summary>Extra service replacements, applied after the scenario's own (a fake image fetcher, say).</summary>
    public Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? ConfigureServices { get; init; }
}

/// <summary>
/// One isolated VRCX server with everything around it faked: its own Postgres database and Redis
/// database (from containers shared by the test run), the real API host, a stateful fake VRChat
/// API (<see cref="Vrchat"/>), the pipeline (<see cref="Pipeline"/>), desktop apps shipping game
/// logs (<see cref="ConnectDesktopAsync"/>), browsers listening to live events
/// (<see cref="ConnectLiveAsync"/>) and the clock (<see cref="Time"/>). Scenarios share nothing,
/// so tests using them can run in parallel. See Server/Testing/README.md.
/// </summary>
/// <example>
/// <code>
/// await using var app = await VrcxScenario.StartAsync();
/// var alice = app.Vrchat.Friend("usr_alice", "Alice");
/// await app.LoginAsync();
/// await using var live = await app.ConnectLiveAsync();
/// await app.Pipeline.FriendOnlineAsync(alice, "wrld_a:1~region(eu)");
/// var presence = await live.WaitForAsync&lt;PresenceChanged&gt;(p =&gt; p.UserId == alice.Id);
/// </code>
/// </example>
public sealed class VrcxScenario : IAsyncDisposable
{
    public const string ApiToken = "vrcx-scenario-api-token";

    private readonly ScenarioOptions _options;
    private readonly List<IAsyncDisposable> _sockets = [];
    private TestDatabase _database = null!;
    private TestRedis _redis = null!;
    private ScenarioHost _host = null!;
    private string _imageDirectory = null!;
    private PipelineService? _pipelineService;
    private HttpClient? _api;

    private VrcxScenario(ScenarioOptions options)
    {
        _options = options;
        Time = new ScenarioClock(options.StartTime);
        Vrchat = new FakeVrchatApi(Time);
        Pipeline = new FakePipeline(this);
        Jobs = new ScenarioJobs(this);
    }

    public static async Task<VrcxScenario> StartAsync(ScenarioOptions? options = null)
    {
        // A 401 is checked again before a session is dropped; no need to wait for that in tests.
        Common.Services.Sessions.AccountSessionManager.ConfirmDelay = TimeSpan.Zero;
        // Nor for the auto-login delay setting: its events still carry the seconds.
        Common.Services.Sessions.AccountSessionManager.AutoLoginDelayUnit = TimeSpan.Zero;
        var scenario = new VrcxScenario(options ?? new ScenarioOptions());
        try
        {
            var database = SharedContainers.CreateDatabaseAsync();
            var redis = SharedContainers.RentRedisAsync();
            scenario._database = await database;
            scenario._redis = await redis;
            scenario._imageDirectory = Path.Combine(Path.GetTempPath(), "vrcx-scenario-images-" + Guid.NewGuid().ToString("N"));
            await scenario.StartHostAsync();
        }
        catch
        {
            await scenario.DisposeAsync();
            throw;
        }
        return scenario;
    }

    private async Task StartHostAsync()
    {
        _host = await ScenarioHost.StartAsync(new ScenarioHostSettings(
            _database.ConnectionString, _redis.ConnectionString, ApiToken, _imageDirectory, Vrchat, Time, Logs, _options.KestrelPort, _options.ConfigureServices, Syncs));
        _pipelineService = null;
        _api = null;
    }

    // ---- the pieces ------------------------------------------------------

    /// <summary>The fake api.vrchat.cloud. Add users, friends, worlds ... here before logging in.</summary>
    public FakeVrchatApi Vrchat { get; }

    /// <summary>The fake pipeline websocket: push frames as VRChat would.</summary>
    public FakePipeline Pipeline { get; }

    /// <summary>
    /// The scenario clock (wall clock plus an offset), registered as the server's TimeProvider.
    /// The pipeline ingestor, the fake desktop's log timestamps, the fake VRChat's notification
    /// times and <see cref="ScenarioJobs.SweepPendingPresenceAsync"/> follow it, including its
    /// jumps; code still reading DateTimeOffset.UtcNow directly sees only the wall clock.
    /// </summary>
    public ScenarioClock Time { get; }

    /// <summary>Scheduled work the background services and Hangfire would run, run on demand.</summary>
    public ScenarioJobs Jobs { get; }

    /// <summary>The friend and full syncs the server has run; a login through <see cref="Api"/> waits for its initial one.</summary>
    public SyncTracker Syncs { get; } = new();

    /// <summary>What the server logged.</summary>
    public CapturedLogs Logs { get; } = new();

    public IServiceProvider Services => _host.Services;

    /// <summary>The in-memory test server (not available with Kestrel).</summary>
    public Microsoft.AspNetCore.TestHost.TestServer Server => _host.Server;

    public TestDatabase Database => _database;

    /// <summary>The account <see cref="LoginAsync"/> logged in, which the account helpers default to.</summary>
    public string? AccountId { get; private set; }

    /// <summary>Kestrel's address when <see cref="ScenarioOptions.KestrelPort"/> was set.</summary>
    public Uri BaseAddress => _host.ClientOptions.BaseAddress;

    /// <summary>An HTTP client with the server's API token, as the web UI calls the API.</summary>
    public HttpClient Api => _api ??= CreateClient();

    public HttpClient CreateClient(bool authenticated = true)
    {
        // No redirect or cookie handling, as a browser's fetch to the API sees it; a login returns
        // only once the full sync it starts in the background is done (see SyncTracker).
        var client = _host.CreateDefaultClient(_host.ClientOptions.BaseAddress, new SettleAfterLoginHandler(Syncs));
        if (authenticated) client.DefaultRequestHeaders.Add(Common.Constants.AuthConstants.ApiTokenHeaderName, ApiToken);
        return client;
    }

    /// <summary>A context on the scenario's database, for arranging and asserting rows.</summary>
    public VrcxContext CreateDbContext() => Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContext();

    internal PipelineService PipelineService => _pipelineService ??= ActivatorUtilities.CreateInstance<PipelineService>(Services);

    // ---- common steps ------------------------------------------------------

    /// <summary>
    /// Logs the server into the fake VRChat as <see cref="FakeVrchatApi.Me"/> (creating a default
    /// "usr_me" when none was set) and, unless told not to, runs the full sync the UI triggers
    /// right after a login.
    /// </summary>
    public async Task<string> LoginAsync(bool sync = true)
    {
        var me = Vrchat.Me ?? Vrchat.SetMe();
        var response = await Api.PostAsJsonAsync("/1/accounts/login", new { username = (string?)me.Json["username"] ?? "me", password = "test-password" });
        await EnsureSuccessAsync(response, "login");
        AccountId = me.Id;
        if (sync) await SyncAsync();
        return me.Id;
    }

    /// <summary>POST /1/accounts/{id}/sync: friends, groups, favorites, moderations, notes and notifications.</summary>
    public async Task SyncAsync()
    {
        var response = await Api.PostAsync(AccountPath("sync"), null);
        await EnsureSuccessAsync(response, "sync");
    }

    /// <summary>"/1/accounts/{account}/{rest}".</summary>
    public string AccountPath(string rest = "") =>
        $"/1/accounts/{AccountId ?? throw new InvalidOperationException("Log in first")}{(rest.Length > 0 ? "/" + rest : "")}";

    /// <summary>GET as JSON; fails with the body when the status is not a success.</summary>
    public async Task<JsonElement> GetJsonAsync(string path)
    {
        var response = await Api.GetAsync(path);
        await EnsureSuccessAsync(response, "GET " + path);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, string what)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"{what} answered {(int)response.StatusCode} {response.StatusCode}: {body}", null, response.StatusCode);
    }

    /// <summary>A browser's live connection to /1/ws.</summary>
    public async Task<LiveClient> ConnectLiveAsync()
    {
        var client = await LiveClient.ConnectAsync(await OpenSocketAsync());
        lock (_sockets) _sockets.Add(client);
        return client;
    }

    /// <summary>
    /// A desktop app connecting from a gaming PC. With signedInAs it first ships the "User
    /// Authenticated" line, as the app does at the top of every log, so the server knows whose PC it is.
    /// </summary>
    public async Task<FakeDesktop> ConnectDesktopAsync(string clientId = "pc-test", string? signedInAs = null, bool muteEvents = true)
    {
        var desktop = await FakeDesktop.ConnectAsync(await OpenSocketAsync(), clientId, Time, muteEvents);
        lock (_sockets) _sockets.Add(desktop);
        var account = signedInAs ?? AccountId;
        if (account is not null)
        {
            var name = Vrchat.Me?.Id == account ? Vrchat.Me.DisplayName : account;
            await desktop.SendLogAsync(GameLog.UserAuthenticated(account, name));
        }
        return desktop;
    }

    private async Task<System.Net.WebSockets.WebSocket> OpenSocketAsync()
    {
        if (_options.KestrelPort is not null)
        {
            var socket = new System.Net.WebSockets.ClientWebSocket();
            socket.Options.SetRequestHeader(Common.Constants.AuthConstants.ApiTokenHeaderName, ApiToken);
            var uri = new UriBuilder(BaseAddress) { Scheme = BaseAddress.Scheme == "https" ? "wss" : "ws", Path = "/1/ws" }.Uri;
            await socket.ConnectAsync(uri, CancellationToken.None);
            return socket;
        }
        var client = _host.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers[Common.Constants.AuthConstants.ApiTokenHeaderName] = ApiToken;
        return await client.ConnectAsync(new Uri(_host.Server.BaseAddress, "/1/ws"), CancellationToken.None);
    }

    /// <summary>
    /// Stops the server and starts a new one on the same database, Redis and fake VRChat, as a
    /// redeploy or crash would. Open sockets are closed; sessions resume from the accounts table.
    /// </summary>
    public async Task RestartAsync()
    {
        await CloseSocketsAsync();
        await _host.DisposeAsync();
        await StartHostAsync();
    }

    private async Task CloseSocketsAsync()
    {
        List<IAsyncDisposable> sockets;
        lock (_sockets)
        {
            sockets = _sockets.ToList();
            _sockets.Clear();
        }
        foreach (var socket in sockets) await socket.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await CloseSocketsAsync();
        _api?.Dispose();
        if (_host is not null) await _host.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
        if (_database is not null) await _database.DisposeAsync();
        try
        {
            if (_imageDirectory is not null && Directory.Exists(_imageDirectory)) Directory.Delete(_imageDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>The scheduled work, run once on demand instead of by its timer.</summary>
public sealed class ScenarioJobs(VrcxScenario scenario)
{
    /// <summary>The 5-minute Hangfire current user refresh (FriendSyncJob), which diffs the friend list only when it is over an hour old.</summary>
    public Task<int> FriendSyncAsync() =>
        ActivatorUtilities.CreateInstance<FriendSyncJob>(scenario.Services).Execute();

    /// <summary>The hourly Hangfire full sync (FullSyncJob) for every live account.</summary>
    public Task<int> FullSyncAsync() =>
        ActivatorUtilities.CreateInstance<FullSyncJob>(scenario.Services).Execute();

    /// <summary>
    /// One tick of the pending offline/active timer (PendingPresenceService) at the scenario
    /// clock's now. Advance <see cref="VrcxScenario.Time"/> past PresenceDebouncer.Delay (170 s)
    /// first for a held transition to apply. Returns how many were applied.
    /// </summary>
    public Task<int> SweepPendingPresenceAsync() =>
        scenario.Services.GetRequiredService<PresenceDebouncer>().SweepAsync(scenario.Time.GetUtcNow(), CancellationToken.None);

    /// <summary>One tick of the auto status loop (AutoStatusService) for an account.</summary>
    public Task<string?> AutoStatusAsync(string? accountId = null) =>
        scenario.Services.GetRequiredService<Common.Services.Automation.AccountAutomation>()
            .UpdateStatusAsync(accountId ?? scenario.AccountId ?? throw new InvalidOperationException("Log in first"), CancellationToken.None);
}
