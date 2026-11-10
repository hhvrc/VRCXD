using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services;

/// <summary>
/// The original's allFavoriteFriendIds: who counts as a favorite friend (VIP) across the app. The
/// Social tab's Favorite Friends Groups setting picks which groups count: VRChat groups as
/// "friend:group_N", local groups as "local:Name"; nothing selected means every group.
/// </summary>
public static class FavoriteFriends
{
    public const string LocalPrefix = "local:";
    public const string RemotePrefix = "friend:";

    public static IQueryable<string> Ids(VrcxContext db, string accountId, string[]? groups)
    {
        groups ??= [];
        var remote = db.Favorites.AsNoTracking().Where(f => f.OwnerUserId == accountId && f.Type == FavoriteType.Friend);
        var local = db.LocalFavorites.AsNoTracking().Where(f => f.OwnerUserId == accountId && f.Type == FavoriteType.Friend);
        if (groups.Length > 0)
        {
            var remoteGroups = groups.Where(g => g.StartsWith(RemotePrefix, StringComparison.Ordinal)).Select(g => g[RemotePrefix.Length..]).ToArray();
            var localGroups = groups.Where(g => g.StartsWith(LocalPrefix, StringComparison.Ordinal)).Select(g => g[LocalPrefix.Length..]).ToArray();
            // As the original: picking only local groups still counts every VRChat group.
            if (remoteGroups.Length > 0) remote = remote.Where(f => f.GroupName != null && remoteGroups.Contains(f.GroupName));
            local = local.Where(f => localGroups.Contains(f.GroupName));
        }
        return remote.Select(f => f.TargetId).Concat(local.Select(f => f.TargetId));
    }

    /// <summary>
    /// Friends in exactly these groups (the Auto Status and auto-accept pickers): "friend:group_N" or
    /// "local:Name". A bare "group_N", as stored before the keys matched the original's, counts as a VRChat group.
    /// </summary>
    public static IQueryable<string> InGroups(VrcxContext db, string accountId, IEnumerable<string> groups)
    {
        var keys = groups.ToArray();
        var remoteGroups = keys.Where(g => !g.StartsWith(LocalPrefix, StringComparison.Ordinal))
            .Select(g => g.StartsWith(RemotePrefix, StringComparison.Ordinal) ? g[RemotePrefix.Length..] : g).ToArray();
        var localGroups = keys.Where(g => g.StartsWith(LocalPrefix, StringComparison.Ordinal)).Select(g => g[LocalPrefix.Length..]).ToArray();
        return db.Favorites.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.Type == FavoriteType.Friend && f.GroupName != null && remoteGroups.Contains(f.GroupName))
            .Select(f => f.TargetId)
            .Concat(db.LocalFavorites.AsNoTracking()
                .Where(f => f.OwnerUserId == accountId && f.Type == FavoriteType.Friend && localGroups.Contains(f.GroupName))
                .Select(f => f.TargetId));
    }

    /// <summary>Everyone in any friend favorite group, VRChat's or local (the original's "All Favorites").</summary>
    public static IQueryable<string> All(VrcxContext db, string accountId) => Ids(db, accountId, []);

    public static async Task<HashSet<string>> IdSetAsync(VrcxContext db, string accountId, CancellationToken ct)
    {
        return (await Ids(db, accountId, await GroupsAsync(db, accountId, ct)).ToListAsync(ct)).ToHashSet();
    }

    /// <summary>The setting. The settings column is one converted JSON value, so the whole record is read.</summary>
    public static async Task<string[]> GroupsAsync(VrcxContext db, string accountId, CancellationToken ct) =>
        (await db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => a.Settings).FirstOrDefaultAsync(ct))?.FavoriteFriendGroups ?? [];
}
