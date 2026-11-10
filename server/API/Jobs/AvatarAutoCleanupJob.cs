using VRCX.API.Attributes;
using VRCX.Common.Services.Feed;

namespace VRCX.API.Jobs;

/// <summary>
/// The original's avatar auto-cleanup, which it checks on each start: here hourly, for each account
/// whose Auto-Cleanup setting is not Off, and at most once every 7 days per account.
/// </summary>
[CronJob("23 * * * *")]
public sealed class AvatarAutoCleanupJob(AvatarFeedCleanup cleanup, IHostApplicationLifetime lifetime)
{
    public Task<int> Execute() => cleanup.RunAutoCleanupForAllAsync(lifetime.ApplicationStopping);
}
