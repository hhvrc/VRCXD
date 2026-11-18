using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Worlds;

public sealed class WorldStatsResponse
{
    public required int VisitCount { get; init; }
    public DateTimeOffset? LastVisit { get; init; }
    public required long TimeSpentMs { get; init; }
}

/// <summary>
/// What this server keeps about an account and VRChat's worlds: its own history in a world, and
/// its home. The world endpoints mostly forward to VRChat (API/Controller/Worlds); this is the
/// part of them that is this server's own.
/// </summary>
public sealed class AccountWorlds(VrcxContext db)
{
    /// <summary>This account's own history in a world, from its stored visits.</summary>
    public async Task<WorldStatsResponse> StatsAsync(string accountId, string worldId, CancellationToken cancellationToken)
    {
        var visits = await db.InstanceVisits.AsNoTracking()
            .Where(v => v.UserId == accountId && v.Instance.WorldId == worldId)
            .Select(v => new { v.JoinedAt, v.LeftAt })
            .ToListAsync(cancellationToken);
        return new WorldStatsResponse
        {
            VisitCount = visits.Count,
            LastVisit = visits.Count == 0 ? null : visits.Max(v => v.JoinedAt),
            TimeSpentMs = visits.Where(v => v.LeftAt is not null).Sum(v => (long)(v.LeftAt!.Value - v.JoinedAt).TotalMilliseconds)
        };
    }

    /// <summary>The home world VRChat just accepted for the account (empty when cleared), stored at once rather than at the next sync.</summary>
    public Task HomeChangedAsync(string accountId, string worldId, CancellationToken cancellationToken) =>
        db.Accounts.Where(a => a.UserId == accountId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.HomeLocation, worldId), cancellationToken);
}
