using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace VRCX.Common.Services.Presence;

/// <summary>
/// Current presence in Redis. Reads fall back to the log in Postgres while Redis is away, and
/// anything missed is put right when the connection returns.
/// </summary>
public sealed class RedisPresenceCache : IPresenceCache
{
    private const string PresenceKey = "vrcx:presence";
    private const string InstancePrefix = "vrcx:presence:instance:";

    private readonly IConnectionMultiplexer _redis;
    private readonly PresenceEventReader _log;
    private readonly ILogger<RedisPresenceCache> _logger;

    public RedisPresenceCache(IConnectionMultiplexer redis, PresenceEventReader log, ILogger<RedisPresenceCache> logger)
    {
        _redis = redis;
        _log = log;
        _logger = logger;
        _redis.ConnectionRestored += (_, _) => _ = ReconcileQuietlyAsync();
    }

    public async Task<IReadOnlyDictionary<string, UserPresence>> GetManyAsync(IReadOnlyCollection<string> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return new Dictionary<string, UserPresence>();
        var ids = userIds.Distinct(StringComparer.Ordinal).ToArray();
        try
        {
            var values = await _redis.GetDatabase().HashGetAsync(PresenceKey, ids.Select(id => (RedisValue)id).ToArray());
            var found = new Dictionary<string, UserPresence>(StringComparer.Ordinal);
            for (var i = 0; i < ids.Length; i++)
                if (values[i].HasValue) found[ids[i]] = Deserialize(values[i]);
            return found;
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis is unavailable, reading presence from the event log");
            return await _log.ReadAsync(ids, ct);
        }
    }

    public async Task SetManyAsync(IReadOnlyCollection<PresenceWrite> writes, CancellationToken ct)
    {
        if (writes.Count == 0) return;
        var transaction = _redis.GetDatabase().CreateTransaction();
        var tasks = new List<Task>();
        foreach (var write in writes)
        {
            tasks.Add(transaction.HashSetAsync(PresenceKey, write.UserId, JsonSerializer.Serialize(write.Presence)));
            if (write.PreviousInstance is { } left && left != write.Presence.CurrentInstanceLocation)
                tasks.Add(transaction.SetRemoveAsync(InstancePrefix + left, write.UserId));
            if (write.Presence.CurrentInstanceLocation is { } entered)
                tasks.Add(transaction.SetAddAsync(InstancePrefix + entered, write.UserId));
        }
        await transaction.ExecuteAsync();
        await Task.WhenAll(tasks);
    }

    public async Task<IReadOnlyList<InstanceOccupant>> GetOccupantsAsync(string instanceLocation, CancellationToken ct)
    {
        var ids = (await _redis.GetDatabase().SetMembersAsync(InstancePrefix + instanceLocation)).Select(v => v.ToString()).ToArray();
        var presence = await GetManyAsync(ids, ct);
        // A user whose stored presence has moved on is not here, whatever the set says.
        return presence.Where(p => p.Value.CurrentInstanceLocation == instanceLocation)
            .Select(p => new InstanceOccupant(p.Key, p.Value.LocationSince))
            .ToList();
    }

    /// <summary>
    /// Brings Redis in line with the log. Only entries older than the newest event are replaced,
    /// so states the log does not hold, like a move in progress, survive a restart.
    /// </summary>
    public async Task<int> ReconcileAsync(CancellationToken ct)
    {
        var logged = await _log.ReadAsync(null, ct);
        var stored = (await _redis.GetDatabase().HashGetAllAsync(PresenceKey))
            .ToDictionary(e => e.Name.ToString(), e => Deserialize(e.Value), StringComparer.Ordinal);
        var writes = new List<PresenceWrite>();
        foreach (var (userId, presence) in logged)
        {
            var current = stored.GetValueOrDefault(userId);
            if (current is not null && current.PresenceChangedAt >= presence.PresenceChangedAt) continue;
            writes.Add(new PresenceWrite(userId, presence, current?.CurrentInstanceLocation));
        }
        foreach (var chunk in writes.Chunk(500)) await SetManyAsync(chunk, ct);
        return writes.Count;
    }

    private async Task ReconcileQuietlyAsync()
    {
        try
        {
            var changed = await ReconcileAsync(CancellationToken.None);
            _logger.LogInformation("Redis came back, {Count} presence entries were put right from the log", changed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not reconcile presence after Redis came back");
        }
    }

    private static UserPresence Deserialize(RedisValue value) => JsonSerializer.Deserialize<UserPresence>(value.ToString())!;
}

/// <summary>Pending offline transitions, in Redis so a restart does not forget them.</summary>
public sealed class RedisPendingPresenceQueue(IConnectionMultiplexer redis) : IPendingPresenceQueue
{
    private const string DueKey = "vrcx:presence:pending";
    private const string DataKey = "vrcx:presence:pending:data";

    public async Task ScheduleAsync(string userId, PendingPresence pending, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        if (!await db.HashSetAsync(DataKey, userId, JsonSerializer.Serialize(pending), When.NotExists)) return;
        await db.SortedSetAddAsync(DueKey, userId, pending.StartedAt.ToUnixTimeMilliseconds());
    }

    public async Task CancelAsync(string userId, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        await db.SortedSetRemoveAsync(DueKey, userId);
        await db.HashDeleteAsync(DataKey, userId);
    }

    public async Task<IReadOnlyList<(string UserId, PendingPresence Pending)>> TakeDueAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var due = await db.SortedSetRangeByScoreAsync(DueKey, stop: cutoff.ToUnixTimeMilliseconds());
        var taken = new List<(string, PendingPresence)>();
        foreach (var member in due)
        {
            if (!await db.SortedSetRemoveAsync(DueKey, member)) continue;
            var data = await db.HashGetAsync(DataKey, member);
            await db.HashDeleteAsync(DataKey, member);
            if (data.HasValue) taken.Add((member.ToString(), JsonSerializer.Deserialize<PendingPresence>(data.ToString())!));
        }
        return taken;
    }

    public async Task<IReadOnlySet<string>> PendingAsync(IReadOnlyCollection<string> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return new HashSet<string>();
        try
        {
            var values = await redis.GetDatabase().HashGetAsync(DataKey, ids.Select(id => (RedisValue)id).ToArray());
            return ids.Where((_, i) => values[i].HasValue).ToHashSet(StringComparer.Ordinal);
        }
        catch (RedisException)
        {
            // Without Redis nobody is shown as pending, which is what the original shows after a restart.
            return new HashSet<string>();
        }
    }
}

/// <summary>Each game-log client's current instance and heartbeat, which change on every batch.</summary>
public sealed class RedisGameClientState(IConnectionMultiplexer redis, ILogger<RedisGameClientState> logger) : IGameClientState
{
    private const string Key = "vrcx:gamelog:clients";

    public async Task<GameClientLive?> GetAsync(string clientId, CancellationToken ct)
    {
        try
        {
            var value = await redis.GetDatabase().HashGetAsync(Key, clientId);
            return value.HasValue ? JsonSerializer.Deserialize<GameClientLive>(value.ToString()) : null;
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis is unavailable, game client {Client} has no live state", clientId);
            return null;
        }
    }

    public async Task SetAsync(string clientId, GameClientLive live, CancellationToken ct)
    {
        try
        {
            await redis.GetDatabase().HashSetAsync(Key, clientId, JsonSerializer.Serialize(live));
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis is unavailable, game client {Client} state was not stored", clientId);
        }
    }
}
