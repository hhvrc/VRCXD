using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Users;

public sealed class UserHistoryResponse
{
    public required IReadOnlyList<UserDisplayNameChange> DisplayNames { get; init; }
    public required IReadOnlyList<UserTrustChange> Trust { get; init; }
    public required IReadOnlyList<UserStatusChange> Status { get; init; }
    public required IReadOnlyList<UserBioChange> Bio { get; init; }
    public required IReadOnlyList<UserAvatarChange> Avatars { get; init; }
    public required IReadOnlyList<PresenceEvent> Presence { get; init; }
    public required IReadOnlyList<VisitResponse> Visits { get; init; }
}

public sealed class VisitResponse
{
    public required string InstanceLocation { get; init; }
    public required string WorldName { get; init; }
    public required DateTimeOffset JoinedAt { get; init; }
    public DateTimeOffset? LeftAt { get; init; }
    public required VisitSource Source { get; init; }
}

/// <summary>A stored user that matched a search, with what it matched on.</summary>
public sealed record UserCandidate(string UserId, string DisplayName, bool IsFriend, string MatchedOn, DateTimeOffset LastUpdatedAt);

/// <summary>The best matches of a search, and whether there were more than asked for.</summary>
public sealed record UserSearchResult(IReadOnlyList<UserCandidate> Candidates, bool Truncated);

/// <summary>
/// A user as stored, as one account sees them: the memo on them, the account's VRChat note, the
/// open friendship if there is one, and the moderations the account applied.
/// </summary>
public sealed record StoredUser(User User, string? Memo, string? Note, bool IsFriend, int? FriendNumber, DateTimeOffset? FriendsSince, IReadOnlyList<string> Moderations);

/// <summary>What the server recorded about users, shared by the Users endpoints and the MCP tools.</summary>
public sealed class UserQueries(VrcxContext db)
{
    /// <summary>
    /// Everything that changed about a user while we were watching: names, trust, status, bio,
    /// avatars, presence, instance visits. Each list newest first, at most <paramref name="cap"/>
    /// entries, and only entries before <paramref name="before"/> when it is given.
    /// </summary>
    public async Task<UserHistoryResponse> HistoryAsync(string userId, int cap, DateTimeOffset? before, CancellationToken cancellationToken)
    {
        return new UserHistoryResponse
        {
            DisplayNames = await db.UserDisplayNameChanges.AsNoTracking().Where(c => c.UserId == userId && (before == null || c.ChangedAt < before)).OrderByDescending(c => c.ChangedAt).Take(cap).ToListAsync(cancellationToken),
            Trust = await db.UserTrustChanges.AsNoTracking().Where(c => c.UserId == userId && (before == null || c.ChangedAt < before)).OrderByDescending(c => c.ChangedAt).Take(cap).ToListAsync(cancellationToken),
            Status = await db.UserStatusChanges.AsNoTracking().Where(c => c.UserId == userId && (before == null || c.ChangedAt < before)).OrderByDescending(c => c.ChangedAt).Take(cap).ToListAsync(cancellationToken),
            Bio = await db.UserBioChanges.AsNoTracking().Where(c => c.UserId == userId && (before == null || c.ChangedAt < before)).OrderByDescending(c => c.ChangedAt).Take(cap).ToListAsync(cancellationToken),
            Avatars = await db.UserAvatarChanges.AsNoTracking().Where(c => c.UserId == userId && (before == null || c.ChangedAt < before)).OrderByDescending(c => c.ChangedAt).Take(cap).ToListAsync(cancellationToken),
            Presence = await db.PresenceEvents.AsNoTracking().Where(c => c.UserId == userId && (before == null || c.At < before)).OrderByDescending(c => c.At).Take(cap).ToListAsync(cancellationToken),
            Visits = await db.InstanceVisits.AsNoTracking().Where(v => v.UserId == userId && (before == null || v.JoinedAt < before)).OrderByDescending(v => v.JoinedAt).Take(cap)
                .Select(v => new VisitResponse { InstanceLocation = v.InstanceLocation, WorldName = v.Instance.World.Name, JoinedAt = v.JoinedAt, LeftAt = v.LeftAt, Source = v.Source })
                .ToListAsync(cancellationToken),
        };
    }

