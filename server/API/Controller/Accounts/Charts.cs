using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Services.Charts;
using VRCX.Vrchat;

namespace VRCX.API.Controller.Accounts;

// The Charts pages: instance activity (from the game log), hot worlds (from friends' moves)
// and the mutual-friend network (fetched from VRChat on request).
public sealed partial class AccountsController
{
    /// <summary>When the account's game-log stays ended, so the activity chart knows which days have data.</summary>
    /// <response code="200">Timestamps</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/activity/dates")]
    [ProducesResponseType<DateTimeOffset[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetActivityDates([FromRoute] string accountId, [FromServices] IChartsService charts, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await charts.GetActivityDatesAsync(accountId, cancellationToken));
    }

    /// <summary>The account's stays in instances between two instants, and everyone else's stays in those instances.</summary>
    /// <response code="200">The activity</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/activity")]
    [ProducesResponseType<InstanceActivity>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetActivity([FromRoute] string accountId, [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, [FromServices] IChartsService charts, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await charts.GetInstanceActivityAsync(accountId, from, to, cancellationToken));
    }

    /// <summary>Worlds the account's friends moved into most, by unique friends, over the last days.</summary>
    /// <param name="accountId"></param>
    /// <param name="days">7, 30 or 90 in the original; any positive number</param>
    /// <param name="limit">Default 30</param>
    /// <param name="charts"></param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">The worlds</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/hot-worlds")]
    [ProducesResponseType<HotWorld[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetHotWorlds([FromRoute] string accountId, [FromQuery] int days, [FromQuery] int limit, [FromServices] IChartsService charts, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await charts.GetHotWorldsAsync(accountId, days <= 0 ? 30 : days, limit <= 0 ? 30 : limit, cancellationToken));
    }

    /// <summary>Which friends moved into a world, with how often, over the last days.</summary>
    /// <response code="200">The friends</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/hot-worlds/{worldId}/friends")]
    [ProducesResponseType<HotWorldFriend[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetHotWorldFriends([FromRoute] string accountId, [FromRoute] string worldId, [FromQuery] int days, [FromServices] IChartsService charts, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await charts.GetHotWorldFriendsAsync(accountId, worldId, days <= 0 ? 30 : days, cancellationToken));
    }

    /// <summary>The stored mutual-friend network, with the state of any running fetch.</summary>
    /// <response code="200">The snapshot</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/mutuals")]
    [ProducesResponseType<MutualGraphSnapshot>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetMutualGraph([FromRoute] string accountId, [FromServices] MutualGraphService mutuals, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await mutuals.GetSnapshotAsync(accountId, cancellationToken));
    }

    /// <summary>Just the fetch state, cheap enough to poll.</summary>
    /// <response code="200">The state</response>
    [HttpGet("{accountId}/mutuals/fetch")]
    [ProducesResponseType<MutualGraphFetchStatus>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public IActionResult GetMutualGraphFetch([FromRoute] string accountId, [FromServices] MutualGraphService mutuals) => Ok(mutuals.GetStatus(accountId));

    /// <summary>Starts fetching every friend's mutuals in the background.</summary>
    /// <response code="202">Started</response>
    /// <response code="404">No such account</response>
    /// <response code="409">Already running, or no live session</response>
    [HttpPost("{accountId}/mutuals/fetch")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> StartMutualGraphFetch([FromRoute] string accountId, [FromServices] MutualGraphService mutuals, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        if (_sessions.Get(accountId) is null) return Problem(AccountError.NoLiveSession);
        if (!await mutuals.StartFetchAsync(accountId, cancellationToken)) return Problem(AccountError.FetchAlreadyRunning);
        return Accepted();
    }

    /// <summary>Stops a running fetch; links stored so far stay.</summary>
    /// <response code="204">Stopped (or nothing was running)</response>
    [HttpDelete("{accountId}/mutuals/fetch")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [MapToApiVersion("1")]
    public IActionResult CancelMutualGraphFetch([FromRoute] string accountId, [FromServices] MutualGraphService mutuals)
    {
        mutuals.CancelFetch(accountId);
        return NoContent();
    }

    /// <summary>Fetches one user's mutuals now (the graph's "Refresh Mutuals").</summary>
    /// <response code="200">Whether it worked, and whether they have sharing turned off</response>
    /// <response code="404">No such account</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/mutuals/{userId}/refresh")]
    [ProducesResponseType<MutualRefreshResult>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> RefreshMutuals([FromRoute] string accountId, [FromRoute] string userId, [FromServices] MutualGraphService mutuals, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        if (_sessions.Get(accountId) is null) return Problem(AccountError.NoLiveSession);
        try
        {
            return Ok(await mutuals.RefreshFriendAsync(accountId, userId, cancellationToken));
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
}
