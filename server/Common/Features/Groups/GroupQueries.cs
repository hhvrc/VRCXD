using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Groups;

/// <summary>How often an account was in a group's instances, and when last.</summary>
public sealed record GroupVisits(int JoinCount, DateTimeOffset? LastVisit);

/// <summary>A stored group's name and banner.</summary>
public sealed record GroupCard(string Name, string? BannerUrl);

/// <summary>
/// What is stored about an account and VRChat's groups. Read only. The group endpoints mostly
/// forward to VRChat (API/Controller/Groups); this is the part of them that is this server's own.
/// </summary>
public sealed class GroupQueries(VrcxContext db)
{
    /// <summary>The account's history with a group, from its own instance visits: the group pop-up's join count and last visit.</summary>
    public async Task<GroupVisits> VisitsAsync(string accountId, string groupId, CancellationToken cancellationToken)
    {
        var groupTag = $"~group({groupId})";
        var visits = db.InstanceVisits.AsNoTracking().Where(v => v.UserId == accountId && v.InstanceLocation.Contains(groupTag));
        var joinCount = await visits.CountAsync(cancellationToken);
        var lastVisit = joinCount == 0 ? null : await visits.MaxAsync(v => (DateTimeOffset?)v.JoinedAt, cancellationToken);
        return new GroupVisits(joinCount, lastVisit);
    }

    /// <summary>The name and banner of each of these groups that is stored in full (not just its id).</summary>
    public Task<Dictionary<string, GroupCard>> CardsAsync(IReadOnlyCollection<string> groupIds, CancellationToken cancellationToken) =>
        db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id) && !g.IsStub).ToDictionaryAsync(g => g.Id, g => new GroupCard(g.Name, g.BannerUrl), cancellationToken);
}
