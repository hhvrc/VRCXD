using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Groups;

/// <summary>Storing groups the endpoints come across on VRChat, so they are fetched once.</summary>
public sealed class GroupStore(VrcxContext db, VrchatStore store)
{
    /// <summary>
    /// The stored groups with these ids. One this server has never seen is read through
    /// <paramref name="fetch"/> once and kept, as the original does; one VRChat does not have is left out.
    /// </summary>
    public async Task<Dictionary<string, Group>> KnownOrFetchAsync(
        IReadOnlyCollection<string> groupIds, Func<string, Task<VRCX.Vrchat.Models.Group?>> fetch, CancellationToken cancellationToken)
    {
        var groups = await db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var missing in groupIds.Where(id => !groups.ContainsKey(id)))
        {
            if (await fetch(missing) is not { } api) continue;
            groups[missing] = await store.UpsertGroupAsync(db, api, now, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        return groups;
    }
}