    /// <summary>The stored user, or null when the server has never seen them: GET users/{id}.</summary>
    public Task<User?> FindUserAsync(string userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    /// <summary>Whether the server has ever stored this user.</summary>
    public Task<bool> ExistsAsync(string userId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id == userId, cancellationToken);

    /// <summary>The stored user's display name, or null when the server has never seen them.</summary>
    public Task<string?> DisplayNameAsync(string userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => (string?)u.DisplayName).FirstOrDefaultAsync(cancellationToken);

    /// <summary>The stored user as the account sees them, or null when the server has never seen them.</summary>
    public async Task<StoredUser?> StoredAsync(string accountId, string userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return null;
        var memo = await db.UserMemos.AsNoTracking().Where(m => m.UserId == userId).Select(m => m.Memo).FirstOrDefaultAsync(cancellationToken);
        var note = await db.UserNotes.AsNoTracking().Where(n => n.OwnerUserId == accountId && n.TargetUserId == userId).Select(n => n.Note).FirstOrDefaultAsync(cancellationToken);
        var friendship = await db.Friendships.AsNoTracking().Where(f => f.OwnerUserId == accountId && f.FriendUserId == userId && f.EndedAt == null)
            .Select(f => new { f.FriendNumber, f.StartedAt }).FirstOrDefaultAsync(cancellationToken);
        var moderations = await db.PlayerModerations.AsNoTracking().Where(m => m.OwnerUserId == accountId && m.TargetUserId == userId).Select(m => m.Type).ToListAsync(cancellationToken);
        return new StoredUser(user, memo, note, friendship is not null, friendship?.FriendNumber, friendship?.StartedAt, moderations);
    }

    /// <summary>
    /// Ranked candidates for a name among the stored users, the account's friends first. Matches the
    /// display name (exact, then starting with, then containing, ignoring case), for friends also
    /// the memo and the account's VRChat note on them, and names a user had before. A usr_ id
    /// matches itself.
    /// </summary>
    public async Task<UserSearchResult> FindAsync(string accountId, string text, int limit, CancellationToken cancellationToken)
    {
        var like = $"%{EscapeLike(text)}%";
        var friendIds = db.Friendships.Where(f => f.OwnerUserId == accountId && f.EndedAt == null).Select(f => f.FriendUserId);

        var friendRows = await db.Friendships.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.EndedAt == null)
            .Select(f => new
            {
                f.Friend.Id, f.Friend.DisplayName, f.Friend.UpdatedAt,
                Memo = db.UserMemos.Where(m => m.UserId == f.FriendUserId).Select(m => m.Memo).FirstOrDefault(),
                Note = db.UserNotes.Where(n => n.OwnerUserId == accountId && n.TargetUserId == f.FriendUserId).Select(n => n.Note).FirstOrDefault(),
            })
            .Where(f => f.Id == text || EF.Functions.ILike(f.DisplayName, like) || (f.Memo != null && EF.Functions.ILike(f.Memo, like)) || (f.Note != null && EF.Functions.ILike(f.Note, like)))
            .ToListAsync(cancellationToken);
        var others = await db.Users.AsNoTracking()
            .Where(u => !friendIds.Contains(u.Id) && (u.Id == text || EF.Functions.ILike(u.DisplayName, like)))
            .OrderByDescending(u => u.UpdatedAt).Take(500)
            .Select(u => new { u.Id, u.DisplayName, u.UpdatedAt })
            .ToListAsync(cancellationToken);
        var renamed = await db.UserDisplayNameChanges.AsNoTracking()
            .Where(c => EF.Functions.ILike(c.PreviousDisplayName, like))
            .OrderByDescending(c => c.ChangedAt).Take(500)
            .Select(c => new { c.UserId, c.User.DisplayName, c.User.UpdatedAt, c.PreviousDisplayName, IsFriend = friendIds.Contains(c.UserId) })
            .ToListAsync(cancellationToken);

        var candidates = new List<(UserCandidate Candidate, int Rank)>();
        foreach (var f in friendRows)
        {
            var (rank, on) = NameRank(f.Id, f.DisplayName, text) is { } r ? r
                : f.Memo is not null && f.Memo.Contains(text, StringComparison.OrdinalIgnoreCase) ? (3, "memo") : (4, "note");
            candidates.Add((new UserCandidate(f.Id, f.DisplayName, true, on, f.UpdatedAt), rank));
        }
        foreach (var u in others)
            if (NameRank(u.Id, u.DisplayName, text) is { } r) candidates.Add((new UserCandidate(u.Id, u.DisplayName, false, r.On, u.UpdatedAt), 10 + r.Rank));
        foreach (var c in renamed.DistinctBy(c => c.UserId))
            if (candidates.All(x => x.Candidate.UserId != c.UserId))
                candidates.Add((new UserCandidate(c.UserId, c.DisplayName, c.IsFriend, $"previousName:{c.PreviousDisplayName}", c.UpdatedAt), c.IsFriend ? 5 : 15));

        var ranked = candidates.OrderBy(c => c.Rank).ThenByDescending(c => c.Candidate.LastUpdatedAt).Select(c => c.Candidate).ToList();
        return new UserSearchResult(ranked.Take(limit).ToList(), ranked.Count > limit);
    }

    private static (int Rank, string On)? NameRank(string id, string name, string text) =>
        id == text ? (0, "id")
        : string.Equals(name, text, StringComparison.OrdinalIgnoreCase) ? (0, "name")
        : name.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? (1, "namePrefix")
        : name.Contains(text, StringComparison.OrdinalIgnoreCase) ? (2, "nameContains")
        : null;

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>
    /// The id of the stored user with exactly this display name: a friend of the account first, else
    /// the most recently updated user stored in full. Null when the server knows nobody by that name.
    /// </summary>
    public async Task<string?> IdByNameAsync(string accountId, string displayName, CancellationToken cancellationToken)
    {
        var friend = await db.Friendships.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.EndedAt == null && f.Friend.DisplayName == displayName)
            .Select(f => f.FriendUserId).FirstOrDefaultAsync(cancellationToken);
        if (friend is not null) return friend;
        return await db.Users.AsNoTracking()
            .Where(u => u.DisplayName == displayName && !u.IsStub)
            .OrderByDescending(u => u.UpdatedAt).Select(u => u.Id).FirstOrDefaultAsync(cancellationToken);
    }
}
