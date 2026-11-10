using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.API.Controller.Accounts;

public sealed class OwnAvatarTag
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(64, MinimumLength = 1)]
    public required string Tag { get; set; }
    /// <summary>A palette colour name (blue, green, ...) or null for the hashed default.</summary>
    [StringLength(32)]
    public string? Color { get; set; }
}

/// <summary>One of the account's own avatars: the stored avatar plus VRCX's tags, time worn and whether it is on.</summary>
public sealed class OwnAvatarResponse
{
    public required AvatarResponse Avatar { get; init; }
    public required OwnAvatarTag[] Tags { get; init; }
    /// <summary>Milliseconds worn, including the open session.</summary>
    public required long TimeSpentMs { get; init; }
    public required bool IsCurrent { get; init; }
}

public sealed class UpdateAvatarRequest
{
    [StringLength(64, MinimumLength = 1)]
    public string? Name { get; set; }
    [StringLength(256)]
    public string? Description { get; set; }
    /// <summary>public or private</summary>
    [RegularExpression("^(public|private)$")]
    public string? ReleaseStatus { get; set; }
}

// The My Avatars page: the account's uploads (pulled from VRChat once and on Refresh, like the
// original), VRCX's own tags on them, time worn, and the edits the original offered.
public sealed partial class AccountsController
{
    private static readonly TimeSpan OwnAvatarsMaxAge = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The account's own avatars. With a live session they are fetched from VRChat (GET avatars?user=me,
    /// every page) and stored when asked to refresh or not fetched in the last 10 minutes; otherwise the
    /// stored ones are returned.
    /// </summary>
    /// <response code="200">The avatars, newest update first</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/avatars")]
    [ProducesResponseType<OwnAvatarResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListOwnAvatars([FromRoute] string accountId, [FromServices] Common.Services.Ingestion.VrchatStore store, [FromServices] Common.Services.Sync.OwnAvatarsFetchLog fetchLog, CancellationToken cancellationToken, [FromQuery] bool refresh = false)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.UserId == accountId, cancellationToken);
        if (account is null) return Problem(AccountError.AccountNotFound);

