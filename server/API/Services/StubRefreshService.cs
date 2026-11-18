using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services.Sessions;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.API.Services;

/// <summary>
/// Fills in worlds and groups we only know by id. A location tag creates stub rows so the
/// feed and visits can reference them; the original fetched the world the moment it showed
/// one, so here stubs are fetched a few at a time through any live account: never-tried ones
/// first (newest first), then the ones tried longest ago. Each try is stored with the row, and a
/// stub VRChat does not serve (deleted world, private group) waits longer after every failed
/// try, from an hour up to a week, so dead ones stop costing requests and old ones still get
/// their turn.
/// </summary>
public sealed class StubRefreshService(
    IDbContextFactory<VrcxContext> dbFactory,
    IAccountSessionManager sessions,
    IAccountSyncService sync,
    ILogger<StubRefreshService> logger,
    TimeProvider? time = null) : BackgroundService
{
    public const int BatchSize = 8;
    public static readonly TimeSpan FirstRetry = TimeSpan.FromHours(1);
    public static readonly TimeSpan MaxRetry = TimeSpan.FromDays(7);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            var accountId = sessions.ActiveAccountIds.FirstOrDefault();
            if (accountId is null) continue;
            try
            {
                await RefreshBatchAsync(accountId, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Refreshing stub worlds and groups failed");
            }
        }
    }

    /// <summary>
    /// The wait before the next try after one more failure: an hour after the first, then twice the
    /// last wait, at most a week. <paramref name="lastWait"/> is the wait the previous failure set,
    /// null when this stub has not failed before.
    /// </summary>
    public static TimeSpan NextRetry(TimeSpan? lastWait) =>
        lastWait is not { } last || last < FirstRetry ? FirstRetry : last * 2 > MaxRetry ? MaxRetry : last * 2;

    /// <summary>One tick: up to <see cref="BatchSize"/> stub worlds and as many groups. Returns how many were filled in.</summary>
    public async Task<int> RefreshBatchAsync(string accountId, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        List<string> worldIds;
        List<string> groupIds;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var worlds = await db.Worlds
                .Where(w => w.IsStub && (w.StubRetryAt == null || w.StubRetryAt <= now))
                .OrderBy(w => w.StubAttemptedAt != null).ThenBy(w => w.StubAttemptedAt).ThenByDescending(w => w.FirstSeenAt)
                .Take(BatchSize).ToListAsync(ct);
            var groups = await db.Groups
                .Where(g => g.IsStub && (g.StubRetryAt == null || g.StubRetryAt <= now))
                .OrderBy(g => g.StubAttemptedAt != null).ThenBy(g => g.StubAttemptedAt).ThenByDescending(g => g.FirstSeenAt)
                .Take(BatchSize).ToListAsync(ct);
            // Marked as tried (and failed) before the requests: a fetch that fills the row clears the
            // stub, and one that fails or never returns has already set its wait.
            foreach (var w in worlds) (w.StubAttemptedAt, w.StubRetryAt) = (now, now + NextRetry(w.StubRetryAt - w.StubAttemptedAt));
            foreach (var g in groups) (g.StubAttemptedAt, g.StubRetryAt) = (now, now + NextRetry(g.StubRetryAt - g.StubAttemptedAt));
            await db.SaveChangesAsync(ct);
            worldIds = worlds.Select(w => w.Id).ToList();
            groupIds = groups.Select(g => g.Id).ToList();
        }

        var done = 0;
        foreach (var worldId in worldIds)
        {
            try
            {
                if (await sync.RefreshWorldAsync(accountId, worldId, ct) is not null) done++;
            }
            catch (VrchatApiException ex)
            {
                logger.LogDebug(ex, "World {World} could not be fetched", worldId);
            }
        }
        foreach (var groupId in groupIds)
        {
            try
            {
                if (await sync.RefreshGroupAsync(accountId, groupId, ct) is not null) done++;
            }
            catch (VrchatApiException ex)
            {
                logger.LogDebug(ex, "Group {Group} could not be fetched", groupId);
            }
        }
        if (done > 0) logger.LogDebug("Filled in {Count} stub worlds/groups", done);
        return done;
    }
}
