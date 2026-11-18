using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.Moderation;

namespace VRCX.API.Controller.Moderation;

/// <summary>
/// The player moderations an account applied: the list, and applying, removing and refreshing them.
/// Everything is done by Common/Features/Moderation; the routes stay under /accounts, where they
/// were while they were part of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class ModerationController(AccountQueries accounts, ModerationQueries queries, ModerationActions actions) : VrcxControllerBase
{
    private const string ModerationTypes = "^(block|mute|muteChat|interactOff)$";
    // VRChat also reports the opposite kinds, such as an explicit unmute, and any of them can be removed.
    private const string AllModerationTypes = "^(block|unblock|mute|unmute|interactOn|interactOff|muteChat|unmuteChat)$";

    /// <summary>Blocks, mutes and avatar hides the account has applied, as VRChat reports them.</summary>
    /// <response code="200">The moderations</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/moderations")]
    [ProducesResponseType<PlayerModerationResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListModerations([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        if (!await accounts.ExistsAsync(accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await queries.ModerationsAsync(accountId, cancellationToken));
    }

    /// <summary>Block, mute, disable the chatbox of, or disable avatar interaction with a user.</summary>
    /// <response code="204">Applied</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/moderations/{userId}/{type:regex(" + ModerationTypes + ")}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SendModeration([FromRoute] string accountId, [FromRoute] string userId, [FromRoute] string type, CancellationToken cancellationToken) =>
        Done(await actions.SendAsync(accountId, userId, type, cancellationToken));

    /// <summary>Undo a block, mute, disabled chatbox or disabled avatar interaction.</summary>
    /// <response code="204">Removed</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpDelete("{accountId}/moderations/{userId}/{type:regex(" + AllModerationTypes + ")}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> DeleteModeration([FromRoute] string accountId, [FromRoute] string userId, [FromRoute] string type, CancellationToken cancellationToken) =>
        Done(await actions.DeleteAsync(accountId, userId, type, cancellationToken));

    /// <summary>Remove every moderation of one kind from the account.</summary>
    /// <response code="204">Removed</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpDelete("{accountId}/moderations/{type:regex(" + AllModerationTypes + ")}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ClearModerations([FromRoute] string accountId, [FromRoute] string type, CancellationToken cancellationToken) =>
        Done(await actions.ClearAsync(accountId, type, cancellationToken));

    /// <summary>Fetch the account's moderations from VRChat now instead of waiting for the next sync.</summary>
    /// <response code="204">Refreshed</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/moderations/refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> RefreshModerations([FromRoute] string accountId, CancellationToken cancellationToken) =>
        Done(await actions.RefreshAsync(accountId, cancellationToken));

    private IActionResult Done(OpenShockProblem? problem) => problem is null ? NoContent() : Problem(problem);
}
