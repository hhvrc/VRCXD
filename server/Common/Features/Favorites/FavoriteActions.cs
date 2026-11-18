using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Services;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.Common.Features.Favorites;

/// <summary>
/// What an account does with its favorites through VRCXD: the original's ChooseFavoriteGroupDialog
/// and the Favorites page's group panel. VRChat's favorites and groups are changed on VRChat with
/// the account's own session; the local groups are only this server's. A method that goes to VRChat
/// returns null when it worked, else the problem to answer with.
/// </summary>
public sealed class FavoriteActions(VrcxContext db, AccountSessionRunner runner, IAccountSyncService sync, VrchatStore store, FavoriteQueries queries)
{
    /// <summary>What the favorite dialog offers for one thing: every group slot with its count, and the local groups.</summary>
    public async Task<Result<FavoritePickerResponse>> PickerAsync(string accountId, FavoriteType type, string? targetId, CancellationToken ct)
    {
        var stored = await db.FavoriteGroups.AsNoTracking().Where(g => g.OwnerUserId == accountId).ToListAsync(ct);
        var counts = await db.Favorites.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.Type == type && f.GroupName != null)
            .GroupBy(f => f.GroupName!)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var currentGroup = await db.Favorites.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.Type == type && f.TargetId == targetId)
            .Select(f => f.GroupName)
            .FirstOrDefaultAsync(ct);
        var isFavorite = await db.Favorites.AnyAsync(f => f.OwnerUserId == accountId && f.Type == type && f.TargetId == targetId, ct);
        var locals = await queries.LocalGroupsAsync(accountId, type, targetId, ct);

        return await runner.ReadAsync(accountId, async client =>
        {
            var limits = await client.GetFavoriteLimitsAsync(ct);
            var me = await client.GetCurrentUserAsync(ct);
            var slots = FavoriteSlots(type, limits).Select(slot =>
            {
                var named = stored.FirstOrDefault(g => g.Name == slot.Name);
                return new FavoriteSlotResponse
                {
                    Type = slot.Type,
                    Name = slot.Name,
                    DisplayName = string.IsNullOrEmpty(named?.DisplayName) ? slot.DisplayName : named.DisplayName,
                    Capacity = slot.Capacity,
                    Count = counts.GetValueOrDefault(slot.Name),
                    Visibility = named?.Visibility ?? "private",
                };
            }).ToList();
            return new FavoritePickerResponse
            {
                Groups = slots,
                Current = isFavorite ? slots.FirstOrDefault(s => s.Name == currentGroup) ?? new FavoriteSlotResponse { Type = FavoriteTypeName(type), Name = currentGroup ?? "", DisplayName = currentGroup ?? "Favorites", Capacity = 0, Count = 0 } : null,
                Local = locals,
                IsSupporter = me.Tags.Contains("system_supporter"),
            };
        }, ct);
    }

    /// <summary>Adds a favorite to one of VRChat's groups.</summary>
    public Task<OpenShockProblem?> AddAsync(string accountId, string type, string targetId, string group, CancellationToken ct) =>
        runner.RunAsync(accountId, _ => sync.AddFavoriteAsync(accountId, type, targetId, group, ct), ct);

    /// <summary>Takes something out of VRChat's favorites.</summary>
    public Task<OpenShockProblem?> RemoveAsync(string accountId, string targetId, CancellationToken ct) =>
        runner.RunAsync(accountId, _ => sync.RemoveFavoriteAsync(accountId, targetId, ct), ct);

    /// <summary>Reads every favorite from VRChat again.</summary>
    public Task<OpenShockProblem?> RefreshAsync(string accountId, CancellationToken ct) =>
        runner.RunAsync(accountId, _ => sync.SyncFavoritesAsync(accountId, ct), ct);

    /// <summary>
    /// How many players are in each favorite world right now, for the original's Players sort (upstream
    /// issue 674): VRChat's favorite worlds in one call, plus any of <paramref name="ids"/> (local
    /// favorites, at most 100) fetched one by one. Momentary counts, so nothing is stored.
    /// </summary>
    public Task<Result<Dictionary<string, int>>> WorldOccupantsAsync(string accountId, string[]? ids, CancellationToken ct) =>
        runner.ReadAsync(accountId, async client =>
        {
            var occupants = new Dictionary<string, int>();
            foreach (var world in await client.GetFavoriteWorldsAsync(ct)) occupants[world.Id] = world.Occupants;
            foreach (var id in (ids ?? []).Where(id => id.StartsWith("wrld_", StringComparison.Ordinal) && !occupants.ContainsKey(id)).Distinct().Take(100))
            {
                try
                {
                    if (await client.GetWorldAsync(id, ct) is { } world) occupants[world.Id] = world.Occupants;
                }
                catch (VrchatApiException ex) when (ex is not VrchatUnauthorizedException)
                {
                    // A deleted or private world has no count; it sorts last.
                }
            }
            return occupants;
        }, ct);

    /// <summary>Renames a VRChat favorite group or changes its visibility.</summary>
    public Task<OpenShockProblem?> SaveGroupAsync(string accountId, string type, string name, string? displayName, string? visibility, CancellationToken ct) =>
        runner.RunAsync(accountId, async client =>
        {
            var saved = await client.SaveFavoriteGroupAsync(type, name, accountId, displayName?.Trim(), visibility, ct);
            var row = await db.FavoriteGroups.FirstOrDefaultAsync(g => g.OwnerUserId == accountId && g.Type == type && g.Name == name, ct);
            if (row is null)
            {
                row = new FavoriteGroup { Id = saved.Id, OwnerUserId = accountId, Type = type, Name = name };
                db.FavoriteGroups.Add(row);
            }
            row.DisplayName = saved.DisplayName ?? displayName ?? row.DisplayName;
            row.Visibility = saved.Visibility ?? visibility ?? row.Visibility;
            row.FetchedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }, ct);

    /// <summary>Empties a VRChat favorite group, then reads the favorites again.</summary>
    public Task<OpenShockProblem?> ClearGroupAsync(string accountId, string type, string name, CancellationToken ct) =>
        runner.RunAsync(accountId, async client =>
        {
            await client.ClearFavoriteGroupAsync(type, name, accountId, ct);
            await sync.SyncFavoritesAsync(accountId, ct);
        }, ct);

    /// <summary>Puts something in a local group, which only this server knows about. The group appears when first used.</summary>
    public Task AddLocalAsync(string accountId, FavoriteType type, string targetId, string group, CancellationToken ct) =>
        sync.AddLocalFavoriteAsync(accountId, type, targetId, group.Trim(), ct);

    /// <summary>
    /// Fetches one local favorite from VRChat again and stores it, the original's local Refresh, which
    /// the client runs one per second. Unavailable when VRChat no longer shows it (upstream PR 1525).
    /// </summary>
    public Task<Result<LocalFavoriteRefreshResponse>> RefreshLocalAsync(string accountId, FavoriteType type, string targetId, CancellationToken ct) =>
        runner.ReadAsync(accountId, async client =>
        {
            var now = DateTimeOffset.UtcNow;
            try
            {
                switch (type)
                {
                    case FavoriteType.World when await client.GetWorldAsync(targetId, ct) is { } world:
                        await store.UpsertWorldAsync(db, world, now, ct);
                        break;
                    case FavoriteType.Avatar when await client.GetAvatarAsync(targetId, ct) is { } avatar:
                        await store.UpsertAvatarAsync(db, avatar, now, ct);
                        break;
                    case FavoriteType.Friend when await client.GetUserAsync(targetId, ct) is { } user:
                        await store.UpsertUserAsync(db, user, now, ct);
                        break;
                    default:
                        return new LocalFavoriteRefreshResponse(false);
                }
            }
            catch (VrchatApiException ex) when (ex is not VrchatUnauthorizedException)
            {
                return new LocalFavoriteRefreshResponse(false);
            }
            await db.SaveChangesAsync(ct);
            return new LocalFavoriteRefreshResponse(true);
        }, ct);

    /// <summary>Takes something out of a local group.</summary>
    public Task RemoveLocalAsync(string accountId, FavoriteType type, string targetId, string group, CancellationToken ct) =>
        db.LocalFavorites.Where(f => f.OwnerUserId == accountId && f.Type == type && f.TargetId == targetId && f.GroupName == group).ExecuteDeleteAsync(ct);

    /// <summary>Makes an empty local group. Nothing happens when the name is blank or the group exists.</summary>
    public async Task CreateLocalGroupAsync(string accountId, FavoriteType type, string name, CancellationToken ct)
    {
        name = name.Trim();
        if (name.Length == 0 || await db.LocalFavoriteGroups.AnyAsync(g => g.OwnerUserId == accountId && g.Type == type && g.Name == name, ct)) return;
        db.LocalFavoriteGroups.Add(new LocalFavoriteGroup { OwnerUserId = accountId, Type = type, Name = name, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Renames a local group and moves its favorites with it.</summary>
    public async Task RenameLocalGroupAsync(string accountId, FavoriteType type, string name, string newName, CancellationToken ct)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || newName == name) return;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var moving = await db.LocalFavorites.Where(f => f.OwnerUserId == accountId && f.Type == type && f.GroupName == name).ToListAsync(ct);
        var present = (await db.LocalFavorites.Where(f => f.OwnerUserId == accountId && f.Type == type && f.GroupName == newName).Select(f => f.TargetId).ToListAsync(ct)).ToHashSet();
        foreach (var favorite in moving.Where(f => !present.Contains(f.TargetId)))
            db.LocalFavorites.Add(new LocalFavorite { OwnerUserId = accountId, Type = type, GroupName = newName, TargetId = favorite.TargetId, CreatedAt = favorite.CreatedAt });
        db.LocalFavorites.RemoveRange(moving);
        await db.LocalFavoriteGroups.Where(g => g.OwnerUserId == accountId && g.Type == type && g.Name == name).ExecuteDeleteAsync(ct);
        if (!await db.LocalFavoriteGroups.AnyAsync(g => g.OwnerUserId == accountId && g.Type == type && g.Name == newName, ct))
            db.LocalFavoriteGroups.Add(new LocalFavoriteGroup { OwnerUserId = accountId, Type = type, Name = newName, CreatedAt = DateTimeOffset.UtcNow });
        if (type == FavoriteType.Friend) await RenameFavoriteFriendGroupAsync(accountId, name, newName, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Deletes a local group and what is in it, as the original's delete does.</summary>
    public async Task DeleteLocalGroupAsync(string accountId, FavoriteType type, string name, CancellationToken ct)
    {
        await db.LocalFavorites.Where(f => f.OwnerUserId == accountId && f.Type == type && f.GroupName == name).ExecuteDeleteAsync(ct);
        await db.LocalFavoriteGroups.Where(g => g.OwnerUserId == accountId && g.Type == type && g.Name == name).ExecuteDeleteAsync(ct);
        if (type == FavoriteType.Friend)
        {
            await RenameFavoriteFriendGroupAsync(accountId, name, null, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>The original's favoriteCoordinator keeps the Favorite Friends Groups setting in step when a local group is renamed or deleted.</summary>
    private async Task RenameFavoriteFriendGroupAsync(string accountId, string name, string? newName, CancellationToken ct)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.UserId == accountId, ct);
        var key = FavoriteFriends.LocalPrefix + name;
        if (account is null || !account.Settings.FavoriteFriendGroups.Contains(key)) return;
        var groups = account.Settings.FavoriteFriendGroups.Where(g => g != key).ToList();
        if (newName is not null) groups.Insert(Array.IndexOf(account.Settings.FavoriteFriendGroups, key), FavoriteFriends.LocalPrefix + newName);
        account.Settings = account.Settings with { FavoriteFriendGroups = groups.Distinct().ToArray() };
    }

    private static string FavoriteTypeName(FavoriteType type) => type switch { FavoriteType.Friend => "friend", FavoriteType.World => "world", _ => "avatar" };

    // VRChat only lists the groups that were renamed, so the slots come from the limits, as in the original's buildFavoriteGroups.
    private static IEnumerable<(string Type, string Name, string DisplayName, int Capacity)> FavoriteSlots(FavoriteType type, VRCX.Vrchat.Models.FavoriteLimits limits)
    {
        if (type == FavoriteType.Friend)
        {
            for (var i = 0; i < limits.MaxFavoriteGroups.Friend; i++) yield return ("friend", $"group_{i}", $"Group {i + 1}", limits.MaxFavoritesPerGroup.Friend);
        }
        else if (type == FavoriteType.World)
        {
            for (var i = 0; i < limits.MaxFavoriteGroups.World; i++) yield return ("world", $"worlds{i + 1}", $"Group {i + 1}", limits.MaxFavoritesPerGroup.World);
            for (var i = 0; i < limits.MaxFavoriteGroups.VrcPlusWorld; i++) yield return ("vrcPlusWorld", $"vrcPlusWorlds{i + 1}", $"VRC+ Group {i + 1}", limits.MaxFavoritesPerGroup.VrcPlusWorld);
        }
        else
        {
            for (var i = 0; i < limits.MaxFavoriteGroups.Avatar; i++) yield return ("avatar", $"avatars{i + 1}", $"Group {i + 1}", limits.MaxFavoritesPerGroup.Avatar);
        }
    }
}
