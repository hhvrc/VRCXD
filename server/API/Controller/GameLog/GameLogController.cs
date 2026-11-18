using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.GameLog;

namespace VRCX.API.Controller.GameLog;

/// <summary>
/// What the account's game log holds: the Game Log page (every kind of row in one list, and
/// instances with what happened in them), the Player List page, and the original's Previous
/// Instances dialogs. Everything is read by Common/Features/GameLog; the routes stay under
/// /accounts, where they were while they were part of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class GameLogController(AccountQueries accounts, GameLogQueries gameLog, PreviousInstanceQueries previous, GameLogActions actions) : VrcxControllerBase
{
    /// <summary>The account's game log, newest first: places it went, players it saw, videos, portals, events and resource loads.</summary>
    /// <param name="accountId"></param>
    /// <param name="types">Comma separated types, or empty for all</param>
    /// <param name="search">Part of a name, world, url or message</param>
    /// <param name="vip">Only favorite friends among rows that name a user</param>
    /// <param name="limit">Most rows to return</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">The rows</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/game-log")]
    [ProducesResponseType<GameLogRowResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListGameLog([FromRoute] string accountId, [FromQuery] string? types, [FromQuery] string? search, [FromQuery] bool vip, [FromQuery] int limit = 500, CancellationToken cancellationToken = default)
    {
        if (!await accounts.ExistsAsync(accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await gameLog.ListAsync(accountId, types, search, vip, limit, cancellationToken));
    }

    /// <summary>Instances the account was in, newest first, with who came and went and what played there.</summary>
    /// <param name="accountId"></param>
    /// <param name="beforeId">Continue after this location row</param>
    /// <param name="limit">Most instances to return</param>
    /// <param name="from">Only stays that started after this</param>
    /// <param name="to">Only stays that started before this</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">The instances and their rows</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/game-log/sessions")]
    [ProducesResponseType<GameLogSessionsResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListGameLogSessions([FromRoute] string accountId, [FromQuery] long? beforeId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] int limit = 10, CancellationToken cancellationToken = default)
    {
        if (!await accounts.ExistsAsync(accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await gameLog.SessionsAsync(accountId, beforeId, from, to, limit, cancellationToken));
    }

    /// <summary>Remove a row from the account's game log view. The row itself stays in the database.</summary>
    /// <response code="204">Removed</response>
    /// <response code="404">No such account</response>
    [HttpDelete("{accountId}/game-log/{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> HideGameLogEntry([FromRoute] string accountId, [FromRoute] string key, CancellationToken cancellationToken)
    {
        if (!await accounts.ExistsAsync(accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        await actions.HideEntryAsync(accountId, key, cancellationToken);
        return NoContent();
    }

    /// <summary>The account's current instance and who is in it.</summary>
    /// <response code="200">The instance, or empty fields when the account is not in one</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/player-list")]
    [ProducesResponseType<PlayerListResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetPlayerList([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        if (!await accounts.ExistsAsync(accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await gameLog.PlayerListAsync(accountId, cancellationToken));
    }

    /// <summary>Instances a user was seen in, or this account's own instances of a world or group.</summary>
    /// <response code="200">One row per instance, newest first</response>
    [HttpGet("{accountId}/previous-instances")]
    [ProducesResponseType<IReadOnlyList<PreviousInstanceResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListPreviousInstances([FromRoute] string accountId, [FromQuery] string? userId, [FromQuery] string? worldId, [FromQuery] string? groupId, CancellationToken cancellationToken) =>
        Ok(await previous.ListAsync(accountId, userId, worldId, groupId, cancellationToken));

    /// <summary>Who was seen in one instance: a total per player, and each stay for the chart.</summary>
    /// <response code="200">The players</response>
    [HttpGet("{accountId}/previous-instances/players")]
    [ProducesResponseType<PreviousInstancePlayersResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListPreviousInstancePlayers([FromRoute] string accountId, [FromQuery] string location, CancellationToken cancellationToken) =>
        Ok(await previous.PlayersAsync(accountId, location, cancellationToken));
}
