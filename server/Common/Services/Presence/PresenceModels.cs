using System.Text.Json.Serialization;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services.Presence;

/// <summary>
/// Where a user is right now. This lives in Redis, not on the users row. presence_events in
/// Postgres stays the permanent log, and this can always be rebuilt from it.
/// </summary>
public sealed record UserPresence
{
    public static readonly UserPresence Unknown = new();

    [JsonConverter(typeof(JsonStringEnumConverter<UserState>))]
    public UserState State { get; init; } = UserState.Offline;
    public string? Location { get; init; }
    public string? TravelingToLocation { get; init; }
    /// <summary>The instances row Location resolves to, when it is a real instance.</summary>
    public string? CurrentInstanceLocation { get; init; }
    public DateTimeOffset? PresenceChangedAt { get; init; }
    /// <summary>When State last changed: what Online For and Offline For count from.</summary>
    public DateTimeOffset? StateSince { get; init; }
    /// <summary>When they arrived at Location (what "time spent there" is measured from).</summary>
    public DateTimeOffset? LocationSince { get; init; }
    /// <summary>While Location is "traveling": where they were before, so the move is logged from there once they arrive.</summary>
    public string? LocationBeforeTraveling { get; init; }
    public DateTimeOffset? TravelingSince { get; init; }
    public string? Platform { get; init; }
}

/// <summary>A presence change ready for the cache, with the instance it moves the user out of.</summary>
public sealed record PresenceWrite(string UserId, UserPresence Presence, string? PreviousInstance);

/// <summary>Someone the presence cache currently places in an instance.</summary>
public sealed record InstanceOccupant(string UserId, DateTimeOffset? Since);

public interface IPresenceCache
{
    /// <summary>Users with no presence are left out of the result.</summary>
    Task<IReadOnlyDictionary<string, UserPresence>> GetManyAsync(IReadOnlyCollection<string> userIds, CancellationToken ct);
    Task SetManyAsync(IReadOnlyCollection<PresenceWrite> writes, CancellationToken ct);
    Task<IReadOnlyList<InstanceOccupant>> GetOccupantsAsync(string instanceLocation, CancellationToken ct);
}

public static class PresenceCacheExtensions
{
    public static async Task<UserPresence> GetAsync(this IPresenceCache cache, string userId, CancellationToken ct) =>
        (await cache.GetManyAsync([userId], ct)).GetValueOrDefault(userId) ?? UserPresence.Unknown;
}

/// <summary>
/// The presence read or changed by one unit of work. Changes reach the cache only after
/// SaveChanges has committed their presence_events row, so the log is never behind the cache.
/// </summary>
public sealed class PresenceUnitOfWork
{
    private readonly Dictionary<string, UserPresence> _seen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _changed = new(StringComparer.Ordinal);

    public bool TryGet(string userId, out UserPresence presence) => _seen.TryGetValue(userId, out presence!);

    public void Remember(string userId, UserPresence presence) => _seen.TryAdd(userId, presence);

    public void Change(string userId, UserPresence before, UserPresence after)
    {
        _changed.TryAdd(userId, before.CurrentInstanceLocation);
        _seen[userId] = after;
    }

    /// <summary>The instance this unit of work moved the user out of, and whether it changed them at all.</summary>
    public bool TryGetPreviousInstance(string userId, out string? previous) => _changed.TryGetValue(userId, out previous);

    public IReadOnlyList<PresenceWrite> TakeChanges()
    {
        if (_changed.Count == 0) return [];
        var writes = _changed.Select(c => new PresenceWrite(c.Key, _seen[c.Key], c.Value)).ToList();
        _changed.Clear();
        return writes;
    }

    public void Clear()
    {
        _seen.Clear();
        _changed.Clear();
    }
}

/// <summary>An online friend reported offline or active, not believed until the delay runs out.</summary>
public sealed record PendingPresence(PresenceKind Kind, string? Platform, DateTimeOffset StartedAt);

public interface IPendingPresenceQueue
{
    /// <summary>Keeps the first start time when the user is already waiting.</summary>
    Task ScheduleAsync(string userId, PendingPresence pending, CancellationToken ct);
    Task CancelAsync(string userId, CancellationToken ct);
    /// <summary>Removes and returns everything started at or before the cutoff.</summary>
    Task<IReadOnlyList<(string UserId, PendingPresence Pending)>> TakeDueAsync(DateTimeOffset cutoff, CancellationToken ct);
    /// <summary>Which of these users are waiting to go offline (the original's pendingOffline).</summary>
    Task<IReadOnlySet<string>> PendingAsync(IReadOnlyCollection<string> userIds, CancellationToken ct);
}

/// <summary>What a game-log client is doing right now.</summary>
public sealed record GameClientLive(string? CurrentInstanceLocation, DateTimeOffset LastSeenAt);

public interface IGameClientState
{
    Task<GameClientLive?> GetAsync(string clientId, CancellationToken ct);
    Task SetAsync(string clientId, GameClientLive live, CancellationToken ct);
}
