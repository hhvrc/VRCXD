using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.GameLog;

/// <summary>
/// What an account does to its game log through VRCXD. The rows themselves are written by the game
/// log ingest (Services/Live/GameLogIngestor), which has not moved into this folder yet.
/// </summary>
public sealed class GameLogActions(VrcxContext db)
{
    /// <summary>Removes a row from the account's game log view. The row itself stays in the database.</summary>
    public async Task HideEntryAsync(string accountId, string key, CancellationToken ct)
    {
        if (await db.GameLogHiddenEntries.AnyAsync(h => h.OwnerUserId == accountId && h.EntryKey == key, ct)) return;
        db.GameLogHiddenEntries.Add(new GameLogHiddenEntry { OwnerUserId = accountId, EntryKey = key, HiddenAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
    }
}
