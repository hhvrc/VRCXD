using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services.Feed;

/// <summary>
/// The original's Database Cleanup (Settings, Advanced): purgeAvatarFeedData deletes the account's
/// avatar feed rows, those older than a cutoff or all of them, and runAvatarAutoCleanup does the same
/// once a week when VRCX_avatarAutoCleanup is not Off. Nothing else is touched.
/// <para>
/// The original's feed_avatar table belonged to one account. Here the rows are shared, so the
/// account's avatar feed is the avatar changes of its friends (as the Feed reads them), less any
/// user who is also a friend of another account on the server: those rows are still in that
/// account's feed, and it did not ask for them to go.
/// </para>
/// </summary>
public sealed class AvatarFeedCleanup(IDbContextFactory<VrcxContext> dbFactory, TimeProvider time, ILogger<AvatarFeedCleanup> logger)
{
    /// <summary>The original's auto-cleanup choices, in days; anything else is Off.</summary>
    public static readonly string[] AutoCleanupDays = ["30", "90", "180", "365"];

    /// <summary>The original checks the auto-cleanup at most once every 7 days.</summary>
    public static readonly TimeSpan AutoCleanupInterval = TimeSpan.FromDays(7);

    public static string Normalize(string? value) => value is not null && AutoCleanupDays.Contains(value) ? value : "Off";

    /// <summary>Deletes the account's avatar feed rows older than <paramref name="cutoff"/>, or all of them when it is null.</summary>
    /// <returns>The number of rows deleted.</returns>
    public async Task<int> PurgeAsync(string accountId, DateTimeOffset? cutoff, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var friendIds = db.Friendships.Where(f => f.OwnerUserId == accountId).Select(f => f.FriendUserId);
        var sharedIds = db.Friendships.Where(f => f.OwnerUserId != accountId).Select(f => f.FriendUserId);
        var rows = db.UserAvatarChanges.Where(c => friendIds.Contains(c.UserId) && !sharedIds.Contains(c.UserId));
        if (cutoff is { } before) rows = rows.Where(c => c.ChangedAt < before);
        return await rows.ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// The original's runAvatarAutoCleanup for one account: nothing when the setting is Off or the
    /// last run was under 7 days ago, else the rows older than the chosen days go and the run is noted.
    /// </summary>
    /// <returns>The number of rows deleted, or null when it did not run.</returns>
    public async Task<int?> RunAutoCleanupAsync(string accountId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.UserId == accountId, ct);
        if (account is null) return null;
        var settings = account.Settings;
        var setting = Normalize(settings.AvatarAutoCleanup);
        if (setting == "Off") return null;
        var now = time.GetUtcNow();
        if (settings.LastAvatarCleanupAt is { } last && now - last < AutoCleanupInterval) return null;

        var days = int.Parse(setting);
        var deleted = await PurgeAsync(accountId, now.AddDays(-days), ct);
        account.Settings = settings with { LastAvatarCleanupAt = now };
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Auto-cleaned avatar feed data older than {Days} days for {Account}: {Deleted} rows", days, accountId, deleted);
        return deleted;
    }

    /// <summary>The auto-cleanup for every account that turned it on.</summary>
    public async Task<int> RunAutoCleanupForAllAsync(CancellationToken ct)
    {
        List<string> accountIds;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            accountIds = await db.Accounts.AsNoTracking().Select(a => a.UserId).ToListAsync(ct);

        var ran = 0;
        foreach (var accountId in accountIds)
        {
            try
            {
                if (await RunAutoCleanupAsync(accountId, ct) is not null) ran++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Avatar auto-cleanup failed for {Account}", accountId);
            }
        }
        return ran;
    }
}
