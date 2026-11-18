using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat.Models;
using User = VRCX.Common.VrcxDb.User;

namespace VRCX.Common.Features.Friends;

/// <summary>
/// The one writer of an account's friends: every friendship opened or ended goes through here (the
/// pipeline's friend-add and friend-delete, the friend sync's diff, the Unfriend endpoint), and it is
/// what tells <see cref="FriendsTopic"/> a friend changed, so no path can change a friend without a
/// delta. Writes inside the caller's unit of work are announced once it commits
/// (<see cref="CommitHook"/>), with the other changes that unit of work made to what a friend row
/// shows: the user's profile fields, their presence and the memo on them, whichever path made them.
/// The state kept outside Postgres (a pending offline in Redis) is announced by the code that keeps
/// it, through <see cref="PresencePendingChanged"/> and <see cref="DeleteMemoAsync"/>.
/// </summary>
public sealed class FriendWriter(VrchatStore store, FriendsTopic topic)
{
    /// <summary>friend-add from the pipeline: opens a friendship if none is open. Returns it, or null when one was already open.</summary>
    public Task<Friendship?> AddAsync(VrcxContext db, string accountId, string friendId, DateTimeOffset now, CancellationToken ct) =>
        store.AddFriendAsync(db, accountId, friendId, now, ct);

    /// <summary>friend-delete from the pipeline: ends the open friendship. Returns it, or null when none was open.</summary>
    public Task<Friendship?> RemoveAsync(VrcxContext db, string accountId, string friendId, DateTimeOffset now, CancellationToken ct) =>
        store.RemoveFriendAsync(db, accountId, friendId, now, ct);

    /// <summary>The friend sync's diff of VRChat's whole friend list (<see cref="VrchatStore.SyncFriendsAsync"/>).</summary>
    public Task<VrchatStore.FriendSyncResult> SyncAsync(
        VrcxContext db, string accountId, CurrentUser me, IReadOnlyList<LimitedUserFriend> friends, IReadOnlySet<string> stillFriends, DateTimeOffset now, CancellationToken ct) =>
        store.SyncFriendsAsync(db, accountId, me, friends, stillFriends, now, ct);

    /// <summary>
    /// Sets last_confirmed_at on the friendships a sync confirmed, in one statement after its save
    /// rather than one UPDATE per friend. No row shows it, so there is nothing to announce.
    /// </summary>
    public static Task ConfirmAsync(VrcxContext db, VrchatStore.FriendSyncResult result, DateTimeOffset now, CancellationToken ct) =>
        result.Confirmed.Count == 0
            ? Task.CompletedTask
            : db.Friendships.Where(f => result.Confirmed.Contains(f.Id)).ExecuteUpdateAsync(s => s.SetProperty(f => f.LastConfirmedAt, now), ct);

    /// <summary>
    /// The account unfriended them through VRCXD (VRChat said yes): the friendship ends now, as the
    /// pipeline's friend-delete would end it. Saves.
    /// </summary>
    public async Task UnfriendAsync(VrcxContext db, string accountId, string friendId, DateTimeOffset now, CancellationToken ct)
    {
        var open = await db.Friendships.CurrentOf(accountId).Where(f => f.FriendUserId == friendId).ToListAsync(ct);
        foreach (var friendship in open) friendship.EndedAt = now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The account sent or withdrew a friend request through VRCXD: a FriendRequest or
    /// CancelFriendRequest row of its friend log, with the name they had then. Saves.
    /// </summary>
    public static async Task LogRequestAsync(VrcxContext db, string accountId, string userId, string type, DateTimeOffset now, CancellationToken ct)
    {
        var name = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        db.FriendRequestEvents.Add(new FriendRequestEvent { OwnerUserId = accountId, TargetUserId = userId, DisplayName = name ?? userId, Type = type, CreatedAt = now });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Removes a row from the account's friend log view; the history behind it stays. Saves.</summary>
    public static async Task HideLogEntryAsync(VrcxContext db, string accountId, string key, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.FriendLogHiddenEntries.AnyAsync(h => h.OwnerUserId == accountId && h.EntryKey == key, ct)) return;
        db.FriendLogHiddenEntries.Add(new FriendLogHiddenEntry { OwnerUserId = accountId, EntryKey = key, HiddenAt = now });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes the user's memo and announces the changed friend rows after commit.</summary>
    public async Task DeleteMemoAsync(VrcxContext db, string userId, CancellationToken ct)
    {
        var memo = await db.UserMemos.SingleOrDefaultAsync(m => m.UserId == userId, ct);
        if (memo is null) return;
        db.UserMemos.Remove(memo);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A pending offline for the user was queued or dropped in Redis (<see cref="PresenceDebouncer"/>).</summary>
    public void PresencePendingChanged(string userId) => topic.UsersChanged([userId]);

    /// <summary>The user columns a friend row shows (FriendQueries.ListFriendsAsync): a change to only others is not announced.</summary>
    private static readonly HashSet<string> ListedColumns =
    [
        nameof(User.DisplayName), nameof(User.TrustLevel), nameof(User.Status), nameof(User.StatusDescription),
        nameof(User.LastActivity), nameof(User.LastLogin), nameof(User.IconUrl), nameof(User.CurrentAvatarThumbnailImageUrl),
        nameof(User.CurrentAvatarImageUrl), nameof(User.Tags), nameof(User.DeveloperType), nameof(User.LastPlatform),
        nameof(User.IconFrame), nameof(User.ProfileEffect),
    ];

    /// <summary>What one unit of work changed about friends, read before its save.</summary>
    private sealed record Pending(HashSet<string> Users, List<(string AccountId, string UserId)> Friendships);

    private readonly ConditionalWeakTable<DbContext, Pending> _pending = new();

    private void Collect(VrcxContext db)
    {
        var users = new HashSet<string>(StringComparer.Ordinal);
        var friendships = new List<(string, string)>();
        foreach (var entry in db.ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case Friendship f when entry.State is EntityState.Added or EntityState.Deleted
                    || (entry.State == EntityState.Modified && entry.Property(nameof(Friendship.EndedAt)).IsModified):
                    friendships.Add((f.OwnerUserId, f.FriendUserId));
                    break;
                case User u when entry.State == EntityState.Modified && entry.Properties.Any(p => p.IsModified && ListedColumns.Contains(p.Metadata.Name)):
                    users.Add(u.Id);
                    break;
                case UserMemo m when entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted:
                    users.Add(m.UserId);
                    break;
            }
        }
        users.UnionWith(db.Presence.ChangedUserIds);
        if (users.Count == 0 && friendships.Count == 0) _pending.Remove(db);
        else _pending.AddOrUpdate(db, new Pending(users, friendships));
    }

    private void Committed(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending)) return;
        _pending.Remove(context);
        topic.UsersChanged(pending.Users);
        foreach (var group in pending.Friendships.GroupBy(f => f.AccountId))
            topic.FriendshipsChanged(group.Key, group.Select(f => f.UserId).Distinct().ToList());
    }

    /// <summary>
    /// The writer's commit step: reads what a SaveChanges is about to write and announces it once it
    /// committed (registered after PresenceFlushInterceptor, so Redis has the presence by then).
    /// Resolved lazily: the writer needs the context factory this hook is part of.
    /// </summary>
    public sealed class CommitHook(Lazy<FriendWriter> writer) : SaveChangesInterceptor
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
