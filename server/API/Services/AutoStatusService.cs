using System.Collections.Concurrent;
using VRCX.Common.Services.Automation;
using VRCX.Common.Services.Sessions;
using VRCX.Vrchat;

namespace VRCX.API.Services;

/// <summary>Runs each account's auto status rules every 3 seconds, the original's interval, waiting a minute after a VRChat error.</summary>
public sealed class AutoStatusService(IAccountSessionManager sessions, AccountAutomation automation, ILogger<AutoStatusService> logger) : BackgroundService
{
    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _failed = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            foreach (var accountId in sessions.ActiveAccountIds.ToArray())
            {
                if (_failed.TryGetValue(accountId, out var at) && DateTimeOffset.UtcNow - at < Retry) continue;
                try
                {
                    await automation.UpdateStatusAsync(accountId, stoppingToken);
                }
                catch (VrchatUnauthorizedException)
                {
                    // The session manager notices this on the account's own sync.
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _failed[accountId] = DateTimeOffset.UtcNow;
                    logger.LogWarning(ex, "Auto status for {Account} failed", accountId);
                }
            }
        }
    }
}
