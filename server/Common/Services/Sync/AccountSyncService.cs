using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.Services.Live;
using VRCX.Common.Services.Sessions;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;
// Entity names that also exist as API models resolve to the entities here; the API side is Vrchat.Models.*.
using User = VRCX.Common.VrcxDb.User;
using World = VRCX.Common.VrcxDb.World;
using Avatar = VRCX.Common.VrcxDb.Avatar;
using Group = VRCX.Common.VrcxDb.Group;
using Instance = VRCX.Common.VrcxDb.Instance;

namespace VRCX.Common.Services.Sync;

/// <summary>
/// The pull side: fetches what VRChat knows about an account and stores it. The friend sync is
/// cheap and frequent; the full sync pulls the rest (groups, favorites, moderations, notes,
/// notifications) on a longer cycle. The Refresh* methods fetch-and-store one object on demand.
/// </summary>
public interface IAccountSyncService
{
    Task SyncFriendsAsync(string accountId, CancellationToken ct);
    /// <summary>
    /// The original's 5-minute getCurrentUser. The friend list is only fetched too when the last one is
    /// over an hour old, as the original refreshes it hourly (the full sync does that).
    /// </summary>
    Task RefreshCurrentUserAsync(string accountId, CancellationToken ct);
    Task SyncEverythingAsync(string accountId, CancellationToken ct);
    Task<User?> RefreshUserAsync(string accountId, string userId, CancellationToken ct);
    /// <summary>Reads profile/{id}, the source of the bio, and stores it.</summary>
    Task RefreshProfileAsync(string accountId, string userId, CancellationToken ct);
    Task<World?> RefreshWorldAsync(string accountId, string worldId, CancellationToken ct);
    Task<Avatar?> RefreshAvatarAsync(string accountId, string avatarId, CancellationToken ct);
    Task<Group?> RefreshGroupAsync(string accountId, string groupId, CancellationToken ct);
    Task<Instance?> RefreshInstanceAsync(string accountId, string location, CancellationToken ct);
    Task SyncFavoritesAsync(string accountId, CancellationToken ct);
    Task AddFavoriteAsync(string accountId, string type, string targetId, string group, CancellationToken ct);
    Task RemoveFavoriteAsync(string accountId, string targetId, CancellationToken ct);
    Task AddLocalFavoriteAsync(string accountId, FavoriteType type, string targetId, string group, CancellationToken ct);
}

