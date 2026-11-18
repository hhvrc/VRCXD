using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services;
using VRCX.Common.Services.Presence;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Favorites;

public sealed class FavoriteResponse
{
    public required string Id { get; init; }
    public required FavoriteType Type { get; init; }
    public required string TargetId { get; init; }
    /// <summary>The user's display name, the world's name or the avatar's name - whatever we have.</summary>
    public required string TargetName { get; init; }
    public string? ThumbnailImageUrl { get; init; }
    public string? GroupName { get; init; }
    public string? GroupDisplayName { get; init; }
    /// <summary>When this server first saw the favorite: the original's sort by date.</summary>
    public DateTimeOffset? CreatedAt { get; init; }
    public string? AuthorId { get; init; }
    public string? AuthorName { get; init; }
    public string? ReleaseStatus { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public string? Status { get; init; }
    public string? StatusDescription { get; init; }
    /// <summary>A friend's presence: online, active or offline.</summary>
    public string? State { get; init; }
    public string? Location { get; init; }
    public string? TravelingToLocation { get; init; }
    public bool Missing { get; init; }
}

public sealed class FavoriteGroupResponse
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Visibility { get; init; }
}

public sealed class FavoritesResponse
{
    public required IReadOnlyList<FavoriteGroupResponse> Groups { get; init; }
    public required IReadOnlyList<FavoriteResponse> Favorites { get; init; }
}

public sealed class FavoriteSlotResponse
{
    public required string Type { get; init; }
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required int Capacity { get; init; }
    public required int Count { get; init; }
    public string Visibility { get; init; } = "private";
}

public sealed class LocalFavoriteGroupResponse
{
    public required string Name { get; init; }
    public required int Count { get; init; }
    public required bool Contains { get; init; }
}

public sealed class FavoritePickerResponse
{
    public required IReadOnlyList<FavoriteSlotResponse> Groups { get; init; }
    public FavoriteSlotResponse? Current { get; init; }
    public required IReadOnlyList<LocalFavoriteGroupResponse> Local { get; init; }
    public required bool IsSupporter { get; init; }
}

public sealed class LocalFavoriteResponse
{
    public required string GroupName { get; init; }
    public required string TargetId { get; init; }
    public required string TargetName { get; init; }
    public string? ThumbnailImageUrl { get; init; }
    /// <summary>For the favorites search, which matches author names and author tags (upstream issue 735).</summary>
    public string? AuthorName { get; init; }
    /// <summary>For the export's Author ID column (upstream issue 984).</summary>
    public string? AuthorId { get; init; }
    /// <summary>So local cards can mark private ones and hide Select Avatar on them, as the original.</summary>
    public string? ReleaseStatus { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
}

/// <summary>Whether VRChat still has a local favorite that was fetched again.</summary>
public sealed record LocalFavoriteRefreshResponse(bool Available);

/// <summary>
/// The account's favorites as stored: VRChat's, which the full sync mirrors, and the local groups
/// only this server knows of. Read only; shared by the favorites endpoints and the MCP tools.
/// </summary>
public sealed class FavoriteQueries(VrcxContext db, IPresenceCache presence)
{
    /// <summary>The account's VRChat favorites of one kind, with their groups.</summary>
    public async Task<FavoritesResponse> FavoritesAsync(string accountId, FavoriteType type, CancellationToken cancellationToken)
    {
        var typeName = type switch { FavoriteType.Friend => "friend", FavoriteType.World => "world", _ => "avatar" };
        var groups = await db.FavoriteGroups.AsNoTracking()
            .Where(g => g.OwnerUserId == accountId && (g.Type == typeName || (type == FavoriteType.World && g.Type == "vrcPlusWorld")))
            .OrderBy(g => g.Name)
            .Select(g => new FavoriteGroupResponse { Id = g.Id, Type = g.Type, Name = g.Name, DisplayName = g.DisplayName, Visibility = g.Visibility })
            .ToListAsync(cancellationToken);
        var groupNames = groups.ToDictionary(g => g.Name, g => g.DisplayName);

        var rows = await db.Favorites.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.Type == type)
            .OrderBy(f => f.GroupName).ThenBy(f => f.FirstSeenAt)
            .ToListAsync(cancellationToken);
        var ids = rows.Select(f => f.TargetId).ToList();

        var targets = await FavoriteCardsAsync(type, ids, cancellationToken);

        return new FavoritesResponse
        {
            Groups = groups,
            Favorites = rows.Select(f =>
            {
                var card = targets.GetValueOrDefault(f.TargetId);
                return new FavoriteResponse
                {
                    Id = f.Id, Type = f.Type, TargetId = f.TargetId,
                    TargetName = card?.Name ?? f.TargetId,
                    ThumbnailImageUrl = card?.Thumbnail,
                    GroupName = f.GroupName,
                    GroupDisplayName = f.GroupName is not null ? groupNames.GetValueOrDefault(f.GroupName) : null,
                    CreatedAt = f.FirstSeenAt,
                    AuthorId = card?.AuthorId, AuthorName = card?.AuthorName, ReleaseStatus = card?.ReleaseStatus, Tags = card?.Tags,
                    Status = card?.Status, StatusDescription = card?.StatusDescription, State = card?.State, Location = card?.Location, TravelingToLocation = card?.Traveling,
                    Missing = card is null,
                };
            }).ToList(),
        };
    }

