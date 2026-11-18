using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services;
using VRCX.Common.Services.Presence;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Friends;

public sealed class FriendResponse
{
    public required int FriendNumber { get; init; }
    public required DateTimeOffset FriendsSince { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string TrustLevel { get; init; }
    public required UserState State { get; init; }
    public required string Status { get; init; }
    public string? StatusDescription { get; init; }
    public string? Location { get; init; }
    public string? TravelingToLocation { get; init; }
    /// <summary>When they arrived at Location.</summary>
    public DateTimeOffset? LocationSince { get; init; }
    /// <summary>When they last came online, went active or went offline (the original's $online_for and $offline_for).</summary>
    public DateTimeOffset? OnlineFor { get; init; }
    public DateTimeOffset? LastActivity { get; init; }
    public DateTimeOffset? LastLogin { get; init; }
    public string? ThumbnailImageUrl { get; init; }
    public string[] Tags { get; init; } = [];
    public string DeveloperType { get; init; } = "none";
    /// <summary>VRChat's last_platform, for the mobile status dot.</summary>
    public string? Platform { get; init; }
    /// <summary>The first line of the account's memo on them, which the sidebar shows beside the name.</summary>
    public string? Nickname { get; init; }
    /// <summary>Reported offline but still inside the original's grace period, so still listed online.</summary>
    public bool PendingOffline { get; init; }
    /// <summary>For "consistent colors per user", as on the Friend List.</summary>
    public int NameHue { get; init; }
    /// <summary>The icon frame VRChat shows around their picture (an inventory template id).</summary>
    public string? IconFrame { get; init; }
    /// <summary>The friend's profile effect template id, drawn behind the Friends Locations card.</summary>
    public string? ProfileEffect { get; init; }
}

/// <summary>One row of the Friend List page: everything its columns and search need.</summary>
public sealed class FriendDetailResponse
{
    public required int FriendNumber { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string TrustLevel { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required string DeveloperType { get; init; }
    public required string Status { get; init; }
    public string? StatusDescription { get; init; }
    public string? ThumbnailImageUrl { get; init; }
    public string? Bio { get; init; }
    public required IReadOnlyList<string> BioLinks { get; init; }
    public string? Note { get; init; }
    public string? Memo { get; init; }
    /// <summary>Times the account's game log saw them join.</summary>
    public required int JoinCount { get; init; }
    public required long TimeTogetherMs { get; init; }
    public DateTimeOffset? LastSeen { get; init; }
    public required int MutualCount { get; init; }
    public required bool MutualOptedOut { get; init; }
    public DateTimeOffset? LastActivity { get; init; }
    public DateTimeOffset? LastLogin { get; init; }
    public DateOnly? DateJoined { get; init; }
    public required bool IsFavorite { get; init; }
    /// <summary>The full user object or profile has never been fetched.</summary>
    public required bool NeedsLoad { get; init; }
    /// <summary>The original's random name colour hue for this user id, 0 to 65535.</summary>
    public required int NameHue { get; init; }
}

public sealed class FriendDetailsResponse
{
    public required IReadOnlyList<FriendDetailResponse> Friends { get; init; }
    /// <summary>The account has turned off sharing its connections, so mutuals can't be loaded.</summary>
    public required bool MutualOptOut { get; init; }
}

public sealed class FriendLogEntryResponse
{
    /// <summary>Stable row key: what a delete takes.</summary>
    public required string Key { get; init; }
    /// <summary>Friend, Unfriend, FriendRequest, CancelFriendRequest, DisplayName or TrustLevel.</summary>
    public required string Type { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string UserId { get; init; }
    /// <summary>Their name at the time.</summary>
    public required string DisplayName { get; init; }
    public string? PreviousDisplayName { get; init; }
    public string? TrustLevel { get; init; }
    public string? PreviousTrustLevel { get; init; }
    public int? FriendNumber { get; init; }
}

/// <summary>Times the account's game log saw a user join, the time spent together, when it last saw them, and the friend log's dates for them.</summary>
public sealed record TimeTogetherResponse(int JoinCount, long TimeTogetherMs, DateTimeOffset? LastSeen, IReadOnlyList<FriendLogDate> FriendLog);

/// <summary>A Friend or Unfriend row of the friend log.</summary>
public sealed record FriendLogDate(string Type, DateTimeOffset CreatedAt);

/// <summary>
/// What is stored about an account's friends: the friend list with presence, the friend log, and
/// the time spent with a user. Read only; shared by the Accounts endpoints and the MCP tools.
/// </summary>
public sealed class FriendQueries(VrcxContext db, IPresenceCache presence, IPendingPresenceQueue pending)
{
    /// <summary>
    /// The account's current friends with their latest known presence, in friend-number order: GET
    /// friends and the friends topic's rows (Features/Friends/FriendsTopic), so both show the same.
    /// With <paramref name="onlyUserIds"/>, just those of them that are still friends.
    /// </summary>
    public async Task<FriendResponse[]> ListFriendsAsync(string accountId, CancellationToken cancellationToken, IReadOnlyCollection<string>? onlyUserIds = null)
    {
        var current = db.Friendships.AsNoTracking().CurrentOf(accountId);
        if (onlyUserIds is not null) current = current.Where(f => onlyUserIds.Contains(f.FriendUserId));
        var friends = await current
            .OrderBy(f => f.FriendNumber)
            .Select(f => new
            {
                f.FriendNumber,
                f.StartedAt,
                f.Friend.Id,
                f.Friend.DisplayName,
                f.Friend.TrustLevel,
                f.Friend.Status,
                f.Friend.StatusDescription,
                f.Friend.LastActivity,
                f.Friend.LastLogin,
                ThumbnailImageUrl = UserImage.Pick(f.Friend.IconUrl, f.Friend.CurrentAvatarThumbnailImageUrl, f.Friend.CurrentAvatarImageUrl),
                f.Friend.Tags,
                f.Friend.DeveloperType,
                f.Friend.LastPlatform,
                f.Friend.IconFrame,
                f.Friend.ProfileEffect,
                Memo = db.UserMemos.Where(m => m.UserId == f.FriendUserId).Select(m => m.Memo).FirstOrDefault(),
            })
            .ToArrayAsync(cancellationToken);
        var ids = friends.Select(f => f.Id).ToArray();
        var now = await presence.GetManyAsync(ids, cancellationToken);
        var waiting = await pending.PendingAsync(ids, cancellationToken);
        return friends.Select(f =>
        {
            var where = now.GetValueOrDefault(f.Id);
            return new FriendResponse
            {
                FriendNumber = f.FriendNumber,
                FriendsSince = f.StartedAt,
                UserId = f.Id,
                DisplayName = f.DisplayName,
                TrustLevel = f.TrustLevel,
                State = where?.State ?? UserState.Offline,
                Status = f.Status,
                StatusDescription = f.StatusDescription,
                Location = where?.Location,
                TravelingToLocation = where?.TravelingToLocation,
                LocationSince = where?.LocationSince,
                OnlineFor = where?.StateSince ?? where?.PresenceChangedAt,
                LastActivity = f.LastActivity,
                LastLogin = f.LastLogin,
                ThumbnailImageUrl = f.ThumbnailImageUrl,
                Tags = f.Tags,
                DeveloperType = f.DeveloperType,
                // The original's computeUserPlatform: VRChat's platform unless it is empty, offline or web.
                Platform = where?.Platform is { Length: > 0 } live && live is not ("offline" or "web") ? live : f.LastPlatform,
                Nickname = string.IsNullOrEmpty(f.Memo) ? null : f.Memo.Split('\n')[0],
                PendingOffline = waiting.Contains(f.Id),
                NameHue = NameHue(f.Id),
                IconFrame = string.IsNullOrEmpty(f.IconFrame) ? null : f.IconFrame,
                ProfileEffect = string.IsNullOrEmpty(f.ProfileEffect) ? null : f.ProfileEffect,
            };
        }).ToArray();
    }

    /// <summary>
    /// Every current friend with what the Friend List page shows and searches: GET friends/details.
    /// Null when no such account is logged into this server.
    /// </summary>
    public async Task<FriendDetailsResponse?> ListFriendDetailsAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => new { a.SharedConnectionsOptOut }).FirstOrDefaultAsync(cancellationToken);
        if (account is null) return null;

        var friends = await db.Friendships.AsNoTracking().CurrentOf(accountId)
            .Select(f => new { f.FriendNumber, User = f.Friend })
            .ToListAsync(cancellationToken);
        var ids = friends.Select(f => f.User.Id).ToArray();

        var notes = await db.UserNotes.AsNoTracking().Where(n => n.OwnerUserId == accountId && ids.Contains(n.TargetUserId))
            .Select(n => new { n.TargetUserId, n.Note }).ToListAsync(cancellationToken);
        var noteByUser = notes.GroupBy(n => n.TargetUserId).ToDictionary(g => g.Key, g => g.First().Note);
        var memos = await db.UserMemos.AsNoTracking().Where(m => ids.Contains(m.UserId)).ToDictionaryAsync(m => m.UserId, m => m.Memo, cancellationToken);

        // Only the game log says they were in the same instance as the account. Identical rows are
        // counted once: the same join has been stored twice in places. Summed by Postgres: a friend
        // list of a few hundred has tens of thousands of visits behind it, and only three numbers
        // per friend leave the database.
        var together = await db.InstanceVisits.AsNoTracking()
            .Where(v => v.Source == VisitSource.GameLog && ids.Contains(v.UserId))
            .Select(v => new { v.UserId, v.InstanceLocation, v.JoinedAt, v.LeftAt })
            .Distinct()
            .GroupBy(v => v.UserId)
            .Select(g => new
            {
                g.Key,
                Joins = g.Count(),
                Ms = g.Where(v => v.LeftAt != null).Sum(v => (v.LeftAt!.Value - v.JoinedAt).TotalMilliseconds),
                LastSeen = g.Max(v => v.LeftAt ?? v.JoinedAt),
            })
            .ToDictionaryAsync(g => g.Key, cancellationToken);

        var mutualCounts = await db.MutualFriendLinks.AsNoTracking().Where(l => l.OwnerUserId == accountId)
            .GroupBy(l => l.FriendUserId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var optedOut = (await db.MutualGraphMetas.AsNoTracking().Where(m => m.OwnerUserId == accountId && m.OptedOut)
            .Select(m => m.FriendUserId).ToListAsync(cancellationToken)).ToHashSet();

        var favorites = await FavoriteFriends.IdSetAsync(db, accountId, cancellationToken);

        return new FriendDetailsResponse
        {
            MutualOptOut = account.SharedConnectionsOptOut,
            Friends = friends.Select(f =>
            {
                var user = f.User;
                var seen = together.GetValueOrDefault(user.Id);
                return new FriendDetailResponse
                {
                    FriendNumber = f.FriendNumber,
                    UserId = user.Id,
                    DisplayName = user.DisplayName,
                    TrustLevel = user.TrustLevel,
                    Tags = user.Tags,
                    DeveloperType = user.DeveloperType,
                    Status = user.Status,
                    StatusDescription = user.StatusDescription,
                    ThumbnailImageUrl = UserImage.Pick(user.IconUrl, user.CurrentAvatarThumbnailImageUrl, user.CurrentAvatarImageUrl),
                    Bio = user.Bio,
                    BioLinks = user.BioLinks,
                    Note = noteByUser.GetValueOrDefault(user.Id),
                    Memo = memos.GetValueOrDefault(user.Id),
                    JoinCount = seen?.Joins ?? 0,
                    TimeTogetherMs = (long)(seen?.Ms ?? 0),
                    LastSeen = seen?.LastSeen,
                    MutualCount = mutualCounts.GetValueOrDefault(user.Id),
                    MutualOptedOut = optedOut.Contains(user.Id),
                    LastActivity = user.LastActivity,
                    LastLogin = user.LastLogin,
                    DateJoined = user.DateJoined,
                    IsFavorite = favorites.Contains(user.Id),
                    NeedsLoad = user.LastFullFetchAt == null || user.ProfileFetchedAt == null,
                    NameHue = NameHue(user.Id),
                };
            }).ToArray(),
        };
    }

    /// <summary>Whether the account is logged into this server: what the friend endpoints answer 404 on.</summary>
    public Task<bool> AccountExistsAsync(string accountId, CancellationToken cancellationToken) =>
        db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken);

    /// <summary>The original's random name colour hue for a user id, 0 to 65535.</summary>
    public static int NameHue(string userId)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(userId));
        return (hash[3] << 8) | hash[4];
    }

