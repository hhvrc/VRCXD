using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using VRCX.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.Common.Features.Avatars;

public sealed class OwnAvatarTag
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(64, MinimumLength = 1)]
    public required string Tag { get; set; }
    /// <summary>A palette colour name (blue, green, ...) or null for the hashed default.</summary>
    [StringLength(32)]
    public string? Color { get; set; }
}

/// <summary>One of the account's own avatars as stored, with VRCX's tags, the time worn (including the open session) and whether it is on.</summary>
public sealed record OwnAvatar(Avatar Avatar, OwnAvatarTag[] Tags, long TimeSpentMs, bool IsCurrent);

/// <summary>An account's own standing with an avatar: whether it wears it now, and for how long it has in all.</summary>
public sealed record AvatarStanding(bool IsCurrent, TimeSpan TimeSpent);

public sealed class AvatarLookupResponse
{
    public string? AvatarId { get; init; }
    public string? OwnerId { get; init; }
    public string? AvatarName { get; init; }
}

/// <summary>
/// The My Avatars page and the avatar pop-up's own half: the account's uploads (pulled from VRChat
/// once and on Refresh, like the original), VRCX's own tags on them, time worn, and the edits the
/// original offered. What only forwards to VRChat is in API/Controller/Avatars.
/// </summary>
public sealed class OwnAvatars(
    VrcxContext db, AccountSessionRunner runner, VrchatStore store, OwnAvatarsFetchLog fetchLog, IAvatarImageResolver resolver, ILogger<OwnAvatars> logger)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The account's own avatars, newest update first. With a live session they are fetched from
    /// VRChat (GET avatars?user=me, every page) and stored when asked to refresh or not fetched in
    /// the last 10 minutes; otherwise the stored ones are returned. Null when there is no such account.
    /// </summary>
    public async Task<List<OwnAvatar>?> ListAsync(string accountId, bool refresh, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.UserId == accountId, cancellationToken);
        if (account is null) return null;

        // As the original, which loads them once at startup and on Refresh: paging through a large
        // list on every visit runs into VRChat's rate limit (upstream issues 1611 and 1758).
        var stale = !fetchLog.FetchedAt.TryGetValue(accountId, out var fetchedAt) || DateTimeOffset.UtcNow - fetchedAt > MaxAge;
        if (refresh || stale)
        {
            // Without a session, or with one VRChat no longer accepts, the stored ones are served.
            await runner.RunAsync(accountId, async client =>
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    foreach (var api in await client.GetOwnAvatarsAsync(cancellationToken))
                        await store.UpsertAvatarAsync(db, api, now, cancellationToken);
                    await db.SaveChangesAsync(cancellationToken);
                    fetchLog.FetchedAt[accountId] = now;
                }
                catch (VrchatApiException ex) when (ex is not VrchatUnauthorizedException)
                {
                    logger.LogWarning(ex, "Own avatars of {Account} could not be refreshed; serving stored ones", accountId);
                }
            }, cancellationToken);
        }

        var avatars = await db.Avatars.AsNoTracking().Include(a => a.Author).Where(a => a.AuthorId == accountId && !a.IsStub && a.ReleaseStatus != "hidden").OrderByDescending(a => a.UpdatedAt).ToListAsync(cancellationToken);
        var avatarIds = avatars.Select(a => a.Id).ToList();
        var tags = await db.AvatarTags.AsNoTracking().Where(t => avatarIds.Contains(t.AvatarId)).OrderBy(t => t.Tag).ToListAsync(cancellationToken);
        var usages = await db.AvatarUsages.AsNoTracking().Where(u => u.OwnerUserId == accountId).ToDictionaryAsync(u => u.AvatarId, u => u.TimeSpent, cancellationToken);
        var nowUtc = DateTimeOffset.UtcNow;
        long TimeSpent(string id)
        {
            var total = usages.GetValueOrDefault(id);
            if (id == account.CurrentAvatarId && account.CurrentAvatarSince is { } since) total += nowUtc - since;
            return (long)total.TotalMilliseconds;
        }

        return avatars.Select(a => new OwnAvatar(
            a,
            tags.Where(t => t.AvatarId == a.Id).Select(t => new OwnAvatarTag { Tag = t.Tag, Color = t.Color }).ToArray(),
            TimeSpent(a.Id),
            a.Id == account.CurrentAvatarId)).ToList();
    }

    /// <summary>The account's standing with an avatar, from its stored wear time. Null when there is no such account.</summary>
    public async Task<AvatarStanding?> StandingAsync(string accountId, string avatarId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == accountId, cancellationToken);
        if (account is null) return null;
        var usage = await db.AvatarUsages.AsNoTracking()
            .Where(u => u.OwnerUserId == accountId && u.AvatarId == avatarId)
            .Select(u => u.TimeSpent)
            .FirstOrDefaultAsync(cancellationToken);
        var isCurrent = account.CurrentAvatarId == avatarId;
        if (isCurrent && account.CurrentAvatarSince is { } since) usage += DateTimeOffset.UtcNow - since;
        return new AvatarStanding(isCurrent, usage);
    }

    /// <summary>Wears the avatar (PUT avatars/{id}/select on VRChat) and records the wear.</summary>
    public async Task<VrcxProblem?> SelectAsync(string accountId, string avatarId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.UserId == accountId, cancellationToken);
        if (account is null) return AccountError.AccountNotFound;
        return await runner.RunAsync(accountId, async client =>
        {
            var me = await client.SelectAvatarAsync(avatarId, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            await store.UpsertCurrentUserAsync(db, me, now, cancellationToken);
            await store.RecordAvatarWearAsync(db, account, me.CurrentAvatar ?? avatarId, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// Renames, changes the description or the visibility of one of the account's avatars (PUT
    /// avatars/{id} on VRChat) and stores it as VRChat now has it.
    /// </summary>
    public async Task<Result<Avatar>> UpdateAsync(string accountId, string avatarId, string? name, string? description, string? releaseStatus, CancellationToken cancellationToken)
    {
        if (!await db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return AccountError.AccountNotFound;
        var changes = new Dictionary<string, string>();
        if (name is not null) changes["name"] = name;
        if (description is not null) changes["description"] = description;
        if (releaseStatus is not null) changes["releaseStatus"] = releaseStatus;
        return await runner.ReadAsync(accountId, async client =>
        {
            var api = await client.SaveAvatarAsync(avatarId, changes, cancellationToken);
            var avatar = await store.UpsertAvatarAsync(db, api, DateTimeOffset.UtcNow, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return avatar;
        }, cancellationToken);
    }

    /// <summary>Asks VRChat to build the avatar's impostor.</summary>
    public async Task<VrcxProblem?> CreateImpostorAsync(string accountId, string avatarId, CancellationToken cancellationToken)
    {
        if (!await db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return AccountError.AccountNotFound;
        return await runner.RunAsync(accountId, client => client.EnqueueImpostorAsync(avatarId, cancellationToken), cancellationToken);
    }

    /// <summary>An avatar the account deleted on VRChat is hidden here at once, so My Avatars stops listing it.</summary>
    public Task MarkDeletedAsync(string avatarId, CancellationToken cancellationToken) =>
        db.Avatars.Where(a => a.Id == avatarId).ExecuteUpdateAsync(set => set.SetProperty(a => a.ReleaseStatus, "hidden"), cancellationToken);

    /// <summary>Replaces VRCX's tags on an avatar and answers the stored ones.</summary>
    public async Task<Result<List<OwnAvatarTag>>> PutTagsAsync(string accountId, string avatarId, OwnAvatarTag[] body, CancellationToken cancellationToken)
    {
        if (!await db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return AccountError.AccountNotFound;
        if (!await db.Avatars.AnyAsync(a => a.Id == avatarId, cancellationToken)) return AvatarError.AvatarNotFound;

        var wanted = body.GroupBy(t => t.Tag.Trim()).Where(g => g.Key.Length > 0).ToDictionary(g => g.Key, g => g.First().Color is { Length: > 0 } c ? c : null);
        // Tags are per avatar, as in the original's avatar_tags table (one install, one set of labels).
        var existing = await db.AvatarTags.Where(t => t.AvatarId == avatarId).ToListAsync(cancellationToken);
        foreach (var row in existing)
        {
            if (!wanted.TryGetValue(row.Tag, out var color)) db.AvatarTags.Remove(row);
            else row.Color = color;
        }
        foreach (var (tag, color) in wanted)
        {
            if (existing.All(e => e.Tag != tag))
                db.AvatarTags.Add(new AvatarTag { AvatarId = avatarId, Tag = tag, Color = color });
        }
        await db.SaveChangesAsync(cancellationToken);
        var tags = await db.AvatarTags.AsNoTracking().Where(t => t.AvatarId == avatarId).OrderBy(t => t.Tag)
            .Select(t => new OwnAvatarTag { Tag = t.Tag, Color = t.Color }).ToListAsync(cancellationToken);
        return tags;
    }

    /// <summary>
    /// Which avatar a wearer's picture shows, the original's showAvatarAuthorDialog: their own
    /// current avatar, one the server has seen with that image, and otherwise only who owns it.
    /// Either field can be missing. Null when there is no such account.
    /// </summary>
    public async Task<AvatarLookupResponse?> LookupAsync(string accountId, string imageUrl, string? userId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == accountId, cancellationToken);
        if (account is null) return null;
        var info = await resolver.ResolveAsync(imageUrl, cancellationToken);
        var avatarId = userId == accountId ? account.CurrentAvatarId : null;
        if (avatarId is null && VrchatApiClient.ExtractFileId(imageUrl) is { } fileId)
        {
            avatarId = await db.Avatars.AsNoTracking()
                .Where(a => a.ImageUrl!.Contains(fileId) || a.ThumbnailImageUrl!.Contains(fileId))
                .Select(a => a.Id)
                .FirstOrDefaultAsync(cancellationToken);
            // The original's refreshUserDialogAvatars(fileId): one of the account's own uploads is
            // looked for in its avatars read again from VRChat.
            if (avatarId is null && info?.OwnerId == accountId)
            {
                await runner.RunAsync(accountId, async client =>
                {
                    try
                    {
                        var now = DateTimeOffset.UtcNow;
                        foreach (var api in await client.GetOwnAvatarsAsync(cancellationToken))
                        {
                            await store.UpsertAvatarAsync(db, api, now, cancellationToken);
                            if (avatarId is null && (api.ImageUrl.Contains(fileId) || api.ThumbnailImageUrl.Contains(fileId))) avatarId = api.Id;
                        }
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    catch (VrchatApiException ex) when (ex is not VrchatUnauthorizedException)
                    {
                        logger.LogWarning(ex, "Own avatars of {Account} could not be read for an avatar lookup", accountId);
                    }
                }, cancellationToken);
            }
        }
        return new AvatarLookupResponse { AvatarId = avatarId, OwnerId = info?.OwnerId, AvatarName = info?.AvatarName };
    }

    /// <summary>Stores avatars VRChat just listed (the ones the account bought), and answers them as stored.</summary>
    public async Task<List<Avatar>> StoreAsync(IEnumerable<VRCX.Vrchat.Models.Avatar> listed, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var stored = new List<Avatar>();
        foreach (var api in listed) stored.Add(await store.UpsertAvatarAsync(db, api, now, cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        return stored;
    }
}
