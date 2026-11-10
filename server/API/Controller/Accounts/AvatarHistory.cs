using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Vrchat;

namespace VRCX.API.Controller.Accounts;

public sealed record AvatarHistoryEntry(string Id, string Name, string? AuthorId, string? AuthorName, string? ThumbnailImageUrl, DateTimeOffset LastUsedAt, bool IsCurrent);
public sealed record AvatarExistsResponse(bool Exists);

// The Favorites page's local avatar history: the last 100 avatars worn that someone else made, and
// the check that finds local favorites VRChat no longer serves.
public sealed partial class AccountsController
{
    /// <summary>Avatars the account wore lately that it did not make, newest first, at most 100.</summary>
    /// <response code="200">The history</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/avatar-history")]
    [ProducesResponseType<IReadOnlyList<AvatarHistoryEntry>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetAvatarHistory([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == accountId, cancellationToken);
        if (account is null) return Problem(AccountError.AccountNotFound);
        var after = account.AvatarHistoryClearedAt ?? DateTimeOffset.MinValue;
        var history = await _db.AvatarUsages.AsNoTracking()
            .Where(u => u.OwnerUserId == accountId && u.LastUsedAt > after && u.Avatar.AuthorId != accountId)
            .OrderByDescending(u => u.LastUsedAt)
            .Take(100)
            .Select(u => new AvatarHistoryEntry(u.AvatarId, u.Avatar.Name, u.Avatar.AuthorId, u.Avatar.AuthorName, u.Avatar.ThumbnailImageUrl ?? u.Avatar.ImageUrl, u.LastUsedAt, u.AvatarId == account.CurrentAvatarId))
            .ToListAsync(cancellationToken);
        return Ok(history);
    }

    /// <summary>Empty the avatar history. The usage rows and time worn are kept.</summary>
    /// <response code="204">Cleared</response>
    /// <response code="404">No such account</response>
    [HttpDelete("{accountId}/avatar-history")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ClearAvatarHistory([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.UserId == accountId, cancellationToken);
        if (account is null) return Problem(AccountError.AccountNotFound);
        account.AvatarHistoryClearedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Whether VRChat still serves an avatar: any refusal counts as gone, as the original's check does.</summary>
    /// <response code="200">The answer</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/avatars/{avatarId}/exists")]
    [ProducesResponseType<AvatarExistsResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AvatarExists([FromRoute] string accountId, [FromRoute] string avatarId, CancellationToken cancellationToken) =>
        FetchAsync(accountId, async client =>
        {
            try
            {
                return new AvatarExistsResponse(await client.GetAvatarAsync(avatarId, cancellationToken) is not null);
            }
            catch (VrchatApiException ex) when (ex is not VrchatUnauthorizedException)
            {
                return new AvatarExistsResponse(false);
            }
        });
}
