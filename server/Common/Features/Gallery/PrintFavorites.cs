using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Gallery;

/// <summary>
/// The prints an account marked as favorites, which protects them from the automatic deletion of
/// old prints. VRChat has no such mark; it is this server's own.
/// </summary>
public sealed class PrintFavorites(VrcxContext db)
{
    public Task<List<string>> IdsAsync(string accountId, CancellationToken cancellationToken) =>
        db.PrintFavorites.AsNoTracking().Where(f => f.OwnerUserId == accountId).Select(f => f.PrintId).ToListAsync(cancellationToken);

    /// <summary>Marks a print as a favorite. Marking one twice is fine.</summary>
    public async Task AddAsync(string accountId, string printId, CancellationToken cancellationToken)
    {
        if (await db.PrintFavorites.AnyAsync(f => f.OwnerUserId == accountId && f.PrintId == printId, cancellationToken)) return;
        db.PrintFavorites.Add(new PrintFavorite { OwnerUserId = accountId, PrintId = printId, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task RemoveAsync(string accountId, string printId, CancellationToken cancellationToken) =>
        db.PrintFavorites.Where(f => f.OwnerUserId == accountId && f.PrintId == printId).ExecuteDeleteAsync(cancellationToken);
}
