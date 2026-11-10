using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using VRCX.Common.Services.Sessions;
using VRCX.Common.Services.Sync;
using VRCX.Testing.FakeVrchat;

namespace VRCX.Testing.Infrastructure;

/// <summary>What one host needs: its own database, Redis database, fake VRChat, clock and log sink.</summary>
internal sealed record ScenarioHostSettings(
    string DatabaseConnection,
    string RedisConnection,
    string ApiToken,
    string ImageDirectory,
    FakeVrchatApi Vrchat,
    ScenarioClock Time,
    CapturedLogs Logs,
    int? KestrelPort,
    Action<IServiceCollection>? ConfigureServices,
    SyncTracker Syncs);

/// <summary>
/// The real API (its Program) on a TestServer, or on Kestrel for the browser tests. VRChat is the
/// fake, the clock is the scenario's, and the background loops (pipeline sockets, Hangfire
/// workers, refresh timers) are not started: the scenario drives each of those explicitly so
/// nothing races the assertions.
/// </summary>
internal sealed class ScenarioHost(ScenarioHostSettings settings) : WebApplicationFactory<Program>
{
    // The API reads its configuration from environment variables under integration tests (see
    // VrcxApplication), and those are process-wide: hosts are built one at a time.
    private static readonly SemaphoreSlim BuildLock = new(1, 1);

    private static string? FindApiDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var api = Path.Combine(dir.FullName, "API");
            if (File.Exists(Path.Combine(api, "API.csproj"))) return api;
        }
        return null;
    }

    public static async Task<ScenarioHost> StartAsync(ScenarioHostSettings settings)
    {
        var host = new ScenarioHost(settings);
        await BuildLock.WaitAsync();
        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_UNDER_INTEGRATION_TEST", "1");
            Environment.SetEnvironmentVariable("VRCX__DB__CONN", settings.DatabaseConnection);
            Environment.SetEnvironmentVariable("VRCX__DB__SKIPMIGRATION", "true");
            Environment.SetEnvironmentVariable("VRCX__DB__DEBUG", "false");
            Environment.SetEnvironmentVariable("VRCX__REDIS__CONN", settings.RedisConnection);
            Environment.SetEnvironmentVariable("VRCX__AUTH__TOKEN", settings.ApiToken);
            Environment.SetEnvironmentVariable("VRCX__IMAGES__DIR", settings.ImageDirectory);
            Environment.SetEnvironmentVariable("VRCX__KEYS__DIR", Path.Combine(settings.ImageDirectory, "keys"));
            // A test project gets its content root from the attribute Mvc.Testing generates into
            // it; a plain app using the harness (Testing.E2EHost) has none, so point it at API/.
            const string contentRootVariable = "ASPNETCORE_TEST_CONTENTROOT_VRCX_API";
            if (Environment.GetEnvironmentVariable(contentRootVariable) is null && FindApiDirectory() is { } apiDirectory)
                Environment.SetEnvironmentVariable(contentRootVariable, apiDirectory);
            if (settings.KestrelPort is { } port)
            {
                host.UseKestrel(port);
                host.StartServer();
            }
            else
            {
                _ = host.Server;
            }
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
        finally
        {
            BuildLock.Release();
        }
        return host;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSerilog(configuration => configuration
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Override("Hangfire", LogEventLevel.Warning)
                .WriteTo.Sink(settings.Logs));
        });

        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IVrchatClientFactory>(settings.Vrchat));
            services.Replace(ServiceDescriptor.Singleton<VRCX.Common.Services.Images.IImageFetcher>(settings.Vrchat.Images));
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(settings.Time));
            services.Replace(ServiceDescriptor.Singleton(settings.Time));
            services.AddSingleton<AccountSyncService>();
            services.Replace(ServiceDescriptor.Singleton<IAccountSyncService>(sp => new TrackedAccountSyncService(sp.GetRequiredService<AccountSyncService>(), settings.Syncs)));

            // The server's own loops and Hangfire's workers stay off; the web host (Kestrel or
            // the TestServer) must stay.
            services.RemoveAll(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType?.Namespace?.StartsWith("Microsoft.", StringComparison.Ordinal) != true);

            settings.ConfigureServices?.Invoke(services);
        });
    }
}

internal static class ServiceCollectionRemoveExtensions
{
    public static void RemoveAll(this IServiceCollection services, Func<ServiceDescriptor, bool> match)
    {
        for (var i = services.Count - 1; i >= 0; i--)
            if (match(services[i])) services.RemoveAt(i);
    }
}
