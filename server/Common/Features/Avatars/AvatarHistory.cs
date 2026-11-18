using Microsoft.EntityFrameworkCore;
using VRCX.Common.Features.Accounts;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.Common.Features.Avatars;

public sealed record AvatarHistoryEntry(string Id, string Name, string? AuthorId, string? AuthorName, string? ThumbnailImageUrl, DateTimeOffset LastUsedAt, bool IsCurrent);

public sealed record AvatarExistsResponse(bool Exists);

/// <summary>
/// The Favorites page's local avatar history: the last 100 avatars worn that someone else made, and
/// the check that finds local favorites VRChat no longer serves. The rest of what the avatar
/// endpoints do has not moved into this folder yet.
/// </summary>
public sealed class AvatarHistory(VrcxContext db, AccountSessionRunner runner)
{
    /// <summary>
    /// Avatars the account wore lately that it did not make, newest first, at most 100. Null when no
    /// such account is logged into this server.
    /// </summary>
    public async Task<List<AvatarHistoryEntry>?> ListAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == accountId, cancellationToken);
        if (account is null) return null;
        var after = account.AvatarHistoryClearedAt ?? DateTimeOffset.MinValue;
        return await db.AvatarUsages.AsNoTracking()
            .Where(u => u.OwnerUserId == accountId && u.LastUsedAt > after && u.Avatar.AuthorId != accountId)
            .OrderByDescending(u => u.LastUsedAt)
            .Take(100)
            .Select(u => new AvatarHistoryEntry(u.AvatarId, u.Avatar.Name, u.Avatar.AuthorId, u.Avatar.AuthorName, u.Avatar.ThumbnailImageUrl ?? u.Avatar.ImageUrl, u.LastUsedAt, u.AvatarId == account.CurrentAvatarId))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Empties the avatar history. The usage rows and time worn are kept. False when there is no such account.</summary>
    public async Task<bool> ClearAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.UserId == accountId, cancellationToken);
        if (account is null) return false;
        account.AvatarHistoryClearedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Whether VRChat still serves an avatar: any refusal counts as gone, as the original's check does.</summary>
    public Task<LiveRead<AvatarExistsResponse>> ExistsAsync(string accountId, string avatarId, CancellationToken cancellationToken) =>
        runner.ReadAsync(accountId, async client =>
        {
            try
            {
                return new AvatarExistsResponse(await client.GetAvatarAsync(avatarId, cancellationToken) is not null);
            }
            catch (VrchatApiException ex) when (ex is not VrchatUnauthorizedException)
            {
                return new AvatarExistsResponse(false);
            }
        }, cancellationToken);
}
