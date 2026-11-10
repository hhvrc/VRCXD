using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using VRCX.Common.HealthChecks;
using VRCX.Common.Options;
using VRCX.Common.VrcxDb;
using VRCX.Common.Websocket;
using IPNetwork = System.Net.IPNetwork;

namespace VRCX.Common;

public static class VrcxMiddlewareHelper
{
    public static IApplicationBuilder UseCommonVrcxMiddleware(this WebApplication app, string? pathBase = null)
    {
        // Networks allowed to reach the operational endpoints (metrics, health). Defaults to the
        // private ranges, so in-cluster scrapers and container probes work without configuration.
        var metricsOptions = app.Services.GetRequiredService<MetricsOptions>();
        var internalAllowedIpNetworks = metricsOptions.AllowedNetworks.Select(IPNetwork.Parse).ToArray();

        // Mount the whole app under a path prefix (e.g. "/api" when reverse-proxied on a shared host).
        if (!string.IsNullOrWhiteSpace(pathBase))
        {
            app.UsePathBase(NormalizePathBase(pathBase));
        }

        // Health is operational data, gated by the same allow-list as /metrics.
        app.Use(async (HttpContext context, RequestDelegate next) =>
        {
            if (context.Request.Path.StartsWithSegments(HealthCheckPaths.Health)
                && !IsAllowed(context.Connection.RemoteIpAddress, internalAllowedIpNetworks))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context);
        });

        app.UseSerilogRequestLogging(options =>
        {
            // Probes hit the health endpoint every few seconds; drop *successful* ones to Verbose.
            var defaultGetLevel = options.GetLevel;
            options.GetLevel = (context, elapsed, exception) =>
                exception is null
                && context.Response.StatusCode < 400
                && context.Request.Path.StartsWithSegments(HealthCheckPaths.Health)
                    ? LogEventLevel.Verbose
                    : defaultGetLevel(context, elapsed, exception);
        });

        if (app.Environment.IsDevelopment())
        {
            app.Use((context, next) =>
            {
                context.Request.EnableBuffering();
                return next.Invoke();
            });
        }
        app.UseExceptionHandler();

        app.UseCors();

        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromMinutes(1)
        });
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseOpenTelemetryPrometheusScrapingEndpoint(context =>
        {
            if (context.Request.Path != "/metrics") return false;

            return IsAllowed(context.Connection.RemoteIpAddress, internalAllowedIpNetworks);
        });

        app.MapOpenApi("/openapi/{documentName}.json").WithDocumentPerVersion();

        app.MapScalarApiReference("/scalar/viewer", options =>
            options
                .WithOpenApiRoutePattern("/openapi/{documentName}.json")
                .AddDocument("1", "Version 1"));

        app.MapHealthChecks(HealthCheckPaths.Health, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthCheckTags.Ready),
            ResponseWriter = WriteHealthReportAsync
        });

        app.MapControllers();

        // The live socket: protobuf frames both ways (game logs, server events, and whatever comes next). Same token as the rest.
        app.Map("/1/ws", (HttpContext context, LiveSocketHandler handler) => handler.HandleAsync(context)).RequireAuthorization();

        return app;
    }

    private static bool IsAllowed(System.Net.IPAddress? remoteIp, IPNetwork[] allowedNetworks) =>
        // In-process test servers and local tooling have no remote address.
        remoteIp is null || allowedNetworks.Any(network => network.Contains(remoteIp));

    private static Task WriteHealthReportAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(HealthReportResponse.From(report));
    }

    private static string NormalizePathBase(string pathBase)
    {
        var trimmed = pathBase.Trim().Trim('/');
        return trimmed.Length == 0 ? "/" : "/" + trimmed;
    }

    public static async Task<IApplicationBuilder> ApplyPendingVrcxMigrations(this IApplicationBuilder app, DatabaseOptions options)
    {
        using var scope = app.ApplicationServices.CreateScope();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("MigrationHelper");

        logger.LogInformation("Running database migrations...");

        await using var migrationContext = new MigrationVrcxContext(options.Conn, options.Debug, loggerFactory);

        var pendingMigrations = (await migrationContext.Database.GetPendingMigrationsAsync()).ToArray();

        if (pendingMigrations.Length > 0)
        {
            logger.LogInformation("Found pending migrations, applying [{@Migrations}]", string.Join(", ", pendingMigrations));
            await migrationContext.Database.MigrateAsync();
            logger.LogInformation("Applied database migrations... proceeding with startup");

            // A migration may have introduced new Postgres enum types. Npgsql snapshots the type
            // catalog on the runtime data source's first connection; reload it so this process does
            // not need a restart to see them.
            await ReloadRuntimeTypeCatalogAsync(scope.ServiceProvider, logger);
        }
        else
        {
            logger.LogInformation("No pending migrations found, proceeding with startup");
        }

        return app;
    }

    private static async Task ReloadRuntimeTypeCatalogAsync(IServiceProvider services, Microsoft.Extensions.Logging.ILogger logger)
    {
        try
        {
            await using var context = services.GetRequiredService<VrcxContext>();
            var connection = (Npgsql.NpgsqlConnection)context.Database.GetDbConnection();
            await connection.OpenAsync();
            await connection.ReloadTypesAsync();
            logger.LogInformation("Reloaded Npgsql type catalog after applying migrations");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to reload Npgsql type catalog after migrations; a restart may be required for new enum types");
        }
    }

    /// <summary>
    /// Blocks startup until the schema is up to date, for hosts that do not own migrations (Cron).
    /// The context binds Postgres enum types by name on the pool's first connection, so opening it
    /// before the migrator has created a new enum would fail every query until a restart.
    /// </summary>
    public static async Task<IApplicationBuilder> WaitForVrcxSchemaReady(this IApplicationBuilder app, DatabaseOptions options, TimeSpan? timeout = null)
    {
        using var scope = app.ApplicationServices.CreateScope();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("SchemaReadyGate");

        if (options.SkipMigration)
        {
            logger.LogWarning("SkipMigration is set; not waiting for database schema readiness");
            return app;
        }

        logger.LogInformation("Waiting for database schema to be up to date (migrations owned by the API host)...");

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(5));
        var delay = TimeSpan.FromSeconds(1);
        var maxDelay = TimeSpan.FromSeconds(15);

        while (true)
        {
            try
            {
                await using var migrationContext = new MigrationVrcxContext(options.Conn, options.Debug, loggerFactory);
                var pending = (await migrationContext.Database.GetPendingMigrationsAsync()).ToArray();
                if (pending.Length == 0)
                {
                    logger.LogInformation("Database schema is up to date; proceeding with startup");
                    return app;
                }

                logger.LogWarning("Schema not ready: {Count} pending migration(s) [{Migrations}] not yet applied by the migrator",
                    pending.Length, string.Join(", ", pending));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not determine migration state; will retry");
            }

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Timed out waiting for the database schema to be brought up to date by the API host.");

            await Task.Delay(delay);
            delay = TimeSpan.FromSeconds(Math.Min(maxDelay.TotalSeconds, delay.TotalSeconds * 2));
        }
    }
}
