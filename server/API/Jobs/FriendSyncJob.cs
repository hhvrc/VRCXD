using VRCX.API.Attributes;
using VRCX.Common.Services.Sessions;
using VRCX.Common.Services.Sync;
using VRCX.Vrchat;

namespace VRCX.API.Jobs;

/// <summary>
/// The original's 5-minute current user refresh for every live account. The friend list is diffed
/// again only when it is over an hour old (upstream PR 881: VRChat asked clients to poll less).
/// </summary>
[CronJob("*/5 * * * *")]
public sealed class FriendSyncJob
{
    private readonly IAccountSessionManager _sessions;
    private readonly IAccountSyncService _sync;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<FriendSyncJob> _logger;

    public FriendSyncJob(IAccountSessionManager sessions, IAccountSyncService sync, IHostApplicationLifetime lifetime, ILogger<FriendSyncJob> logger)
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
                await _sync.RefreshCurrentUserAsync(accountId, cancellationToken);
                synced++;
            }
            catch (VrchatUnauthorizedException)
            {
                await _sessions.InvalidateAsync(accountId, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Friend sync failed for {Account}", accountId);
            }
        }
        return synced;
    }
}
