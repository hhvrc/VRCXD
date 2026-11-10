using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.Common.Services.Feed;

/// <summary>One row of the feed: the original's feed_* tables unioned, newest first.</summary>
public sealed class FeedEntry
{
    /// <summary>GPS, Online, Offline, Status, Avatar, Bio.</summary>
    public required string Type { get; init; }
    public required long RowId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }

    // GPS / Online / Offline
    public string? Location { get; init; }
    public string? WorldName { get; init; }
    public string? GroupName { get; init; }
    public string? PreviousLocation { get; init; }
    public string? PreviousWorldName { get; init; }
    /// <summary>Milliseconds spent at the previous location (GPS) or at Location (Offline).</summary>
    public long? Time { get; init; }

    // Status
    public string? Status { get; init; }
    public string? StatusDescription { get; init; }
    public string? PreviousStatus { get; init; }
    public string? PreviousStatusDescription { get; init; }

    // Bio
    public string? Bio { get; init; }
    public string? PreviousBio { get; init; }
    public string[]? BioLinks { get; init; }
    public string[]? PreviousBioLinks { get; init; }

    // Avatar
    public string? OwnerId { get; init; }
    public string? PreviousOwnerId { get; init; }
    public string? AvatarName { get; init; }
    public string? PreviousAvatarName { get; init; }
    public string? CurrentAvatarImageUrl { get; init; }
    public string? CurrentAvatarThumbnailImageUrl { get; init; }
    public string? PreviousCurrentAvatarImageUrl { get; init; }
    public string? PreviousCurrentAvatarThumbnailImageUrl { get; init; }
}

public sealed class FeedQuery
{
    /// <summary>Subset of GPS, Online, Offline, Status, Avatar, Bio; empty = all.</summary>
    public IReadOnlyCollection<string> Types { get; init; } = [];
    public string Search { get; init; } = "";
    /// <summary>Only favorite friends.</summary>
    public bool Vip { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int Limit { get; init; } = 500;
}

public interface IFeedService
{
    Task<IReadOnlyList<FeedEntry>> QueryAsync(string accountId, FeedQuery query, CancellationToken ct);
}

/// <summary>
/// The original kept five feed tables per account and unioned them; here the same rows come
/// from presence_events, user_status_changes, user_bio_changes and user_avatar_changes,
/// restricted to the account's friends (past or present, like rows that were written while
/// they were friends). Each source is queried with the same limit and the merge happens here.
/// </summary>
public sealed class FeedService(IDbContextFactory<VrcxContext> dbFactory) : IFeedService
{
    public static readonly string[] AllTypes = ["GPS", "Online", "Offline", "Status", "Avatar", "Bio"];

