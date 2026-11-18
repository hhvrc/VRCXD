using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Instances;

/// <summary>
/// What is stored that the instance endpoints show beside VRChat's live answer. Read only. The
/// instance endpoints mostly forward to VRChat (API/Controller/Instances).
/// </summary>
public sealed class InstanceQueries(VrcxContext db)
{
    /// <summary>When the account's game log last saw it join this instance in the past week, or null.</summary>
    public Task<DateTimeOffset?> LastJoinedAsync(string accountId, string location, CancellationToken cancellationToken)
    {
        var weekAgo = DateTimeOffset.UtcNow.AddDays(-7);
        return db.InstanceVisits.AsNoTracking()
            .Where(v => v.UserId == accountId && v.Source == VisitSource.GameLog && v.InstanceLocation == location && v.JoinedAt > weekAgo)
            .MaxAsync(v => (DateTimeOffset?)v.JoinedAt, cancellationToken);
    }

    /// <summary>The stored banner of a group, which an instance announcement shows when it is given no picture.</summary>
    public Task<string?> GroupBannerAsync(string groupId, CancellationToken cancellationToken) =>
        db.Groups.AsNoTracking().Where(g => g.Id == groupId).Select(g => g.BannerUrl).FirstOrDefaultAsync(cancellationToken);
}
