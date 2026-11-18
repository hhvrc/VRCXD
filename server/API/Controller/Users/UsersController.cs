using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Features.Content;
using VRCX.Common.Features.Users;
using VRCX.Common.Services.Presence;

namespace VRCX.API.Controller.Users;

/// <summary>
/// VRChat users as stored here: the latest state plus the history of every change seen. Read by
/// Common/Features/Users, after Common/Features/Content has fetched from VRChat when asked to.
/// </summary>
[ApiController]
[Tags("Users")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/users")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class UsersController(UserQueries users, UserSearch search, ContentRefresh fetch, IPresenceCache presence) : VrcxControllerBase
{
    /// <summary>
    /// A user as last seen. With refresh=true, GET users/{id} is fetched from VRChat first (through the given account's session, or any live one),
    /// unless it was less than 30 seconds ago.
    /// </summary>
    /// <param name="userId">usr_…</param>
    /// <param name="refresh">Fetch from VRChat before answering</param>
    /// <param name="account">Which logged-in account's session to fetch with</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">The user</response>
    /// <response code="404">Never seen, or VRChat has no such user</response>
    /// <response code="409">refresh=true but no account is logged in</response>
    [HttpGet("{userId}")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetUser([FromRoute] string userId, [FromQuery] bool refresh, [FromQuery] string? account, CancellationToken cancellationToken)
    {
        if (refresh && await fetch.UserAsync(account, userId, cancellationToken) is { } problem) return Problem(problem);
        var user = await users.FindUserAsync(userId, cancellationToken);
        if (user is null) return Problem(UserError.UserNotFound);
        return Ok(UserResponse.From(user, await presence.GetAsync(userId, cancellationToken)));
    }

    /// <summary>
    /// Everything that changed about a user while we were watching: names, trust, status, bio, avatars, presence, instance visits.
    /// </summary>
    /// <response code="200">The history (each list newest first, capped at 100 entries)</response>
    /// <response code="404">Never seen</response>
    [HttpGet("{userId}/history")]
    [ProducesResponseType<UserHistoryResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetUserHistory([FromRoute] string userId, CancellationToken cancellationToken)
    {
        if (!await users.ExistsAsync(userId, cancellationToken)) return Problem(UserError.UserNotFound);
        return Ok(await users.HistoryAsync(userId, 100, null, cancellationToken));
    }

    /// <summary>Search VRChat for users by display name (through a logged-in account) and store what comes back.</summary>
    /// <param name="q">Display name fragment</param>
    /// <param name="account">Which logged-in account's session to search with</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">Up to 20 results</response>
    /// <response code="409">No account is logged in</response>
    [HttpGet("search")]
    [ProducesResponseType<UserSearchResultResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] string? account, CancellationToken cancellationToken)
    {
        return Answer(await search.SearchAsync(q, account, cancellationToken));
    }
}
