using VRCX.Common.Services.Ingestion;
using Hangfire;
using Hangfire.Redis.StackExchange;
using StackExchange.Redis;
using Serilog;
using VRCX.API.Services;
using VRCX.API.Utils;
using VRCX.Common;
using VRCX.Common.Extensions;
using VRCX.Common.Services.Presence;
using VRCX.Common.Services.Sessions;

// The one server process: HTTP API, the pipeline websocket listener per account, and the
// Hangfire recurring jobs that pull from VRChat. Nothing else runs server-side.
var builder = VrcxApplication.CreateDefaultBuilder<Program>(args);

var databaseOptions = builder.RegisterDatabaseOptions();
var redisOptions = builder.RegisterRedisOptions();
builder.RegisterImageOptions();
builder.RegisterVrchatOptions();
builder.RegisterKeyOptions();
builder.RegisterErrorReportingOptions();
builder.RegisterAuthOptions();
builder.RegisterMetricsOptions();

builder.Services
    .AddVrcxDB(databaseOptions)
    .AddVrcxRedis(redisOptions)
    .AddVrcxServices();

// Hangfire's locks and job state churn constantly, so they live in Redis. The recurring jobs are
// registered again on every start. Jobs are discovered by [CronJob] below; the dashboard is at
// /hangfire and shares the API token requirement.
builder.Services.AddHangfire((services, hangfire) =>
    hangfire.UseRedisStorage(services.GetRequiredService<IConnectionMultiplexer>(), new RedisStorageOptions { Prefix = "vrcx:hangfire:" }));
builder.Services.AddHangfireServer(options => options.WorkerCount = 2);

builder.Services.AddHostedService<PipelineService>();
builder.Services.AddHostedService<PendingPresenceService>();
builder.Services.AddHostedService<PresencePartitionService>();
builder.Services.AddSingleton<NotificationRefreshGate>();
builder.Services.AddHostedService<StubRefreshService>();
builder.Services.AddHostedService<ProfileRefreshService>();
builder.Services.AddHostedService<AutoStatusService>();

var app = builder.Build();

// Optional path prefix the API is served under (e.g. "/api" when reverse-proxied on a shared host).
var apiPathBase = builder.Configuration.GetValue<string>("VRCX:Api:PathBase");

app.UseCommonVrcxMiddleware(apiPathBase);

if (!databaseOptions.SkipMigration)
{
    await app.ApplyPendingVrcxMigrations(databaseOptions);
}
else
{
    Log.Warning("Skipping possible database migrations...");
}

// Redis may be empty or behind after a restart: bring it in line with the log before anything reads it.
// A failure (Redis down at boot, a slow database) must not stop the server: presence falls back to
// Postgres while Redis is unavailable, and Redis is reconciled again when it comes back.
try
{
    var reconciled = await app.Services.GetRequiredService<RedisPresenceCache>().ReconcileAsync(CancellationToken.None);
    Log.Information("{Count} presence entries were restored from the event log", reconciled);
}
catch (Exception ex)
{
    Log.Error(ex, "Could not restore presence into Redis at start-up; continuing, it is put right when Redis is reachable");
}

// Every account that was logged in gets its session back from its cookies before any job runs.
var resumed = await app.Services.GetRequiredService<IAccountSessionManager>().ResumeAllAsync(CancellationToken.None);
Log.Information("Resumed {Count} VRChat session(s)", resumed);

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [],
    AsyncAuthorization = [new DashboardApiTokenAuth()],
});

var jobManager = app.Services.GetRequiredService<IRecurringJobManagerV2>();
foreach (var cronJob in CronJobCollector.GetAllCronJobs())
{
    jobManager.AddOrUpdate(cronJob.Name, cronJob.Job, cronJob.Schedule);
}

await app.RunAsync();

// Expose Program class for integration tests
public partial class Program;
