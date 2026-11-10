using VRCX.API.Attributes;
using VRCX.Common.Services.Sessions;
using VRCX.Common.Services.Sync;
using VRCX.Vrchat;

namespace VRCX.API.Jobs;

/// <summary>Pulls groups, favorites, moderations, notes and notifications for every live account.</summary>
[CronJob("7 * * * *")]
public sealed class FullSyncJob
{
    private readonly IAccountSessionManager _sessions;
    private readonly IAccountSyncService _sync;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<FullSyncJob> _logger;

    public FullSyncJob(IAccountSessionManager sessions, IAccountSyncService sync, IHostApplicationLifetime lifetime, ILogger<FullSyncJob> logger)
    {
        _sessions = sessions;
        _sync = sync;
        _lifetime = lifetime;
        _logger = logger;
    }

    public async Task<int> Execute()
    {
        var cancellationToken = _lifetime.ApplicationStopping;
        await _sessions.ResumeAllAsync(cancellationToken);

        var synced = 0;
        foreach (var accountId in _sessions.ActiveAccountIds)
        {
            try
            {
                await _sync.SyncEverythingAsync(accountId, cancellationToken);
                synced++;
            }
            catch (VrchatUnauthorizedException)
            {
                await _sessions.InvalidateAsync(accountId, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Full sync failed for {Account}", accountId);
            }
        }
        return synced;
    }
}
