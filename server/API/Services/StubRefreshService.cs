using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services.Sessions;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.API.Services;

/// <summary>
/// Fills in worlds and groups we only know by id. A location tag creates stub rows so the
/// feed and visits can reference them; the original fetched the world the moment it showed
/// one, so here the newest stubs are fetched a few at a time through any live account. A
/// stub VRChat no longer serves (deleted world, private group) is left alone for an hour.
/// </summary>
public sealed class StubRefreshService(
    IDbContextFactory<VrcxContext> dbFactory,
    IAccountSessionManager sessions,
    IAccountSyncService sync,
    ILogger<StubRefreshService> logger) : BackgroundService
{
    private const int BatchSize = 8;
    private static readonly TimeSpan Retry = TimeSpan.FromHours(1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _attempted = new();

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

    private async Task RefreshBatchAsync(string accountId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, at) in _attempted.ToArray())
            if (now - at > Retry) _attempted.TryRemove(id, out _);

        List<string> worldIds;
        List<string> groupIds;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            worldIds = await db.Worlds.AsNoTracking().Where(w => w.IsStub).OrderByDescending(w => w.FirstSeenAt).Select(w => w.Id).Take(BatchSize * 4).ToListAsync(ct);
            groupIds = await db.Groups.AsNoTracking().Where(g => g.IsStub).OrderByDescending(g => g.FirstSeenAt).Select(g => g.Id).Take(BatchSize * 4).ToListAsync(ct);
        }

        var done = 0;
        foreach (var worldId in worldIds.Where(id => !_attempted.ContainsKey(id)).Take(BatchSize))
        {
            _attempted[worldId] = now;
            try
            {
                await sync.RefreshWorldAsync(accountId, worldId, ct);
                done++;
            }
            catch (VrchatApiException ex)
            {
                logger.LogDebug(ex, "World {World} could not be fetched", worldId);
            }
        }
        foreach (var groupId in groupIds.Where(id => !_attempted.ContainsKey(id)).Take(BatchSize))
        {
            _attempted[groupId] = now;
            try
            {
                await sync.RefreshGroupAsync(accountId, groupId, ct);
                done++;
            }
            catch (VrchatApiException ex)
            {
                logger.LogDebug(ex, "Group {Group} could not be fetched", groupId);
            }
        }
        if (done > 0) logger.LogDebug("Filled in {Count} stub worlds/groups", done);
    }
}
