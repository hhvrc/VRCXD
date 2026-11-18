using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services.Presence;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services.Queries;

public sealed class NotificationResponse
{
    public required string Id { get; init; }
    public required int Version { get; init; }
    public required string Type { get; init; }
    public string? SenderUserId { get; init; }
    public string? SenderDisplayName { get; init; }
    public string? Title { get; init; }
    public required string Message { get; init; }
    public string? ImageUrl { get; init; }
    public string? Link { get; init; }
    public string? LinkText { get; init; }
    /// <summary>v1 details or v2 data, as VRChat sent it.</summary>
    public System.Text.Json.JsonDocument? Details { get; init; }
    /// <summary>The answers VRChat offers on a v2 notification.</summary>
    public System.Text.Json.JsonDocument? Responses { get; init; }
    public required bool Seen { get; init; }
    public required bool Hidden { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed class PlayerModerationResponse
{
    public required string Id { get; init; }
    public required string SourceUserId { get; init; }
    public required string SourceDisplayName { get; init; }
    public required string TargetUserId { get; init; }
    public required string TargetDisplayName { get; init; }
    /// <summary>block, mute, hideAvatar, interactOff, ...</summary>
    public required string Type { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}

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

/// <summary>
/// The per-account lists VRChat keeps and the full sync mirrors: notifications, player
/// moderations and favorites. Shared by the Accounts endpoints and the MCP tools.
/// </summary>
public sealed class AccountListQueries(VrcxContext db, IPresenceCache presence)
{
    /// <summary>The account's notifications (both feeds), newest first, for the caller to page.</summary>
    public IQueryable<NotificationResponse> Notifications(string accountId, bool includeHidden) =>
        db.Notifications.AsNoTracking()
            .Where(n => n.OwnerUserId == accountId && !n.LogHidden && (includeHidden || !n.Hidden))
            // A hidden friend request or group notice is finished with, and the original drops it from the page.
            .Where(n => !n.Hidden || (n.Type != "friendRequest" && n.Type != "ignoredFriendRequest" && !n.Type.Contains('.')))
            .OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id)
            .Select(n => new NotificationResponse
            {
                Id = n.Id, Version = n.Version, Type = n.Type, SenderUserId = n.SenderUserId,
                SenderDisplayName = n.Sender != null ? n.Sender.DisplayName : n.SenderUsername,
                Title = n.Title, Message = n.Message, ImageUrl = n.ImageUrl, Link = n.Link, LinkText = n.LinkText, Details = n.Details, Responses = n.Responses,
                Seen = n.Seen, Hidden = n.Hidden, CreatedAt = n.CreatedAt, ExpiresAt = n.ExpiresAt,
            });

    /// <summary>Blocks, mutes and avatar hides the account has applied, as VRChat reports them.</summary>
    public Task<PlayerModerationResponse[]> ModerationsAsync(string accountId, CancellationToken cancellationToken) =>
        db.PlayerModerations.AsNoTracking()
            .Where(m => m.OwnerUserId == accountId)
            .OrderBy(m => m.Target.DisplayName).ThenBy(m => m.Type)
            .Select(m => new PlayerModerationResponse
            {
                Id = m.Id, SourceUserId = m.OwnerUserId, SourceDisplayName = m.Owner.User.DisplayName, TargetUserId = m.TargetUserId, TargetDisplayName = m.Target.DisplayName, Type = m.Type, CreatedAt = m.CreatedAt,
            })
            .ToArrayAsync(cancellationToken);

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
}
