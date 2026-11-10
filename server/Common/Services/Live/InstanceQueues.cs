using System.Collections.Concurrent;

namespace VRCX.Common.Services.Live;

/// <summary>One instance join queue the account is in, as the original's queuedInstances entry.</summary>
public sealed record QueuedInstance(string Location, int Position, int QueueSize, string WorldName, string GroupName, DateTimeOffset UpdatedAt);

/// <summary>
/// The instance queues each account is waiting in, from VRChat's instance-queue-* pipeline events
/// (upstream issue 1420). Like the original it lives in memory only: a queue place does not outlast
/// VRChat's own session.
/// </summary>
public sealed class InstanceQueues
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, QueuedInstance>> _byAccount = new();

    public IReadOnlyList<QueuedInstance> List(string accountId) =>
        _byAccount.TryGetValue(accountId, out var queues) ? queues.Values.OrderBy(q => q.UpdatedAt).ToList() : [];

    public void Set(string accountId, QueuedInstance queued) =>
        _byAccount.GetOrAdd(accountId, _ => new())[queued.Location] = queued;

    public QueuedInstance? Get(string accountId, string location) =>
        _byAccount.TryGetValue(accountId, out var queues) && queues.TryGetValue(location, out var queued) ? queued : null;

    public QueuedInstance? Remove(string accountId, string location) =>
        _byAccount.TryGetValue(accountId, out var queues) && queues.TryRemove(location, out var queued) ? queued : null;

    /// <summary>The original's applyQueuedInstance: moving to an instance drops every other queue.</summary>
    public IReadOnlyList<QueuedInstance> RemoveAllBut(string accountId, string? location)
    {
        if (!_byAccount.TryGetValue(accountId, out var queues)) return [];
        var removed = new List<QueuedInstance>();
        foreach (var key in queues.Keys)
        {
            if (key != location && queues.TryRemove(key, out var queued)) removed.Add(queued);
        }
        return removed;
    }
}
