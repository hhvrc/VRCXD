using System.Text.Json;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;

namespace VRCX.Common.Features.Search;

/// <summary>
/// What the Search page keeps on this server. Its tabs ask VRChat through the account's session
/// and pass the answer on (API/Controller/Search); users found are stored, as everywhere else, and
/// the avatar databases are told which install is asking.
/// </summary>
public sealed class SearchStore(VrcxContext db, VrchatStore store)
{
    private const string VrcxIdKey = "VRCX_id";

    /// <summary>This server's install id, made once: the original identifies itself to avatar databases with one.</summary>
    public async Task<string> VrcxIdAsync(CancellationToken ct)
    {
        var row = await db.ServerSettings.FindAsync([VrcxIdKey], ct);
        if (row is not null) return row.Value;
        row = new ServerSetting { Key = VrcxIdKey, Value = Guid.NewGuid().ToString() };
        db.ServerSettings.Add(row);
        await db.SaveChangesAsync(ct);
        return row.Value;
    }

    /// <summary>Stores the users of a VRChat user search (GET users), so a result can be opened without another fetch.</summary>
    public async Task StoreUsersAsync(JsonElement users, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var user in users.EnumerateArray())
            if (user.Deserialize<LimitedUserSearch>(VrchatJson.Options) is { } found) await store.UpsertSearchResultAsync(db, found, now, ct);
        await db.SaveChangesAsync(ct);
    }
}