    /// <summary>
    /// The account's friend log: friendships starting and ending, friend requests it sent through
    /// VRCX, and friends' name and trust changes while they were friends. Newest first, without the
    /// rows the account removed. Whole by default; with <paramref name="limit"/>, a page of the
    /// entries older than <paramref name="before"/>: at least limit of them (fewer only at the end),
    /// and every entry at the oldest time in the page, so the next page is the one before that time
    /// and no entry is cut in two pages. Paged in SQL: a page reads only its own rows.
    /// </summary>
    public async Task<FriendLogEntryResponse[]> FriendLogAsync(string accountId, CancellationToken cancellationToken, int? limit = null, DateTimeOffset? before = null)
    {
        if (limit is <= 0) limit = null;

        var baseline = await FriendLogBaselineAsync(accountId, cancellationToken);
        // Hidden keys are "Type:id"; as ids per type they filter in SQL, so a page is counted after them.
        var hiddenKeys = await db.FriendLogHiddenEntries.AsNoTracking().Where(h => h.OwnerUserId == accountId).Select(h => h.EntryKey).ToListAsync(cancellationToken);
        long[] Hidden(string type) => hiddenKeys
            .Select(k => k.Split(':', 2))
            .Where(k => k.Length == 2 && k[0] == type && long.TryParse(k[1], out _))
            .Select(k => long.Parse(k[1], System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var (hiddenFriend, hiddenUnfriend, hiddenName, hiddenTrust, hiddenRequest, hiddenCancel) =
            (Hidden("Friend"), Hidden("Unfriend"), Hidden("DisplayName"), Hidden("TrustLevel"), Hidden("FriendRequest"), Hidden("CancelFriendRequest"));

        var friendships = db.Friendships.AsNoTracking().Where(f => f.OwnerUserId == accountId);
        // Each kind of entry as SQL, with the rules that decide whether a row is an entry: the
        // baseline's friendships are not, nor a change outside the friendship, nor a veteran toggle.
        IQueryable<FriendLogRow>[] sources =
        [
            friendships.Where(f => (baseline == null || f.StartedAt != baseline) && !hiddenFriend.Contains(f.Id))
                .Select(f => new FriendLogRow { Type = "Friend", Id = f.Id, At = f.StartedAt, UserId = f.FriendUserId, FriendNumber = f.FriendNumber, CurrentName = f.Friend.DisplayName }),
            friendships.Where(f => f.EndedAt != null && !hiddenUnfriend.Contains(f.Id))
                .Select(f => new FriendLogRow { Type = "Unfriend", Id = f.Id, At = f.EndedAt!.Value, UserId = f.FriendUserId, CurrentName = f.Friend.DisplayName }),
            friendships.SelectMany(f => db.UserDisplayNameChanges.Where(c => c.UserId == f.FriendUserId && c.ChangedAt >= f.StartedAt && (f.EndedAt == null || c.ChangedAt <= f.EndedAt)),
                    (f, c) => new { f, c })
                .Where(x => !hiddenName.Contains(x.c.Id))
                .Select(x => new FriendLogRow { Type = "DisplayName", Id = x.c.Id, At = x.c.ChangedAt, UserId = x.f.FriendUserId, FriendNumber = x.f.FriendNumber, DisplayName = x.c.DisplayName, PreviousDisplayName = x.c.PreviousDisplayName }),
            friendships.SelectMany(f => db.UserTrustChanges.Where(c => c.UserId == f.FriendUserId && c.ChangedAt >= f.StartedAt && (f.EndedAt == null || c.ChangedAt <= f.EndedAt)),
                    (f, c) => new { f, c })
                // The original ignores a friend flipping between Trusted and Veteran: it is a VRChat display setting.
                .Where(x => !hiddenTrust.Contains(x.c.Id)
                    && !(x.c.PreviousTrustLevel == "Trusted User" && x.c.TrustLevel == "Veteran User")
                    && !(x.c.PreviousTrustLevel == "Veteran User" && x.c.TrustLevel == "Trusted User"))
                .Select(x => new FriendLogRow { Type = "TrustLevel", Id = x.c.Id, At = x.c.ChangedAt, UserId = x.f.FriendUserId, FriendNumber = x.f.FriendNumber, CurrentName = x.f.Friend.DisplayName, TrustLevel = x.c.TrustLevel, PreviousTrustLevel = x.c.PreviousTrustLevel }),
            db.FriendRequestEvents.AsNoTracking().Where(r => r.OwnerUserId == accountId)
                .Where(r => !(r.Type == "FriendRequest" && hiddenRequest.Contains(r.Id)) && !(r.Type == "CancelFriendRequest" && hiddenCancel.Contains(r.Id)))
                .Select(r => new FriendLogRow { Type = r.Type, Id = r.Id, At = r.CreatedAt, UserId = r.TargetUserId, DisplayName = r.DisplayName }),
        ];
        if (before is { } cutoff) sources = sources.Select(q => q.Where(r => r.At < cutoff)).ToArray();

        // A page: the newest limit times of each kind, and of those together the limit-th newest is
        // the oldest time in the page; then everything from that time on.
        if (limit is { } pageSize)
        {
            var times = new List<DateTimeOffset>();
            foreach (var q in sources)
                times.AddRange(await q.OrderByDescending(r => r.At).Select(r => r.At).Take(pageSize).ToListAsync(cancellationToken));
            if (times.Count > pageSize)
            {
                var oldest = times.OrderByDescending(t => t).ElementAt(pageSize - 1);
                sources = sources.Select(q => q.Where(r => r.At >= oldest)).ToArray();
            }
        }
        var rows = new List<FriendLogRow>();
        foreach (var q in sources) rows.AddRange(await q.ToListAsync(cancellationToken));

        // Their name at the time: the name the next change after it replaced, else the current one.
        var named = rows.Where(r => r.CurrentName is not null).Select(r => r.UserId).Distinct().ToArray();
        var changesByUser = (await db.UserDisplayNameChanges.AsNoTracking().Where(c => named.Contains(c.UserId)).OrderBy(c => c.ChangedAt).ToListAsync(cancellationToken))
            .ToLookup(c => c.UserId);
        string NameAt(string userId, string current, DateTimeOffset at) =>
            changesByUser[userId].FirstOrDefault(c => c.ChangedAt > at)?.PreviousDisplayName ?? current;

        return rows.Select(r => new FriendLogEntryResponse
        {
            Key = $"{r.Type}:{r.Id}", Type = r.Type, CreatedAt = r.At, UserId = r.UserId,
            DisplayName = r.CurrentName is { } current ? NameAt(r.UserId, current, r.At) : r.DisplayName!,
            PreviousDisplayName = r.PreviousDisplayName, TrustLevel = r.TrustLevel, PreviousTrustLevel = r.PreviousTrustLevel, FriendNumber = r.FriendNumber,
        }).OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Key, StringComparer.Ordinal).ToArray();
    }

    private sealed class FriendLogRow
    {
        public required string Type { get; init; }
        public long Id { get; init; }
        public DateTimeOffset At { get; init; }
        public required string UserId { get; init; }
        public int? FriendNumber { get; init; }
        /// <summary>Set for the entries named as they were then (from the name changes); the others carry <see cref="DisplayName"/>.</summary>
        public string? CurrentName { get; init; }
        public string? DisplayName { get; init; }
        public string? PreviousDisplayName { get; init; }
        public string? TrustLevel { get; init; }
        public string? PreviousTrustLevel { get; init; }
    }

    /// <summary>
    /// The first sync records everyone who was already a friend at one instant. Those are a
    /// starting point, not new friends, and the original logged nothing for them either.
    /// </summary>
    private async Task<DateTimeOffset?> FriendLogBaselineAsync(string accountId, CancellationToken cancellationToken)
    {
        var first = await db.Friendships.AsNoTracking().Where(f => f.OwnerUserId == accountId)
            .GroupBy(f => f.StartedAt).OrderBy(g => g.Key).Select(g => new { g.Key, Count = g.Count() })
            .FirstOrDefaultAsync(cancellationToken);
        return first is { Count: > 1 } ? first.Key : null;
    }

    /// <summary>
    /// How often the account's game log saw a user join and how long they were together: the
    /// original user dialog's Join Count and Time Together, counted as the Friend List does.
    /// </summary>
    public async Task<TimeTogetherResponse> TimeTogetherAsync(string accountId, string userId, CancellationToken cancellationToken)
    {
        // Identical rows are counted once: the same join has been stored twice in places.
        var visits = await db.InstanceVisits.AsNoTracking()
            .Where(v => v.Source == VisitSource.GameLog && v.UserId == userId)
            .Select(v => new { v.InstanceLocation, v.JoinedAt, v.LeftAt })
            .Distinct()
            .ToListAsync(cancellationToken);
        // The original's getUserStats: Last Seen is the newest join or leave, but while they are here
        // (their newest stay is still open) it is the one before, not this join.
        var ordered = visits.OrderByDescending(v => v.JoinedAt).ToList();
        var past = ordered.Count > 0 && ordered[0].LeftAt is null ? ordered.Skip(1).ToList() : ordered;
        DateTimeOffset? lastSeen = past.Count > 0 ? past.Max(v => v.LeftAt ?? v.JoinedAt) : null;

        return new TimeTogetherResponse(
            visits.Count,
            (long)visits.Where(v => v.LeftAt != null).Sum(v => (v.LeftAt!.Value - v.JoinedAt).TotalMilliseconds),
            lastSeen,
            await FriendLogForUserAsync(accountId, userId, cancellationToken));
    }

    /// <summary>
    /// The user's Friend and Unfriend rows of the account's friend log, newest first: the original's
    /// getFriendLogHistoryForUserId behind the user dialog's Friended and Unfriended.
    /// </summary>
    public async Task<IReadOnlyList<FriendLogDate>> FriendLogForUserAsync(string accountId, string userId, CancellationToken cancellationToken)
    {
        var friendships = await db.Friendships.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.FriendUserId == userId)
            .Select(f => new { f.Id, f.StartedAt, f.EndedAt })
            .ToListAsync(cancellationToken);
        if (friendships.Count == 0) return [];
        var baseline = await FriendLogBaselineAsync(accountId, cancellationToken);
        var hidden = (await db.FriendLogHiddenEntries.AsNoTracking().Where(h => h.OwnerUserId == accountId).Select(h => h.EntryKey).ToListAsync(cancellationToken)).ToHashSet();
        var entries = new List<FriendLogDate>();
        foreach (var f in friendships)
        {
            if (f.StartedAt != baseline && !hidden.Contains($"Friend:{f.Id}")) entries.Add(new FriendLogDate("Friend", f.StartedAt));
            if (f.EndedAt is { } ended && !hidden.Contains($"Unfriend:{f.Id}")) entries.Add(new FriendLogDate("Unfriend", ended));
        }
        return entries.OrderByDescending(e => e.CreatedAt).ToList();
    }
}
