using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.Common.Features.Locations;

/// <summary>The stored names behind a set of location tags: world names by world id, group names by group id.</summary>
public sealed record LocationNames(IReadOnlyDictionary<string, string> Worlds, IReadOnlyDictionary<string, string> Groups);

/// <summary>
/// What is stored about the places location tags point at. Read only; the MCP tools use it to put
/// names beside the tags they return.
/// </summary>
public sealed class LocationQueries(VrcxContext db)
{
    /// <summary>
    /// World and group names for location tags, from the stored worlds and groups: one query for the
    /// worlds and one for the groups however many tags there are, and none for a kind no tag has.
    /// </summary>
    public async Task<LocationNames> NamesAsync(IEnumerable<string?> tags, CancellationToken cancellationToken)
    {
        var parsed = tags.OfType<string>().Distinct().Select(VrchatLocation.Parse).ToList();
        var worldIds = parsed.Select(l => l.WorldId).Where(id => id.Length > 0).Distinct().ToList();
        var groupIds = parsed.Select(l => l.GroupId).OfType<string>().Distinct().ToList();
        var worlds = worldIds.Count == 0 ? new Dictionary<string, string>() : await db.Worlds.AsNoTracking().Where(w => worldIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken);
        var groups = groupIds.Count == 0 ? new Dictionary<string, string>() : await db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken);
        return new LocationNames(worlds, groups);
    }
}
