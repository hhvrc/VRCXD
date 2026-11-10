using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services.Presence;

/// <summary>
/// Sends a unit of work's presence changes to the cache once Postgres has committed its events.
/// A cache failure is logged, not thrown: the events are safe and reconciling repairs the cache.
/// </summary>
public sealed class PresenceFlushInterceptor(Lazy<IPresenceCache> cache, ILogger<PresenceFlushInterceptor> logger) : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await FlushAsync(eventData.Context, cancellationToken);
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        FlushAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    private async Task FlushAsync(DbContext? context, CancellationToken ct)
    {
        if (context is not VrcxContext db) return;
        var writes = db.Presence.TakeChanges();
        if (writes.Count == 0) return;
        try
        {
            await cache.Value.SetManyAsync(writes, ct);
        }
        catch (Exception ex) when (ex is StackExchange.Redis.RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "{Count} presence change(s) did not reach Redis and will be reconciled from the log", writes.Count);
        }
    }
}
