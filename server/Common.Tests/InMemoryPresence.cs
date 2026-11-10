using System.Collections.Concurrent;
using VRCX.Common.Services.Presence;

namespace VRCX.Common.Tests;

/// <summary>The presence cache's contract without Redis, one per test because user ids repeat.</summary>
public sealed class InMemoryPresenceCache : IPresenceCache
{
    private readonly ConcurrentDictionary<string, UserPresence> _presence = new();

    public Task<IReadOnlyDictionary<string, UserPresence>> GetManyAsync(IReadOnlyCollection<string> userIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, UserPresence>>(userIds.Where(_presence.ContainsKey).Distinct().ToDictionary(id => id, id => _presence[id]));

    public Task SetManyAsync(IReadOnlyCollection<PresenceWrite> writes, CancellationToken ct)
    {
        foreach (var write in writes) _presence[write.UserId] = write.Presence;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<InstanceOccupant>> GetOccupantsAsync(string instanceLocation, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<InstanceOccupant>>(_presence.Where(p => p.Value.CurrentInstanceLocation == instanceLocation).Select(p => new InstanceOccupant(p.Key, p.Value.LocationSince)).ToList());
}

public sealed class InMemoryPendingPresenceQueue : IPendingPresenceQueue
{
    private readonly ConcurrentDictionary<string, PendingPresence> _pending = new();

    public Task ScheduleAsync(string userId, PendingPresence pending, CancellationToken ct)
    {
        _pending.TryAdd(userId, pending);
        return Task.CompletedTask;
    }

    public Task CancelAsync(string userId, CancellationToken ct)
    {
        _pending.TryRemove(userId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(string UserId, PendingPresence Pending)>> TakeDueAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        var due = _pending.Where(p => p.Value.StartedAt <= cutoff).ToList();
        foreach (var entry in due) _pending.TryRemove(entry.Key, out _);
        return Task.FromResult<IReadOnlyList<(string, PendingPresence)>>(due.Select(p => (p.Key, p.Value)).ToList());
    }

    public Task<IReadOnlySet<string>> PendingAsync(IReadOnlyCollection<string> userIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlySet<string>>(userIds.Where(_pending.ContainsKey).ToHashSet());
}
