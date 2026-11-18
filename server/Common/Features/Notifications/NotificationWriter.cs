using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Notifications;

/// <summary>
/// What tells <see cref="NotificationsTopic"/> a notification changed, so no path can change one
/// without a delta. Notifications are written in many places (the pipeline, the syncs, the
/// Notifications page's actions, a boop sent back, a group change noticed by a sync); all of them
/// change tracked rows, which the <see cref="CommitHook"/> sees and announces once they commit. The
/// two changes made in bulk, which no change tracker sees, are made here.
/// </summary>
public sealed class NotificationWriter(NotificationsTopic topic)
{
    /// <summary>
    /// A friend request VRChat no longer lists was answered somewhere: the original drops those on
    /// refresh. Hides every open friend request of the account that is not in <paramref name="listed"/>.
    /// </summary>
    public async Task HideAnsweredRequestsAsync(VrcxContext db, string accountId, IReadOnlyCollection<string> listed, CancellationToken ct)
    {
        var hidden = await db.Notifications
            .Where(n => n.OwnerUserId == accountId && (n.Type == "friendRequest" || n.Type == "ignoredFriendRequest") && !n.Hidden && !listed.Contains(n.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Hidden, true), ct);
        // Which ones is not known from the count, so the topic reads its rows again.
        if (hidden > 0) topic.Changed(accountId, null);
    }

    /// <summary>Takes a notification off the page. The row stays in the database. False when there is no such notification.</summary>
    public async Task<bool> RemoveFromLogAsync(VrcxContext db, string accountId, string notificationId, CancellationToken ct)
    {
        var updated = await db.Notifications.Where(n => n.OwnerUserId == accountId && n.Id == notificationId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.LogHidden, true), ct);
        if (updated > 0) topic.Changed(accountId, [notificationId]);
        return updated > 0;
    }

    private readonly ConditionalWeakTable<DbContext, List<(string AccountId, string Id)>> _pending = new();

    private void Collect(VrcxContext db)
    {
        List<(string, string)> changed = [];
        foreach (var entry in db.ChangeTracker.Entries<Notification>())
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                changed.Add((entry.Entity.OwnerUserId, entry.Entity.Id));
        if (changed.Count == 0) _pending.Remove(db);
        else _pending.AddOrUpdate(db, changed);
    }

    private void Committed(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var changed)) return;
        _pending.Remove(context);
        foreach (var account in changed.GroupBy(c => c.AccountId))
            topic.Changed(account.Key, account.Select(c => c.Id).Distinct().ToList());
    }

    /// <summary>
    /// The writer's commit step: reads which notifications a SaveChanges is about to write and
    /// announces them once it committed. Resolved lazily: the writer needs the context factory this
    /// hook is part of.
    /// </summary>
    public sealed class CommitHook(Lazy<NotificationWriter> writer) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is VrcxContext db) writer.Value.Collect(db);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (eventData.Context is VrcxContext db) writer.Value.Collect(db);
            return result;
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            writer.Value.Committed(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            writer.Value.Committed(eventData.Context);
            return result;
        }

        public override void SaveChangesFailed(DbContextErrorEventData eventData)
        {
            if (eventData.Context is { } context) writer.Value._pending.Remove(context);
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            SaveChangesFailed(eventData);
            return Task.CompletedTask;
        }
    }
}
