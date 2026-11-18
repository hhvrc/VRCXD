using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Features.UserActivity;

namespace VRCX.API.Controller.UserActivity;

/// <summary>
/// The original's Activity tab of the user pop-up. Everything is read by
/// Common/Features/UserActivity; the routes stay under /accounts, where they were while they were
/// part of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class UserActivityController(UserActivityQueries queries) : VrcxControllerBase
{
    /// <summary>The rows the Activity tab builds its charts from, over the last <paramref name="days"/> days, or everything for 0.</summary>
    /// <response code="200">The rows, oldest first</response>
    [HttpGet("{accountId}/users/{userId}/activity")]
    [ProducesResponseType<UserActivityResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetUserActivity([FromRoute] string accountId, [FromRoute] string userId, [FromQuery] int days, CancellationToken cancellationToken) =>
        Ok(await queries.UserActivityAsync(accountId, userId, days, cancellationToken));

    /// <summary>The worlds this account spent the most time in, or visited most, the original's getMyTopWorlds.</summary>
    /// <response code="200">The worlds, best first</response>
    [HttpGet("{accountId}/top-worlds")]
    [ProducesResponseType<IReadOnlyList<TopWorldResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListTopWorlds([FromRoute] string accountId, [FromQuery] int days, [FromQuery] int limit = 5, [FromQuery] string sortBy = "time", [FromQuery] string? excludeWorldId = null, CancellationToken cancellationToken = default) =>
        Ok(await queries.TopWorldsAsync(accountId, days, limit, sortBy, excludeWorldId, cancellationToken));
}
