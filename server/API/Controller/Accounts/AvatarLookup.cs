using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.Services.Ingestion;
using VRCX.Vrchat;

namespace VRCX.API.Controller.Accounts;

// Turning an avatar picture back into the avatar it shows.
public sealed partial class AccountsController
{
    /// <summary>Which avatar a wearer's picture shows, the original's showAvatarAuthorDialog: their own current avatar, one the server has seen with that image, and otherwise only who owns it.</summary>
    /// <response code="200">What could be found. Either field can be missing.</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/avatars/lookup")]
    [ProducesResponseType<AvatarLookupResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> LookupAvatar([FromRoute] string accountId, [FromQuery] string imageUrl, [FromQuery] string? userId, [FromServices] IAvatarImageResolver resolver, [FromServices] VrchatStore store, CancellationToken cancellationToken)
    {
        var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == accountId, cancellationToken);
        if (account is null) return Problem(AccountError.AccountNotFound);
        var info = await resolver.ResolveAsync(imageUrl, cancellationToken);
        var avatarId = userId == accountId ? account.CurrentAvatarId : null;
        if (avatarId is null && VrchatApiClient.ExtractFileId(imageUrl) is { } fileId)
        {
            avatarId = await _db.Avatars.AsNoTracking()
                .Where(a => a.ImageUrl!.Contains(fileId) || a.ThumbnailImageUrl!.Contains(fileId))
                .Select(a => a.Id)
                .FirstOrDefaultAsync(cancellationToken);
            // The original's refreshUserDialogAvatars(fileId): one of the account's own uploads is
            // looked for in its avatars read again from VRChat.
            if (avatarId is null && info?.OwnerId == accountId && _sessions.Get(accountId) is { } client)
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    foreach (var api in await client.GetOwnAvatarsAsync(cancellationToken))
                    {
                        await store.UpsertAvatarAsync(_db, api, now, cancellationToken);
                        if (avatarId is null && (api.ImageUrl.Contains(fileId) || api.ThumbnailImageUrl.Contains(fileId))) avatarId = api.Id;
                    }
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (VrchatApiException ex) when (ex is not VrchatUnauthorizedException)
                {
                    _logger.LogWarning(ex, "Own avatars of {Account} could not be read for an avatar lookup", accountId);
                }
            }
        }
        return Ok(new AvatarLookupResponse { AvatarId = avatarId, OwnerId = info?.OwnerId, AvatarName = info?.AvatarName });
    }
}
