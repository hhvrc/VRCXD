using VRCX.Common.Services.Ingestion;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat.Models;

namespace VRCX.Common.Features.Profile;

/// <summary>
/// The stored copy of an account's own profile, kept in step with what the profile endpoints just
/// read from or changed on VRChat. Those endpoints forward to VRChat (API/Controller/Profile); this
/// is the part of them that is this server's own.
/// </summary>
public sealed class ProfileStore(VrcxContext db, VrchatStore store)
{
    /// <summary>Stores the account as VRChat just returned it (auth/user).</summary>
    public async Task StoreAsync(CurrentUser me, CancellationToken cancellationToken)
    {
        await store.UpsertCurrentUserAsync(db, me, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>VRChat changed one of the account's badges (hidden or showcased): the stored profile follows.</summary>
    public async Task BadgeChangedAsync(string accountId, string badgeId, Badge badge, CancellationToken cancellationToken)
    {
        if (await db.Users.FindAsync([accountId], cancellationToken) is not { } me) return;
        me.Badges = me.Badges.Select(b => b.BadgeId == badgeId ? b with { Hidden = badge.Hidden, Showcased = badge.Showcased, UpdatedAt = badge.UpdatedAt ?? b.UpdatedAt } : b).ToList();
        await db.SaveChangesAsync(cancellationToken);
    }
}
