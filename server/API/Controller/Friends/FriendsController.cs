using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.Friends;

namespace VRCX.API.Controller.Friends;

/// <summary>
/// An account's friends: the list, the Friend List page's details, the friend log, and friend
/// requests and unfriending. Everything is done by Common/Features/Friends; the routes stay under
/// /accounts, where they were while they were part of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class FriendsController(AccountQueries accounts, FriendQueries queries, FriendActions actions) : VrcxControllerBase
{
    /// <summary>
    /// The account's current friends with their latest known presence, in friend-number order.
    /// </summary>
    /// <response code="200">The friends</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/friends")]
    [ProducesResponseType<FriendResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListFriends([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        if (!await accounts.ExistsAsync(accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await queries.ListFriendsAsync(accountId, cancellationToken));
    }

    /// <summary>Every current friend with what the Friend List page shows and searches.</summary>
    /// <response code="200">The friends</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/friends/details")]
    [ProducesResponseType<FriendDetailsResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListFriendDetails([FromRoute] string accountId, CancellationToken cancellationToken) =>
        await queries.ListFriendDetailsAsync(accountId, cancellationToken) is { } details ? Ok(details) : Problem(AccountError.AccountNotFound);

    /// <summary>Fetch a friend's full user object and profile from VRChat now: the Friend List's load missing entries.</summary>
    /// <response code="204">Fetched</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/friends/{userId}/load")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> LoadFriend([FromRoute] string accountId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        Done(await actions.LoadAsync(accountId, userId, cancellationToken));

    /// <summary>Read the friend list from VRChat again, as the sidebar's refresh button does.</summary>
    /// <response code="204">Refreshed</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/friends/refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> RefreshFriends([FromRoute] string accountId, CancellationToken cancellationToken) =>
        Done(await actions.RefreshAsync(accountId, cancellationToken));

    /// <summary>Unfriend on VRChat.</summary>
    /// <response code="204">Unfriended</response>
    [HttpDelete("{accountId}/friends/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> Unfriend([FromRoute] string accountId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        Done(await actions.UnfriendAsync(accountId, userId, cancellationToken));

    /// <summary>Send a friend request from this account.</summary>
    /// <response code="204">Sent</response>
    /// <response code="409">The account has no live VRChat session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/friend-requests/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SendFriendRequest([FromRoute] string accountId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        Done(await actions.SendRequestAsync(accountId, userId, cancellationToken));

    /// <summary>Withdraw a friend request this account sent.</summary>
    /// <response code="204">Withdrawn</response>
    [HttpDelete("{accountId}/friend-requests/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> CancelFriendRequest([FromRoute] string accountId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        Done(await actions.CancelRequestAsync(accountId, userId, cancellationToken));

    /// <summary>
    /// The account's friend log: friendships starting and ending, friend requests it sent through
    /// VRCX, and friends' name and trust changes while they were friends. Newest first, without the
    /// rows the account removed. Whole by default; with limit, a page of the entries older than
    /// before: at least limit of them (fewer only at the end), and every entry at the oldest time
    /// in the page, so the next page is the one before that time and no entry is cut in two pages.
    /// </summary>
    /// <param name="accountId">usr_…</param>
    /// <param name="limit">Entries per page (at least this many, ties at the end included)</param>
    /// <param name="before">Only entries older than this: the last entry's createdAt of the previous page</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">The log</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/friend-log")]
    [ProducesResponseType<FriendLogEntryResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> FriendLog([FromRoute] string accountId, [FromQuery] int? limit, [FromQuery] DateTimeOffset? before, CancellationToken cancellationToken)
    {
        if (!await accounts.ExistsAsync(accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await queries.FriendLogAsync(accountId, cancellationToken, limit, before));
    }

    /// <summary>Remove a row from the account's friend log view. The history behind it stays in the database.</summary>
    /// <response code="204">Removed</response>
    /// <response code="404">No such account</response>
    [HttpDelete("{accountId}/friend-log/{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> HideFriendLogEntry([FromRoute] string accountId, [FromRoute] string key, CancellationToken cancellationToken)
    {
        if (!await accounts.ExistsAsync(accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        await actions.HideLogEntryAsync(accountId, key, cancellationToken);
        return NoContent();
    }

    private IActionResult Done(OpenShockProblem? problem) => problem is null ? NoContent() : Problem(problem);
}
