using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.Utils.Pagination;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Controller.Accounts;

// The per-account lists VRChat keeps and the full sync mirrors: notifications, player
// moderations, favorites - and the account's own instance visits (its game log).
public sealed partial class AccountsController
{
    /// <summary>The account's notifications (both feeds), newest first.</summary>
    /// <response code="200">A page of notifications</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/notifications")]
    [ProducesResponseType<PagedResult<NotificationResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListNotifications([FromRoute] string accountId, [FromQuery] PaginationQuery pagination, [FromQuery] bool includeHidden, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);

        var page = await _db.Notifications.AsNoTracking()
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
            })
            .ToPagedResultAsync(pagination, cancellationToken);
        return Ok(page);
    }

    /// <summary>Blocks, mutes and avatar hides the account has applied, as VRChat reports them.</summary>
    /// <response code="200">The moderations</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/moderations")]
    [ProducesResponseType<PlayerModerationResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListModerations([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);

        var moderations = await _db.PlayerModerations.AsNoTracking()
            .Where(m => m.OwnerUserId == accountId)
            .OrderBy(m => m.Target.DisplayName).ThenBy(m => m.Type)
            .Select(m => new PlayerModerationResponse
            {
                Id = m.Id, SourceUserId = m.OwnerUserId, SourceDisplayName = m.Owner.User.DisplayName, TargetUserId = m.TargetUserId, TargetDisplayName = m.Target.DisplayName, Type = m.Type, CreatedAt = m.CreatedAt,
            })
            .ToArrayAsync(cancellationToken);
        return Ok(moderations);
    }

    /// <summary>The account's VRChat favorites of one kind, with their groups.</summary>
    /// <param name="accountId"></param>
    /// <param name="type">friend, world or avatar</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">Groups and favorites of that kind</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/favorites")]
    [ProducesResponseType<FavoritesResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListFavorites([FromRoute] string accountId, [FromQuery] FavoriteType type, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);

        var typeName = type switch { FavoriteType.Friend => "friend", FavoriteType.World => "world", _ => "avatar" };
        var groups = await _db.FavoriteGroups.AsNoTracking()
            .Where(g => g.OwnerUserId == accountId && (g.Type == typeName || (type == FavoriteType.World && g.Type == "vrcPlusWorld")))
            .OrderBy(g => g.Name)
            .Select(g => new FavoriteGroupResponse { Id = g.Id, Type = g.Type, Name = g.Name, DisplayName = g.DisplayName, Visibility = g.Visibility })
            .ToListAsync(cancellationToken);
        var groupNames = groups.ToDictionary(g => g.Name, g => g.DisplayName);

        var rows = await _db.Favorites.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.Type == type)
            .OrderBy(f => f.GroupName).ThenBy(f => f.FirstSeenAt)
            .ToListAsync(cancellationToken);
        var ids = rows.Select(f => f.TargetId).ToList();

        var targets = await FavoriteCardsAsync(type, ids, cancellationToken);

        return Ok(new FavoritesResponse
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
        });
    }

    private sealed record FavoriteCard(string Name, string? Thumbnail, string? AuthorId = null, string? AuthorName = null, string? ReleaseStatus = null, string[]? Tags = null,
        string? Status = null, string? StatusDescription = null, string? State = null, string? Location = null, string? Traveling = null);

    private async Task<Dictionary<string, FavoriteCard>> FavoriteCardsAsync(FavoriteType type, List<string> ids, CancellationToken ct)
    {
        if (type == FavoriteType.World)
            return await _db.Worlds.AsNoTracking().Where(w => ids.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => new FavoriteCard(w.Name, w.ThumbnailImageUrl, w.AuthorId, w.AuthorName, w.ReleaseStatus, w.Tags), ct);
        if (type == FavoriteType.Avatar)
            return await _db.Avatars.AsNoTracking().Where(a => ids.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => new FavoriteCard(a.Name, a.ThumbnailImageUrl, a.AuthorId, a.AuthorName, a.ReleaseStatus, a.Tags), ct);
        var users = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(ct);
        var presence = await HttpContext.RequestServices.GetRequiredService<VRCX.Common.Services.Presence.IPresenceCache>().GetManyAsync(ids, ct);
        return users.ToDictionary(u => u.Id, u =>
        {
            var now = presence.GetValueOrDefault(u.Id);
            return new FavoriteCard(u.DisplayName, UserImage.Pick(u.IconUrl, u.CurrentAvatarThumbnailImageUrl, u.CurrentAvatarImageUrl), Tags: u.Tags, Status: u.Status,
                StatusDescription: u.StatusDescription, State: now?.State.ToString().ToLowerInvariant() ?? "offline", Location: now?.Location ?? "offline", Traveling: now?.TravelingToLocation);
        });
    }

    /// <summary>The account's own instance visits (its game log), newest first.</summary>
    /// <response code="200">A page of visits</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/visits")]
    [ProducesResponseType<PagedResult<InstanceVisitResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListVisits([FromRoute] string accountId, [FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);

        var page = await _db.InstanceVisits.AsNoTracking()
            .Where(v => v.UserId == accountId)
            .OrderByDescending(v => v.JoinedAt).ThenByDescending(v => v.Id)
            .Select(v => new InstanceVisitResponse
            {
                Id = v.Id, UserId = v.UserId, DisplayName = v.User.DisplayName, InstanceLocation = v.InstanceLocation,
                WorldId = v.Instance.WorldId, WorldName = v.Instance.World.Name, AccessType = v.Instance.Type,
                JoinedAt = v.JoinedAt, LeftAt = v.LeftAt, Source = v.Source,
            })
            .ToPagedResultAsync(pagination, cancellationToken);
        return Ok(page);
    }
}
