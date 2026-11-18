using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services.Queries;

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
}