        // As the original, which loads them once at startup and on Refresh: paging through a large
        // list on every visit runs into VRChat's rate limit (upstream issues 1611 and 1758).
        var stale = !fetchLog.FetchedAt.TryGetValue(accountId, out var fetchedAt) || DateTimeOffset.UtcNow - fetchedAt > OwnAvatarsMaxAge;
        if ((refresh || stale) && _sessions.Get(accountId) is { } client)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var api in await client.GetOwnAvatarsAsync(cancellationToken))
                    await store.UpsertAvatarAsync(_db, api, now, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                fetchLog.FetchedAt[accountId] = now;
            }
            catch (VrchatUnauthorizedException)
            {
                await _sessions.InvalidateAsync(accountId, cancellationToken);
            }
            catch (VrchatApiException ex)
            {
                _logger.LogWarning(ex, "Own avatars of {Account} could not be refreshed; serving stored ones", accountId);
            }
        }

        var avatars = await _db.Avatars.AsNoTracking().Include(a => a.Author).Where(a => a.AuthorId == accountId && !a.IsStub && a.ReleaseStatus != "hidden").OrderByDescending(a => a.UpdatedAt).ToListAsync(cancellationToken);
        var avatarIds = avatars.Select(a => a.Id).ToList();
        var tags = await _db.AvatarTags.AsNoTracking().Where(t => avatarIds.Contains(t.AvatarId)).OrderBy(t => t.Tag).ToListAsync(cancellationToken);
        var usages = await _db.AvatarUsages.AsNoTracking().Where(u => u.OwnerUserId == accountId).ToDictionaryAsync(u => u.AvatarId, u => u.TimeSpent, cancellationToken);
        var nowUtc = DateTimeOffset.UtcNow;
        long TimeSpent(string id)
        {
            var total = usages.GetValueOrDefault(id);
            if (id == account.CurrentAvatarId && account.CurrentAvatarSince is { } since) total += nowUtc - since;
            return (long)total.TotalMilliseconds;
        }

        return Ok(avatars.Select(a => new OwnAvatarResponse
        {
            Avatar = AvatarResponse.From(a),
            Tags = tags.Where(t => t.AvatarId == a.Id).Select(t => new OwnAvatarTag { Tag = t.Tag, Color = t.Color }).ToArray(),
            TimeSpentMs = TimeSpent(a.Id),
            IsCurrent = a.Id == account.CurrentAvatarId,
        }).ToList());
    }

    /// <summary>Wear the avatar (PUT avatars/{id}/select on VRChat).</summary>
    /// <response code="204">Selected</response>
    /// <response code="404">No such account</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/avatars/{avatarId}/select")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SelectAvatar([FromRoute] string accountId, [FromRoute] string avatarId, [FromServices] Common.Services.Ingestion.VrchatStore store, CancellationToken cancellationToken)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.UserId == accountId, cancellationToken);
        if (account is null) return Problem(AccountError.AccountNotFound);
        if (_sessions.Get(accountId) is not { } client) return Problem(AccountError.NoLiveSession);
        try
        {
            var me = await client.SelectAvatarAsync(avatarId, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            await store.UpsertCurrentUserAsync(_db, me, now, cancellationToken);
            await store.RecordAvatarWearAsync(_db, account, me.CurrentAvatar ?? avatarId, now, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return NoContent();
        }
        catch (VrchatUnauthorizedException)
        {
            await _sessions.InvalidateAsync(accountId, cancellationToken);
            return Problem(VrchatError.SessionExpired);
        }
        catch (VrchatApiException ex)
        {
            return VrchatProblem(ex);
        }
    }

    /// <summary>Rename, change the description or the visibility of one of the account's avatars (PUT avatars/{id} on VRChat).</summary>
    /// <response code="200">The avatar as VRChat now has it</response>
    /// <response code="404">No such account</response>
    /// <response code="409">No live session</response>
    [HttpPatch("{accountId}/avatars/{avatarId}")]
    [ProducesResponseType<AvatarResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> UpdateAvatar([FromRoute] string accountId, [FromRoute] string avatarId, [FromBody] UpdateAvatarRequest body, [FromServices] Common.Services.Ingestion.VrchatStore store, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        if (_sessions.Get(accountId) is not { } client) return Problem(AccountError.NoLiveSession);
        var changes = new Dictionary<string, string>();
        if (body.Name is not null) changes["name"] = body.Name;
        if (body.Description is not null) changes["description"] = body.Description;
        if (body.ReleaseStatus is not null) changes["releaseStatus"] = body.ReleaseStatus;
        try
        {
            var api = await client.SaveAvatarAsync(avatarId, changes, cancellationToken);
            var avatar = await store.UpsertAvatarAsync(_db, api, DateTimeOffset.UtcNow, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return Ok(AvatarResponse.From(avatar));
        }
        catch (VrchatUnauthorizedException)
        {
            await _sessions.InvalidateAsync(accountId, cancellationToken);
            return Problem(VrchatError.SessionExpired);
        }
        catch (VrchatApiException ex)
        {
            return VrchatProblem(ex);
        }
    }

    /// <summary>Ask VRChat to build the avatar's impostor.</summary>
    /// <response code="202">Queued</response>
    /// <response code="404">No such account</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/avatars/{avatarId}/impostor")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> CreateImpostor([FromRoute] string accountId, [FromRoute] string avatarId, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        if (_sessions.Get(accountId) is not { } client) return Problem(AccountError.NoLiveSession);
        try
        {
            await client.EnqueueImpostorAsync(avatarId, cancellationToken);
            return Accepted();
        }
        catch (VrchatUnauthorizedException)
        {
            await _sessions.InvalidateAsync(accountId, cancellationToken);
            return Problem(VrchatError.SessionExpired);
        }
        catch (VrchatApiException ex)
        {
            return VrchatProblem(ex);
        }
    }

    /// <summary>Replace VRCX's tags on an avatar.</summary>
    /// <response code="200">The stored tags</response>
    /// <response code="404">No such account or avatar</response>
    [HttpPut("{accountId}/avatars/{avatarId}/tags")]
    [ProducesResponseType<OwnAvatarTag[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> PutAvatarTags([FromRoute] string accountId, [FromRoute] string avatarId, [FromBody] OwnAvatarTag[] body, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        if (!await _db.Avatars.AnyAsync(a => a.Id == avatarId, cancellationToken)) return Problem(AvatarError.AvatarNotFound);

        var wanted = body.GroupBy(t => t.Tag.Trim()).Where(g => g.Key.Length > 0).ToDictionary(g => g.Key, g => g.First().Color is { Length: > 0 } c ? c : null);
        // Tags are per avatar, as in the original's avatar_tags table (one install, one set of labels).
        var existing = await _db.AvatarTags.Where(t => t.AvatarId == avatarId).ToListAsync(cancellationToken);
        foreach (var row in existing)
        {
            if (!wanted.TryGetValue(row.Tag, out var color)) _db.AvatarTags.Remove(row);
            else row.Color = color;
        }
        foreach (var (tag, color) in wanted)
        {
            if (existing.All(e => e.Tag != tag))
                _db.AvatarTags.Add(new AvatarTag { AvatarId = avatarId, Tag = tag, Color = color });
        }
        await _db.SaveChangesAsync(cancellationToken);
        var tags = await _db.AvatarTags.AsNoTracking().Where(t => t.AvatarId == avatarId).OrderBy(t => t.Tag)
            .Select(t => new OwnAvatarTag { Tag = t.Tag, Color = t.Color }).ToListAsync(cancellationToken);
        return Ok(tags);
    }
}
