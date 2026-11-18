using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Features.Avatars;

namespace VRCX.API.Controller.Avatars;

/// <summary>
/// The Favorites page's local avatar history. Everything is done by Common/Features/Avatars; the
/// routes stay under /accounts, where they were while they were part of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class AvatarHistoryController(AvatarHistory history) : VrcxControllerBase
{
    /// <summary>Avatars the account wore lately that it did not make, newest first, at most 100.</summary>
    /// <response code="200">The history</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/avatar-history")]
    [ProducesResponseType<IReadOnlyList<AvatarHistoryEntry>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetAvatarHistory([FromRoute] string accountId, CancellationToken cancellationToken) =>
        await history.ListAsync(accountId, cancellationToken) is { } entries ? Ok(entries) : Problem(AccountError.AccountNotFound);

    /// <summary>Empty the avatar history. The usage rows and time worn are kept.</summary>
    /// <response code="204">Cleared</response>
    /// <response code="404">No such account</response>
    [HttpDelete("{accountId}/avatar-history")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ClearAvatarHistory([FromRoute] string accountId, CancellationToken cancellationToken) =>
        await history.ClearAsync(accountId, cancellationToken) ? NoContent() : Problem(AccountError.AccountNotFound);

    /// <summary>Whether VRChat still serves an avatar: any refusal counts as gone, as the original's check does.</summary>
    /// <response code="200">The answer</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/avatars/{avatarId}/exists")]
    [ProducesResponseType<AvatarExistsResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> AvatarExists([FromRoute] string accountId, [FromRoute] string avatarId, CancellationToken cancellationToken)
    {
        var read = await history.ExistsAsync(accountId, avatarId, cancellationToken);
        return read.Problem is { } problem ? Problem(problem) : Ok(read.Value);
    }
}