    public async Task<IReadOnlyList<FeedEntry>> QueryAsync(string accountId, FeedQuery query, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var types = query.Types.Count == 0 ? AllTypes : query.Types.ToArray();
        // The client's Search Limit Returns setting goes up to 100000 (the original's SEARCH_LIMIT_MAX).
        var limit = Math.Clamp(query.Limit, 1, 100_000);
        var search = query.Search.Trim();
        var like = $"%{EscapeLike(search)}%";
        var locationSearch = search.StartsWith("wrld_", StringComparison.Ordinal) || search.StartsWith("grp_", StringComparison.Ordinal);

        var friendIds = db.Friendships.Where(f => f.OwnerUserId == accountId).Select(f => f.FriendUserId);
        if (query.Vip)
        {
            var groups = await FavoriteFriends.GroupsAsync(db, accountId, ct);
            friendIds = friendIds.Intersect(FavoriteFriends.Ids(db, accountId, groups));
        }

        var rows = new List<FeedEntry>();

        var wantsGps = types.Contains("GPS");
        var wantsOnline = types.Contains("Online");
        var wantsOffline = types.Contains("Offline");
        if (wantsGps || wantsOnline || wantsOffline)
        {
            var q = db.PresenceEvents.AsNoTracking()
                .Where(e => friendIds.Contains(e.UserId) && e.PreviousState != null)
                .Where(e => e.Kind != PresenceKind.Location || (e.PreviousLocation != null && e.PreviousLocation != "offline" && e.PreviousLocation != "" && e.PreviousLocation != "traveling" && e.PreviousState == UserState.Online));
            // Kind -> feed type: a move is GPS; leaving the game is Offline; entering it is Online.
            if (!wantsGps) q = q.Where(e => e.Kind != PresenceKind.Location);
            if (!wantsOffline) q = q.Where(e => !((e.Kind == PresenceKind.Offline || e.Kind == PresenceKind.Active) && e.PreviousState == UserState.Online));
            if (!wantsOnline) q = q.Where(e => !(e.Kind == PresenceKind.Online && e.PreviousState != UserState.Online));
            q = q.Where(e => e.Kind == PresenceKind.Location
                             || ((e.Kind == PresenceKind.Offline || e.Kind == PresenceKind.Active) && e.PreviousState == UserState.Online)
                             || (e.Kind == PresenceKind.Online && e.PreviousState != UserState.Online));
            q = ApplyDates(q, e => e.At, query);
            if (search.Length > 0)
            {
                q = locationSearch
                    ? q.Where(e => (e.Location != null && EF.Functions.ILike(e.Location, like)) || (e.PreviousLocation != null && EF.Functions.ILike(e.PreviousLocation, like)))
                    : q.Where(e => EF.Functions.ILike(e.User.DisplayName, like)
                                   || (e.Instance != null && EF.Functions.ILike(e.Instance.World.Name, like))
                                   || (e.Instance != null && e.Instance.Group != null && EF.Functions.ILike(e.Instance.Group.Name, like)));
            }
            var presence = await q.OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Take(limit)
                .Select(e => new
                {
                    e.Id, e.At, e.UserId, e.User.DisplayName, e.Kind, e.PreviousState, e.Location, e.PreviousLocation, e.DurationAtPrevious,
                    WorldName = e.Instance != null ? e.Instance.World.Name : null,
                    GroupName = e.Instance != null && e.Instance.Group != null ? e.Instance.Group.Name : null,
                })
                .ToListAsync(ct);

            // Offline rows show where they were; GPS rows show both sides. Name the previous worlds in one go.
            var previousWorldIds = presence.Select(p => VrchatLocation.Parse(p.PreviousLocation).WorldId).Where(w => w.Length > 0).Distinct().ToList();
            var previousWorldNames = previousWorldIds.Count == 0 ? new Dictionary<string, string>() :
                await db.Worlds.AsNoTracking().Where(w => previousWorldIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name, ct);
            var previousGroupIds = presence.Select(p => VrchatLocation.Parse(p.PreviousLocation).GroupId).OfType<string>().Distinct().ToList();
            var previousGroupNames = previousGroupIds.Count == 0 ? new Dictionary<string, string>() :
                await db.Groups.AsNoTracking().Where(g => previousGroupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);

            foreach (var p in presence)
            {
                var previous = VrchatLocation.Parse(p.PreviousLocation);
                var previousWorld = previous.WorldId.Length > 0 ? previousWorldNames.GetValueOrDefault(previous.WorldId) : null;
                var previousGroup = previous.GroupId is not null ? previousGroupNames.GetValueOrDefault(previous.GroupId) : null;
                if (p.Kind == PresenceKind.Location)
                {
                    rows.Add(new FeedEntry
                    {
                        Type = "GPS", RowId = p.Id, CreatedAt = p.At, UserId = p.UserId, DisplayName = p.DisplayName,
                        Location = p.Location, WorldName = p.WorldName, GroupName = p.GroupName,
                        PreviousLocation = p.PreviousLocation, PreviousWorldName = previousWorld,
                        Time = (long?)p.DurationAtPrevious?.TotalMilliseconds,
                    });
                }
                else if (p.Kind == PresenceKind.Online)
                {
                    rows.Add(new FeedEntry
                    {
                        Type = "Online", RowId = p.Id, CreatedAt = p.At, UserId = p.UserId, DisplayName = p.DisplayName,
                        Location = p.Location, WorldName = p.WorldName, GroupName = p.GroupName,
                    });
                }
                else
                {
                    rows.Add(new FeedEntry
                    {
                        Type = "Offline", RowId = p.Id, CreatedAt = p.At, UserId = p.UserId, DisplayName = p.DisplayName,
                        Location = p.PreviousLocation, WorldName = previousWorld, GroupName = previousGroup,
                        Time = (long?)p.DurationAtPrevious?.TotalMilliseconds,
                    });
                }
            }
        }

        if (types.Contains("Status") && !locationSearch)
        {
            var q = db.UserStatusChanges.AsNoTracking().Where(c => friendIds.Contains(c.UserId));
            q = ApplyDates(q, c => c.ChangedAt, query);
            if (search.Length > 0)
                q = q.Where(c => EF.Functions.ILike(c.User.DisplayName, like) || EF.Functions.ILike(c.Status, like) || (c.StatusDescription != null && EF.Functions.ILike(c.StatusDescription, like)));
            rows.AddRange(await q.OrderByDescending(c => c.ChangedAt).ThenByDescending(c => c.Id).Take(limit)
                .Select(c => new FeedEntry
                {
                    Type = "Status", RowId = c.Id, CreatedAt = c.ChangedAt, UserId = c.UserId, DisplayName = c.User.DisplayName,
                    Status = c.Status, StatusDescription = c.StatusDescription, PreviousStatus = c.PreviousStatus, PreviousStatusDescription = c.PreviousStatusDescription,
                }).ToListAsync(ct));
        }

        if (types.Contains("Bio") && !locationSearch)
        {
            var q = db.UserBioChanges.AsNoTracking().Where(c => friendIds.Contains(c.UserId));
            q = ApplyDates(q, c => c.ChangedAt, query);
            if (search.Length > 0)
                q = q.Where(c => EF.Functions.ILike(c.User.DisplayName, like) || (c.Bio != null && EF.Functions.ILike(c.Bio, like)) || (c.PreviousBio != null && EF.Functions.ILike(c.PreviousBio, like)) || c.BioLinks.Any(l => EF.Functions.ILike(l, like)));
            rows.AddRange(await q.OrderByDescending(c => c.ChangedAt).ThenByDescending(c => c.Id).Take(limit)
                .Select(c => new FeedEntry
                {
                    Type = "Bio", RowId = c.Id, CreatedAt = c.ChangedAt, UserId = c.UserId, DisplayName = c.User.DisplayName,
                    Bio = c.Bio, PreviousBio = c.PreviousBio, BioLinks = c.BioLinks, PreviousBioLinks = c.PreviousBioLinks,
                }).ToListAsync(ct));
        }

        if (types.Contains("Avatar") && !locationSearch)
        {
            var q = db.UserAvatarChanges.AsNoTracking().Where(c => friendIds.Contains(c.UserId));
            q = ApplyDates(q, c => c.ChangedAt, query);
            if (search.Length > 0)
            {
                // The original's "public"/"private" words filter own vs other people's avatars.
                var lower = search.ToLowerInvariant();
                if (lower.Contains("private")) q = q.Where(c => c.AvatarOwnerId == c.UserId || EF.Functions.ILike(c.User.DisplayName, like) || (c.AvatarName != null && EF.Functions.ILike(c.AvatarName, like)));
                else if (lower.Contains("public")) q = q.Where(c => c.AvatarOwnerId != c.UserId || EF.Functions.ILike(c.User.DisplayName, like) || (c.AvatarName != null && EF.Functions.ILike(c.AvatarName, like)));
                else q = q.Where(c => EF.Functions.ILike(c.User.DisplayName, like) || (c.AvatarName != null && EF.Functions.ILike(c.AvatarName, like)));
            }
            rows.AddRange(await q.OrderByDescending(c => c.ChangedAt).ThenByDescending(c => c.Id).Take(limit)
                .Select(c => new FeedEntry
                {
                    Type = "Avatar", RowId = c.Id, CreatedAt = c.ChangedAt, UserId = c.UserId, DisplayName = c.User.DisplayName,
                    OwnerId = c.AvatarOwnerId, PreviousOwnerId = c.PreviousAvatarOwnerId, AvatarName = c.AvatarName, PreviousAvatarName = c.PreviousAvatarName,
                    CurrentAvatarImageUrl = c.ImageUrl, CurrentAvatarThumbnailImageUrl = c.ThumbnailImageUrl,
                    PreviousCurrentAvatarImageUrl = c.PreviousImageUrl, PreviousCurrentAvatarThumbnailImageUrl = c.PreviousThumbnailImageUrl,
                }).ToListAsync(ct));
        }

        return rows.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.RowId).Take(limit).ToList();
    }

    private static IQueryable<T> ApplyDates<T>(IQueryable<T> q, System.Linq.Expressions.Expression<Func<T, DateTimeOffset>> at, FeedQuery query)
    {
        if (query.From is { } from) q = q.Where(Lambda(at, x => x >= from));
        if (query.To is { } to) q = q.Where(Lambda(at, x => x <= to));
        return q;
    }

    private static System.Linq.Expressions.Expression<Func<T, bool>> Lambda<T>(
        System.Linq.Expressions.Expression<Func<T, DateTimeOffset>> selector,
        System.Linq.Expressions.Expression<Func<DateTimeOffset, bool>> predicate)
    {
        var parameter = selector.Parameters[0];
        var body = new Replace(predicate.Parameters[0], selector.Body).Visit(predicate.Body)!;
        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private sealed class Replace(System.Linq.Expressions.Expression from, System.Linq.Expressions.Expression to) : System.Linq.Expressions.ExpressionVisitor
    {
        public override System.Linq.Expressions.Expression? Visit(System.Linq.Expressions.Expression? node) => node == from ? to : base.Visit(node);
    }

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