    private sealed record FavoriteCard(string Name, string? Thumbnail, string? AuthorId = null, string? AuthorName = null, string? ReleaseStatus = null, string[]? Tags = null,
        string? Status = null, string? StatusDescription = null, string? State = null, string? Location = null, string? Traveling = null);

    private async Task<Dictionary<string, FavoriteCard>> FavoriteCardsAsync(FavoriteType type, List<string> ids, CancellationToken ct)
    {
        if (type == FavoriteType.World)
            return await db.Worlds.AsNoTracking().Where(w => ids.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => new FavoriteCard(w.Name, w.ThumbnailImageUrl, w.AuthorId, w.AuthorName, w.ReleaseStatus, w.Tags), ct);
        if (type == FavoriteType.Avatar)
            return await db.Avatars.AsNoTracking().Where(a => ids.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => new FavoriteCard(a.Name, a.ThumbnailImageUrl, a.AuthorId, a.AuthorName, a.ReleaseStatus, a.Tags), ct);
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(ct);
        var now = await presence.GetManyAsync(ids, ct);
        return users.ToDictionary(u => u.Id, u =>
        {
            var here = now.GetValueOrDefault(u.Id);
            return new FavoriteCard(u.DisplayName, UserImage.Pick(u.IconUrl, u.CurrentAvatarThumbnailImageUrl, u.CurrentAvatarImageUrl), Tags: u.Tags, Status: u.Status,
                StatusDescription: u.StatusDescription, State: here?.State.ToString().ToLowerInvariant() ?? "offline", Location: here?.Location ?? "offline", Traveling: here?.TravelingToLocation);
        });
    }

    /// <summary>Whether something is a favorite at all, on VRChat or locally.</summary>
    public async Task<bool> IsFavoriteAsync(string accountId, string targetId, CancellationToken cancellationToken) =>
        await db.Favorites.AnyAsync(f => f.OwnerUserId == accountId && f.TargetId == targetId, cancellationToken)
        || await db.LocalFavorites.AnyAsync(f => f.OwnerUserId == accountId && f.TargetId == targetId, cancellationToken);

    /// <summary>Everything in the local favorite groups of one kind.</summary>
    public async Task<List<LocalFavoriteResponse>> LocalFavoritesAsync(string accountId, FavoriteType type, CancellationToken cancellationToken)
    {
        var rows = await db.LocalFavorites.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.Type == type)
            .OrderBy(f => f.GroupName).ThenBy(f => f.CreatedAt)
            .ToListAsync(cancellationToken);
        var names = await FavoriteTargetsAsync(type, rows.Select(f => f.TargetId).ToList(), cancellationToken);
        return rows.Select(f => new LocalFavoriteResponse
        {
            GroupName = f.GroupName,
            TargetId = f.TargetId,
            TargetName = names.TryGetValue(f.TargetId, out var n) ? n.Name : f.TargetId,
            ThumbnailImageUrl = n?.Thumbnail,
            AuthorName = n?.AuthorName,
            AuthorId = n?.AuthorId,
            ReleaseStatus = n?.ReleaseStatus,
            Tags = n?.Tags,
        }).ToList();
    }

    /// <summary>
    /// The local groups of one kind, empty ones included, with how many each holds and, for
    /// <paramref name="targetId"/>, whether it is in each.
    /// </summary>
    public async Task<List<LocalFavoriteGroupResponse>> LocalGroupsAsync(string accountId, FavoriteType type, string? targetId, CancellationToken ct)
    {
        var used = await db.LocalFavorites.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.Type == type)
            .GroupBy(f => f.GroupName)
            .Select(g => new LocalFavoriteGroupResponse { Name = g.Key, Count = g.Count(), Contains = targetId != null && g.Any(f => f.TargetId == targetId) })
            .ToListAsync(ct);
        var made = await db.LocalFavoriteGroups.AsNoTracking().Where(g => g.OwnerUserId == accountId && g.Type == type).Select(g => g.Name).ToListAsync(ct);
        var names = used.Select(g => g.Name).ToHashSet();
        return used.Concat(made.Where(n => !names.Contains(n)).Select(n => new LocalFavoriteGroupResponse { Name = n, Count = 0, Contains = false }))
            .OrderBy(g => g.Name, StringComparer.Ordinal).ToList();
    }

    private sealed record FavoriteTarget(string Name, string? Thumbnail, string? AuthorName = null, string[]? Tags = null, string? AuthorId = null, string? ReleaseStatus = null);

    private async Task<Dictionary<string, FavoriteTarget>> FavoriteTargetsAsync(FavoriteType type, List<string> ids, CancellationToken ct) => type switch
    {
        FavoriteType.Friend => await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => new FavoriteTarget(u.DisplayName, u.CurrentAvatarThumbnailImageUrl), ct),
        FavoriteType.World => await db.Worlds.AsNoTracking().Where(w => ids.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => new FavoriteTarget(w.Name, w.ThumbnailImageUrl, w.AuthorName, w.Tags, w.AuthorId, w.ReleaseStatus), ct),
        _ => await db.Avatars.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => new FavoriteTarget(a.Name, a.ThumbnailImageUrl, a.AuthorName, null, a.AuthorId, a.ReleaseStatus), ct),
    };
}
