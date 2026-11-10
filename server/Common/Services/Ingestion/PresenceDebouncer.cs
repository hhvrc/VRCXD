using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using VRCX.Common.Services.Live;
using VRCX.Common.Services.Presence;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services.Ingestion;

/// <summary>
/// The original's pending-offline map: an online friend reported offline or active is not
/// believed for 170 seconds, because VRChat flaps presence while a client reconnects. A
/// friend-online or friend-location inside that window cancels it; otherwise the transition
/// is applied when it expires, with the time they left measured then. The waiting transitions
/// are in Redis, so a restart inside the window does not forget them.
/// </summary>
public sealed class PresenceDebouncer(IDbContextFactory<VrcxContext> dbFactory, VrchatStore store, IPendingPresenceQueue queue, ILiveEventBus bus, ILogger<PresenceDebouncer> logger)
{
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(170_000);

    /// <summary>False when the queue could not take it, so the caller applies the transition right away instead of losing it.</summary>
    public async Task<bool> ScheduleAsync(string userId, PresenceKind kind, string? platform, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            await queue.ScheduleAsync(userId, new PendingPresence(kind, platform, now), ct);
            return true;
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis is unavailable, applying the {Kind} for {User} without waiting", kind, userId);
            return false;
        }
    }

    public async Task CancelAsync(string userId, CancellationToken ct)
    {
        try
        {
            await queue.CancelAsync(userId, ct);
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis is unavailable, could not cancel the pending change for {User}", userId);
        }
    }

    /// <summary>Applies every pending transition older than <see cref="Delay"/>. Returns how many were applied.</summary>
    public async Task<int> SweepAsync(DateTimeOffset now, CancellationToken ct)
    {
        var applied = 0;
        foreach (var (userId, pending) in await queue.TakeDueAsync(now - Delay, ct))
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var user = await db.Users.FindAsync([userId], ct);
                if (user is null || (await store.GetPresenceAsync(db, userId, ct)).State != UserState.Online) continue;
                var presence = await store.ApplyPresenceAsync(db, user, pending.Kind, null, null, pending.Platform, now, ct);
                await db.SaveChangesAsync(ct);
                if (presence is not null) bus.Publish(LiveEvents.Presence(user, presence));
                applied++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Applying the delayed {Kind} for {User} failed", pending.Kind, userId);
            }
        }
        return applied;
    }
}

/// <summary>Ticks the debouncer once a second (the original's pendingOffline interval).</summary>
public sealed class PendingPresenceService(PresenceDebouncer debouncer, ILogger<PendingPresenceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await debouncer.SweepAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (RedisException ex)
            {
                // Still queued in Redis, so the next tick after it returns picks them up.
                logger.LogWarning(ex, "Redis is unavailable, pending presence changes are waiting");
            }
        }
    }
}
