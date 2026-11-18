using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services.Presence;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services.Queries;

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
    /// <summary>The account's current friends with their latest known presence, in friend-number order.</summary>
    public async Task<FriendResponse[]> ListFriendsAsync(string accountId, CancellationToken cancellationToken)
    {
        var friends = await db.Friendships.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.EndedAt == null)
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
        var current = await presence.GetManyAsync(ids, cancellationToken);
        var waiting = await pending.PendingAsync(ids, cancellationToken);
        return friends.Select(f =>
        {
            var where = current.GetValueOrDefault(f.Id);
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

    /// <summary>The original's random name colour hue for a user id, 0 to 65535.</summary>
    public static int NameHue(string userId)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(userId));
        return (hash[3] << 8) | hash[4];
    }

    /// <summary>The account's friend log: friendships starting and ending, friend requests it sent through VRCX, and friends' name and trust changes while they were friends. Newest first, without the rows the account removed.</summary>
    public async Task<FriendLogEntryResponse[]> FriendLogAsync(string accountId, CancellationToken cancellationToken)
    {
        var friendships = await db.Friendships.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId)
            .Select(f => new { f.Id, f.FriendUserId, f.FriendNumber, f.StartedAt, f.EndedAt, CurrentName = f.Friend.DisplayName })
            .ToListAsync(cancellationToken);
        var friendIds = friendships.Select(f => f.FriendUserId).Distinct().ToArray();
        var nameChanges = await db.UserDisplayNameChanges.AsNoTracking().Where(c => friendIds.Contains(c.UserId)).OrderBy(c => c.ChangedAt).ToListAsync(cancellationToken);
        var trustChanges = await db.UserTrustChanges.AsNoTracking().Where(c => friendIds.Contains(c.UserId)).ToListAsync(cancellationToken);
        var requests = await db.FriendRequestEvents.AsNoTracking().Where(r => r.OwnerUserId == accountId).ToListAsync(cancellationToken);
        var hidden = (await db.FriendLogHiddenEntries.AsNoTracking().Where(h => h.OwnerUserId == accountId).Select(h => h.EntryKey).ToListAsync(cancellationToken)).ToHashSet();

        var changesByUser = nameChanges.ToLookup(c => c.UserId);
        var trustByUser = trustChanges.ToLookup(c => c.UserId);
        string NameAt(string userId, string current, DateTimeOffset at) =>
            changesByUser[userId].FirstOrDefault(c => c.ChangedAt > at)?.PreviousDisplayName ?? current;

        var baseline = FriendLogBaseline(friendships.Select(f => f.StartedAt));

        var entries = new List<FriendLogEntryResponse>();
        foreach (var f in friendships)
        {
            if (f.StartedAt != baseline)
                entries.Add(new FriendLogEntryResponse
                {
                    Key = $"Friend:{f.Id}", Type = "Friend", CreatedAt = f.StartedAt, UserId = f.FriendUserId,
                    DisplayName = NameAt(f.FriendUserId, f.CurrentName, f.StartedAt), FriendNumber = f.FriendNumber,
                });
            if (f.EndedAt is { } ended)
                entries.Add(new FriendLogEntryResponse
                {
                    Key = $"Unfriend:{f.Id}", Type = "Unfriend", CreatedAt = ended, UserId = f.FriendUserId,
                    DisplayName = NameAt(f.FriendUserId, f.CurrentName, ended),
                });
            bool During(DateTimeOffset at) => at >= f.StartedAt && (f.EndedAt is null || at <= f.EndedAt);
            foreach (var c in changesByUser[f.FriendUserId].Where(c => During(c.ChangedAt)))
                entries.Add(new FriendLogEntryResponse
                {
                    Key = $"DisplayName:{c.Id}", Type = "DisplayName", CreatedAt = c.ChangedAt, UserId = f.FriendUserId,
                    DisplayName = c.DisplayName, PreviousDisplayName = c.PreviousDisplayName, FriendNumber = f.FriendNumber,
                });
            foreach (var c in trustByUser[f.FriendUserId].Where(c => During(c.ChangedAt) && !IsVeteranToggle(c.PreviousTrustLevel, c.TrustLevel)))
                entries.Add(new FriendLogEntryResponse
                {
                    Key = $"TrustLevel:{c.Id}", Type = "TrustLevel", CreatedAt = c.ChangedAt, UserId = f.FriendUserId,
                    DisplayName = NameAt(f.FriendUserId, f.CurrentName, c.ChangedAt), TrustLevel = c.TrustLevel, PreviousTrustLevel = c.PreviousTrustLevel, FriendNumber = f.FriendNumber,
                });
        }
        entries.AddRange(requests.Select(r => new FriendLogEntryResponse
        {
            Key = $"{r.Type}:{r.Id}", Type = r.Type, CreatedAt = r.CreatedAt, UserId = r.TargetUserId, DisplayName = r.DisplayName,
        }));

        return entries.Where(e => !hidden.Contains(e.Key)).OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Key, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// The first sync records everyone who was already a friend at one instant. Those are a
    /// starting point, not new friends, and the original logged nothing for them either.
    /// </summary>
    private static DateTimeOffset? FriendLogBaseline(IEnumerable<DateTimeOffset> startedAt)
    {
        var firstGroup = startedAt.GroupBy(t => t).OrderBy(g => g.Key).FirstOrDefault();
        return firstGroup is not null && firstGroup.Count() > 1 ? firstGroup.Key : null;
    }

    private async Task<DateTimeOffset?> FriendLogBaselineAsync(string accountId, CancellationToken cancellationToken) =>
        FriendLogBaseline(await db.Friendships.AsNoTracking().Where(f => f.OwnerUserId == accountId).Select(f => f.StartedAt).ToListAsync(cancellationToken));

    // The original ignores a friend flipping between these two: it is a VRChat display setting.
    private static bool IsVeteranToggle(string previous, string current) =>
        (previous, current) is ("Trusted User", "Veteran User") or ("Veteran User", "Trusted User");

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
