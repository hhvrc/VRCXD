using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.QuickSearch;

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

/// <summary>
/// What the original's Ctrl+K search looks through: friends with their memos and notes, own and
/// favorite avatars and worlds (VRChat and local favorites), and joined groups. Read only.
/// </summary>
public sealed class QuickSearchQueries(VrcxContext db)
{
    /// <summary>Everything the quick search dialog matches against, read once when it opens.</summary>
    public async Task<QuickSearchIndexResponse> IndexAsync(string accountId, CancellationToken cancellationToken)
    {
        var friends = await db.Friendships.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.EndedAt == null)
            .Select(f => new
            {
                f.FriendUserId,
                f.Friend.DisplayName,
                f.Friend.IconUrl,
                f.Friend.CurrentAvatarImageUrl,
                f.Friend.CurrentAvatarThumbnailImageUrl,
                Memo = db.UserMemos.Where(m => m.UserId == f.FriendUserId).Select(m => m.Memo).FirstOrDefault(),
                Note = db.UserNotes.Where(n => n.OwnerUserId == accountId && n.TargetUserId == f.FriendUserId).Select(n => n.Note).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var favoriteIds = await db.Favorites.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && (f.Type == FavoriteType.Avatar || f.Type == FavoriteType.World))
            .Select(f => new { f.Type, f.TargetId })
            .Union(db.LocalFavorites.AsNoTracking()
                .Where(f => f.OwnerUserId == accountId && (f.Type == FavoriteType.Avatar || f.Type == FavoriteType.World))
                .Select(f => new { f.Type, f.TargetId }))
            .ToListAsync(cancellationToken);
        var favAvatarIds = favoriteIds.Where(f => f.Type == FavoriteType.Avatar).Select(f => f.TargetId).ToList();
        var favWorldIds = favoriteIds.Where(f => f.Type == FavoriteType.World).Select(f => f.TargetId).ToList();

        var avatars = await db.Avatars.AsNoTracking()
            .Where(a => a.AuthorId == accountId || favAvatarIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Name, a.AuthorId, Image = a.ThumbnailImageUrl ?? a.ImageUrl })
            .ToListAsync(cancellationToken);
        var worlds = await db.Worlds.AsNoTracking()
            .Where(w => w.AuthorId == accountId || favWorldIds.Contains(w.Id))
            .Select(w => new { w.Id, w.Name, w.AuthorId, Image = w.ThumbnailImageUrl ?? w.ImageUrl })
            .ToListAsync(cancellationToken);
        var groups = await db.GroupMembers.AsNoTracking()
            .Where(m => m.UserId == accountId && m.MembershipStatus == "member")
            .Select(m => new QuickSearchItem(m.GroupId, m.Group.Name, m.Group.OwnerUserId, m.Group.IconUrl ?? m.Group.BannerUrl))
            .ToListAsync(cancellationToken);

        var favAvatarSet = favAvatarIds.ToHashSet();
        var favWorldSet = favWorldIds.ToHashSet();
        return new QuickSearchIndexResponse
        {
            Friends = friends.Select(f => new QuickSearchFriend(f.FriendUserId, f.DisplayName, f.Memo ?? "", f.Note ?? "",
                UserImage.Pick(f.IconUrl, f.CurrentAvatarThumbnailImageUrl, f.CurrentAvatarImageUrl))).ToList(),
            OwnAvatars = avatars.Where(a => a.AuthorId == accountId).Select(a => new QuickSearchItem(a.Id, a.Name, a.AuthorId, a.Image)).ToList(),
            FavAvatars = avatars.Where(a => favAvatarSet.Contains(a.Id)).Select(a => new QuickSearchItem(a.Id, a.Name, a.AuthorId, a.Image)).ToList(),
            OwnWorlds = worlds.Where(w => w.AuthorId == accountId).Select(w => new QuickSearchItem(w.Id, w.Name, w.AuthorId, w.Image)).ToList(),
            FavWorlds = worlds.Where(w => favWorldSet.Contains(w.Id)).Select(w => new QuickSearchItem(w.Id, w.Name, w.AuthorId, w.Image)).ToList(),
            Groups = groups,
        };
    }
}