/// <inheritdoc />
public sealed class AccountSyncService(
    IAccountSessionManager sessions,
    IDbContextFactory<VrcxContext> dbFactory,
    VrchatStore store,
    ILiveEventBus bus,
    ILogger<AccountSyncService> logger) : IAccountSyncService
{
    // One sync per account at a time: a manual sync, the login-triggered one and the Hangfire
    // jobs can overlap, and two of them inserting the same new users race on the primary key.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    private async Task<IDisposable> LockAsync(string accountId, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    public async Task SyncFriendsAsync(string accountId, CancellationToken ct)
    {
        using var _ = await LockAsync(accountId, ct);
        await SyncFriendsCoreAsync(accountId, ct);
    }

    /// <summary>How old the stored friend list may get before the 5-minute refresh fetches it again.</summary>
    public static readonly TimeSpan FriendListMaxAge = TimeSpan.FromHours(1);

    public async Task RefreshCurrentUserAsync(string accountId, CancellationToken ct)
    {
        using var _ = await LockAsync(accountId, ct);
        DateTimeOffset? lastFriendSync;
        await using (var check = await dbFactory.CreateDbContextAsync(ct))
            lastFriendSync = await check.Accounts.Where(a => a.UserId == accountId).Select(a => a.LastFriendSyncAt).FirstOrDefaultAsync(ct);
        if (lastFriendSync is null || DateTimeOffset.UtcNow - lastFriendSync > FriendListMaxAge)
        {
            await SyncFriendsCoreAsync(accountId, ct);
            return;
        }

        var client = sessions.Require(accountId);
        var me = await client.GetCurrentUserAsync(ct);
        var now = DateTimeOffset.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await store.UpsertCurrentUserAsync(db, me, now, ct);
        var account = await db.Accounts.FindAsync([accountId], ct);
        if (account is not null) ApplyCurrentUser(account, me);
        if (account is not null) await store.RecordAvatarWearAsync(db, account, me.CurrentAvatar, now, ct);
        await db.SaveChangesAsync(ct);
        await sessions.PersistCookiesAsync(accountId, ct);
    }

    private static void ApplyCurrentUser(Account account, CurrentUser me)
    {
        account.HomeLocation = me.HomeLocation;
        account.FriendGroupNames = me.FriendGroupNames.ToArray();
        account.TwoFactorEnabled = me.TwoFactorAuthEnabled;
        account.SharedConnectionsOptOut = me.HasSharedConnectionsOptOut;
    }

    private async Task SyncFriendsCoreAsync(string accountId, CancellationToken ct)
    {
        var client = sessions.Require(accountId);
        var me = await client.GetCurrentUserAsync(ct);
        var friends = await client.GetAllFriendsAsync(ct);
        var now = DateTimeOffset.UtcNow;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await store.UpsertCurrentUserAsync(db, me, now, ct);
        var stillFriends = await FindMissedFriendsAsync(db, client, accountId, me, friends, ct);
        var diff = await store.SyncFriendsAsync(db, accountId, me, friends, stillFriends, now, ct);
        var account = await db.Accounts.FindAsync([accountId], ct);
        if (account is not null)
        {
            account.LastFriendSyncAt = now;
            await store.RecordAvatarWearAsync(db, account, me.CurrentAvatar, now, ct);
            ApplyCurrentUser(account, me);
        }
        await db.SaveChangesAsync(ct);
        await sessions.PersistCookiesAsync(accountId, ct);

        if (diff.Added.Count > 0 || diff.Removed.Count > 0)
        {
            var names = friends.ToDictionary(f => f.Id, f => f.DisplayName);
            var numbers = await db.Friendships.Where(f => f.OwnerUserId == accountId).OrderByDescending(f => f.Id)
                .Select(f => new { f.FriendUserId, f.FriendNumber }).ToListAsync(ct);
            foreach (var added in diff.Added)
                bus.Publish(LiveEvents.Friendship(accountId, added, names.GetValueOrDefault(added, added), true, numbers.FirstOrDefault(n => n.FriendUserId == added)?.FriendNumber ?? 0));
            foreach (var removed in diff.Removed)
                bus.Publish(LiveEvents.Friendship(accountId, removed, removed, false, numbers.FirstOrDefault(n => n.FriendUserId == removed)?.FriendNumber ?? 0));
        }
    }

    // The friend list is paged and shifts while it is read, so someone can be missing from it for one
    // sync without having gone anywhere. Like the original, an unfriend only counts once the account's
    // own friend ids and VRChat's friend status both agree. Without an answer they stay friends.
    private async Task<IReadOnlySet<string>> FindMissedFriendsAsync(
        VrcxContext db, VrchatApiClient client, string accountId, CurrentUser me, IReadOnlyList<LimitedUserFriend> friends, CancellationToken ct)
    {
        var listed = friends.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        var missing = await db.Friendships.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.EndedAt == null && !listed.Contains(f.FriendUserId))
            .Select(f => f.FriendUserId).ToListAsync(ct);
        var ids = me.Friends.ToHashSet(StringComparer.Ordinal);
        var stillFriends = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in missing)
        {
            if (ids.Contains(id))
            {
                stillFriends.Add(id);
                continue;
            }
            try
            {
                var status = await client.GetFriendStatusAsync(id, ct);
                if (status is null || status.IsFriend) stillFriends.Add(id);
            }
            catch (VrchatApiException ex)
            {
                logger.LogWarning(ex, "Could not confirm unfriend of {User} for {Account}", id, accountId);
                stillFriends.Add(id);
            }
        }
        return stillFriends;
    }

    public async Task SyncEverythingAsync(string accountId, CancellationToken ct)
    {
        using var _ = await LockAsync(accountId, ct);
        await SyncFriendsCoreAsync(accountId, ct);
        var client = sessions.Require(accountId);

        await Step("groups", async () =>
        {
            var groups = await client.GetUserGroupsWithRepresentationAsync(accountId, ct);
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var before = await GroupSnapshotAsync(db, accountId, ct);
            await store.SyncUserGroupsAsync(db, accountId, groups, DateTimeOffset.UtcNow, ct);
            // The original's onGroupLeft: a group the account was in and VRChat no longer lists.
            var listed = groups.Select(g => g.GroupId).ToHashSet(StringComparer.Ordinal);
            foreach (var left in before.Values.Where(b => !listed.Contains(b.GroupId)))
                db.Notifications.Add(VrchatStore.GroupChangeNotice(accountId, left.GroupId, left.Name, left.IconUrl, "Left group", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(ct);

            // Roles and the account's own membership details come from the full group object.
            foreach (var g in groups)
            {
                var full = await client.GetGroupAsync(g.GroupId, includeRoles: true, ct);
                if (full is null) continue;
                await using var groupDb = await dbFactory.CreateDbContextAsync(ct);
                await store.UpsertGroupAsync(groupDb, full, DateTimeOffset.UtcNow, ct);
                if (before.TryGetValue(g.GroupId, out var old))
                    await AddGroupChangesAsync(groupDb, accountId, old, full, ct);
                await groupDb.SaveChangesAsync(ct);
            }
        });

        await Step("favorites", () => SyncFavoritesCoreAsync(accountId, ct));

        await Step("player moderations", async () =>
        {
            var moderations = await client.GetPlayerModerationsAsync(ct);
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await store.SyncPlayerModerationsAsync(db, accountId, moderations, DateTimeOffset.UtcNow, ct);
            await db.SaveChangesAsync(ct);
        });

        await Step("user notes", async () =>
        {
            var notes = new List<Vrchat.Models.UserNote>();
            for (var offset = 0; ; offset += 100)
            {
                var page = await client.GetUserNotesAsync(100, offset, ct);
                notes.AddRange(page);
                if (page.Count < 100) break;
            }
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await store.SyncUserNotesAsync(db, accountId, notes, DateTimeOffset.UtcNow, ct);
            await db.SaveChangesAsync(ct);
        });

        await Step("notifications", async () =>
        {
            var v1 = await client.GetNotificationsAsync(100, 0, false, ct);
            var v2 = await client.GetNotificationsV2Async(100, 0, ct);
            var now = DateTimeOffset.UtcNow;
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            foreach (var n in v1) await store.UpsertNotificationAsync(db, accountId, n, now, ct);
            foreach (var n in v2) await store.UpsertNotificationAsync(db, accountId, n, now, ct);
            await db.SaveChangesAsync(ct);
        });

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await db.Accounts.Where(a => a.UserId == accountId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastFullSyncAt, DateTimeOffset.UtcNow), ct);
        }
        await sessions.PersistCookiesAsync(accountId, ct);

        async Task Step(string name, Func<Task> work)
        {
            try
            {
                await work();
            }
            catch (VrchatUnauthorizedException)
            {
                throw;
            }
            catch (Exception ex) when (ex is VrchatApiException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Full sync step '{Step}' failed for {Account}; the rest continues", name, accountId);
            }
        }
    }

    public async Task<User?> RefreshUserAsync(string accountId, string userId, CancellationToken ct)
    {
        var api = await sessions.Require(accountId).GetUserAsync(userId, ct);
        if (api is null) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await store.UpsertUserAsync(db, api, DateTimeOffset.UtcNow, ct);
        if (await store.ApplySeenNoteAsync(db, accountId, userId, api.Note, DateTimeOffset.UtcNow, ct))
        {
            // As the original's getLatestUserNotes: the newest notes hold the one just written.
            var latest = await sessions.Require(accountId).GetUserNotesAsync(10, 0, ct);
            await store.SyncUserNotesAsync(db, accountId, latest, DateTimeOffset.UtcNow, ct, complete: false);
        }
        await db.SaveChangesAsync(ct);
        await RefreshProfileAsync(accountId, userId, ct);
        await db.Entry(user).ReloadAsync(ct);
        return user;
    }

    public async Task RefreshProfileAsync(string accountId, string userId, CancellationToken ct)
    {
        var profile = await sessions.Require(accountId).GetPublicProfileAsync(userId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (profile is not null) await store.ApplyProfileAsync(db, userId, profile, DateTimeOffset.UtcNow, ct);
        else if (await db.Users.FindAsync([userId], ct) is { } user) user.ProfileFetchedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<World?> RefreshWorldAsync(string accountId, string worldId, CancellationToken ct)
    {
        var api = await sessions.Require(accountId).GetWorldAsync(worldId, ct);
        if (api is null) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var world = await store.UpsertWorldAsync(db, api, DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct);
        return world;
    }

    /// <summary>Read every favorite group and favorite from VRChat again, as the Favorites page's refresh does.</summary>
    public async Task SyncFavoritesAsync(string accountId, CancellationToken ct)
    {
        using var _ = await LockAsync(accountId, ct);
        await SyncFavoritesCoreAsync(accountId, ct);
    }

    private async Task SyncFavoritesCoreAsync(string accountId, CancellationToken ct)
    {
        var client = sessions.Require(accountId);
        var groups = await client.GetFavoriteGroupsAsync(ct);
        var favorites = await client.GetAllFavoritesAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await store.SyncFavoritesAsync(db, accountId, groups, favorites.DistinctBy(f => f.Id).ToList(), DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct);

        // The cards need names and pictures. Only these lists give them for avatars that are not public.
        var now = DateTimeOffset.UtcNow;
        foreach (var avatar in (await client.GetFavoriteAvatarsAsync(ct)).Where(a => a.Name != "???").DistinctBy(a => a.Id))
            await store.UpsertAvatarAsync(db, avatar, now, ct);
        foreach (var world in (await client.GetFavoriteWorldsAsync(ct)).DistinctBy(w => w.Id))
            await store.UpsertWorldAsync(db, world, now, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddFavoriteAsync(string accountId, string type, string targetId, string group, CancellationToken ct)
    {
        var favorite = await sessions.Require(accountId).AddFavoriteAsync(type, targetId, group, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await store.UpsertFavoriteAsync(db, accountId, favorite, DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveFavoriteAsync(string accountId, string targetId, CancellationToken ct)
    {
        try
        {
            await sessions.Require(accountId).RemoveFavoriteAsync(targetId, ct);
        }
        catch (VrchatApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // VRChat no longer has it (an unfriend drops it there, upstream issue 269): the stored copy goes too.
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Favorites.Where(f => f.OwnerUserId == accountId && f.TargetId == targetId).ExecuteDeleteAsync(ct);
    }

    public async Task AddLocalFavoriteAsync(string accountId, FavoriteType type, string targetId, string group, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.LocalFavorites.AnyAsync(f => f.OwnerUserId == accountId && f.Type == type && f.GroupName == group && f.TargetId == targetId, ct)) return;
        var now = DateTimeOffset.UtcNow;
        await store.EnsureFavoriteTargetAsync(db, type, targetId, now, ct);
        db.LocalFavorites.Add(new LocalFavorite { OwnerUserId = accountId, Type = type, GroupName = group, TargetId = targetId, CreatedAt = now });
        await db.SaveChangesAsync(ct);
    }

    public async Task<Avatar?> RefreshAvatarAsync(string accountId, string avatarId, CancellationToken ct)
    {
        var api = await sessions.Require(accountId).GetAvatarAsync(avatarId, ct);
        if (api is null) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var avatar = await store.UpsertAvatarAsync(db, api, DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct);
        return avatar;
    }

    public async Task<Group?> RefreshGroupAsync(string accountId, string groupId, CancellationToken ct)
    {
        var api = await sessions.Require(accountId).GetGroupAsync(groupId, true, ct);
        if (api is null) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await store.UpsertGroupAsync(db, api, DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct);
        return group;
    }

    public async Task<Instance?> RefreshInstanceAsync(string accountId, string location, CancellationToken ct)
    {
        var api = await sessions.Require(accountId).GetInstanceAsync(location, ct);
        if (api is null) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var instance = await store.UpsertInstanceAsync(db, api, DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct);
        return instance;
    }

    /// <summary>What the account's groups looked like before a sync, for the original's groupChange notices.</summary>
    private sealed record GroupBefore(string GroupId, string Name, string? IconUrl, string? OwnerUserId, IReadOnlyList<string> RoleIds);

    /// <summary>
    /// The account's open memberships of groups already fetched once. A group seen for the first time
    /// has nothing to compare with, as the original skips changes before currentUserGroupsInit.
    /// </summary>
    private static async Task<Dictionary<string, GroupBefore>> GroupSnapshotAsync(VrcxContext db, string accountId, CancellationToken ct)
    {
        var rows = await db.GroupMembers
            .Where(m => m.UserId == accountId && m.LeftAt == null && m.Group.FetchedAt != null)
            .Select(m => new { m.GroupId, m.Group.Name, m.Group.IconUrl, m.Group.OwnerUserId, Roles = m.Roles.Select(r => r.RoleId).ToList() })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.GroupId, r => new GroupBefore(r.GroupId, r.Name, r.IconUrl, r.OwnerUserId, r.Roles), StringComparer.Ordinal);
    }

    /// <summary>The original's applyGroup comparisons: name, owner, and the account's own roles.</summary>
    private static async Task AddGroupChangesAsync(VrcxContext db, string accountId, GroupBefore old, VRCX.Vrchat.Models.Group full, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        void Notice(string message) => db.Notifications.Add(VrchatStore.GroupChangeNotice(accountId, full.Id, full.Name, full.IconUrl, message, now));

        if (!string.IsNullOrEmpty(old.OwnerUserId) && !string.IsNullOrEmpty(full.OwnerId) && old.OwnerUserId != full.OwnerId)
        {
            var names = await db.Users.Where(u => u.Id == old.OwnerUserId || u.Id == full.OwnerId).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
            Notice($"Owner changed from {names.GetValueOrDefault(old.OwnerUserId)} to {names.GetValueOrDefault(full.OwnerId)}");
        }
        if (!string.IsNullOrEmpty(old.Name) && !string.IsNullOrEmpty(full.Name) && old.Name != full.Name)
            Notice($"Name changed from {old.Name} to {full.Name}");
        if (full.MyMember?.RoleIds is { } newRoles)
        {
            var roleIds = old.RoleIds.Concat(newRoles).Distinct().ToList();
            var roleNames = await db.GroupRoles.Where(r => r.GroupId == full.Id && roleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
            foreach (var removed in old.RoleIds.Except(newRoles)) Notice($"Role {roleNames.GetValueOrDefault(removed, "")} removed");
            foreach (var added in newRoles.Except(old.RoleIds)) Notice($"Role {roleNames.GetValueOrDefault(added, "")} added");
        }
    }
}
