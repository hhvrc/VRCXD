using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Controller.Accounts;

public sealed record QuickSearchFriend(string Id, string Name, string Memo, string Note, string? ImageUrl);
public sealed record QuickSearchItem(string Id, string Name, string? OwnerId, string? ImageUrl);

public sealed class QuickSearchIndexResponse
{
    public required IReadOnlyList<QuickSearchFriend> Friends { get; init; }
    public required IReadOnlyList<QuickSearchItem> OwnAvatars { get; init; }
    public required IReadOnlyList<QuickSearchItem> FavAvatars { get; init; }
    public required IReadOnlyList<QuickSearchItem> OwnWorlds { get; init; }
    public required IReadOnlyList<QuickSearchItem> FavWorlds { get; init; }
    public required IReadOnlyList<QuickSearchItem> Groups { get; init; }
}

// What the original's Ctrl+K search looks through: friends with their memos and notes, own and
// favorite avatars and worlds (VRChat and local favorites), and joined groups.
public sealed partial class AccountsController
{
    /// <summary>Everything the quick search dialog matches against, read once when it opens.</summary>
    /// <response code="200">The index</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/quick-search")]
    [ProducesResponseType<QuickSearchIndexResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetQuickSearchIndex([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);

        var friends = await _db.Friendships.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.EndedAt == null)
            .Select(f => new
            {
                f.FriendUserId,
                f.Friend.DisplayName,
                f.Friend.IconUrl,
                f.Friend.CurrentAvatarImageUrl,
                f.Friend.CurrentAvatarThumbnailImageUrl,
                Memo = _db.UserMemos.Where(m => m.UserId == f.FriendUserId).Select(m => m.Memo).FirstOrDefault(),
                Note = _db.UserNotes.Where(n => n.OwnerUserId == accountId && n.TargetUserId == f.FriendUserId).Select(n => n.Note).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var favoriteIds = await _db.Favorites.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && (f.Type == FavoriteType.Avatar || f.Type == FavoriteType.World))
            .Select(f => new { f.Type, f.TargetId })
            .Union(_db.LocalFavorites.AsNoTracking()
                .Where(f => f.OwnerUserId == accountId && (f.Type == FavoriteType.Avatar || f.Type == FavoriteType.World))
                .Select(f => new { f.Type, f.TargetId }))
            .ToListAsync(cancellationToken);
        var favAvatarIds = favoriteIds.Where(f => f.Type == FavoriteType.Avatar).Select(f => f.TargetId).ToList();
        var favWorldIds = favoriteIds.Where(f => f.Type == FavoriteType.World).Select(f => f.TargetId).ToList();

        var avatars = await _db.Avatars.AsNoTracking()
            .Where(a => a.AuthorId == accountId || favAvatarIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Name, a.AuthorId, Image = a.ThumbnailImageUrl ?? a.ImageUrl })
            .ToListAsync(cancellationToken);
        var worlds = await _db.Worlds.AsNoTracking()
            .Where(w => w.AuthorId == accountId || favWorldIds.Contains(w.Id))
            .Select(w => new { w.Id, w.Name, w.AuthorId, Image = w.ThumbnailImageUrl ?? w.ImageUrl })
            .ToListAsync(cancellationToken);
        var groups = await _db.GroupMembers.AsNoTracking()
            .Where(m => m.UserId == accountId && m.MembershipStatus == "member")
            .Select(m => new QuickSearchItem(m.GroupId, m.Group.Name, m.Group.OwnerUserId, m.Group.IconUrl ?? m.Group.BannerUrl))
            .ToListAsync(cancellationToken);

        var favAvatarSet = favAvatarIds.ToHashSet();
        var favWorldSet = favWorldIds.ToHashSet();
        return Ok(new QuickSearchIndexResponse
        {
            Friends = friends.Select(f => new QuickSearchFriend(f.FriendUserId, f.DisplayName, f.Memo ?? "", f.Note ?? "",
                UserImage.Pick(f.IconUrl, f.CurrentAvatarThumbnailImageUrl, f.CurrentAvatarImageUrl))).ToList(),
            OwnAvatars = avatars.Where(a => a.AuthorId == accountId).Select(a => new QuickSearchItem(a.Id, a.Name, a.AuthorId, a.Image)).ToList(),
            FavAvatars = avatars.Where(a => favAvatarSet.Contains(a.Id)).Select(a => new QuickSearchItem(a.Id, a.Name, a.AuthorId, a.Image)).ToList(),
            OwnWorlds = worlds.Where(w => w.AuthorId == accountId).Select(w => new QuickSearchItem(w.Id, w.Name, w.AuthorId, w.Image)).ToList(),
            FavWorlds = worlds.Where(w => favWorldSet.Contains(w.Id)).Select(w => new QuickSearchItem(w.Id, w.Name, w.AuthorId, w.Image)).ToList(),
            Groups = groups,
        });
    }
}
