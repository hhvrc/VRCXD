using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Services.Queries;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Controller.Accounts;

// The Game Log page: every kind of game log row in one list, and instances with what happened in them.
public sealed partial class AccountsController
{
    /// <summary>The account's game log, newest first: places it went, players it saw, videos, portals, events and resource loads.</summary>
    /// <param name="accountId"></param>
    /// <param name="types">Comma separated types, or empty for all</param>
    /// <param name="search">Part of a name, world, url or message</param>
    /// <param name="vip">Only favorite friends among rows that name a user</param>
    /// <param name="limit">Most rows to return</param>
    /// <param name="gameLog"></param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">The rows</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/game-log")]
    [ProducesResponseType<GameLogRowResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListGameLog([FromRoute] string accountId, [FromQuery] string? types, [FromQuery] string? search, [FromQuery] bool vip, [FromServices] GameLogQueries gameLog, [FromQuery] int limit = 500, CancellationToken cancellationToken = default)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await gameLog.ListAsync(accountId, types, search, vip, limit, cancellationToken));
    }

    /// <summary>Instances the account was in, newest first, with who came and went and what played there.</summary>
    /// <param name="accountId"></param>
    /// <param name="beforeId">Continue after this location row</param>
    /// <param name="limit">Most instances to return</param>
    /// <param name="from">Only stays that started after this</param>
    /// <param name="to">Only stays that started before this</param>
    /// <param name="gameLog"></param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">The instances and their rows</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/game-log/sessions")]
    [ProducesResponseType<GameLogSessionsResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListGameLogSessions([FromRoute] string accountId, [FromQuery] long? beforeId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromServices] GameLogQueries gameLog, [FromQuery] int limit = 10, CancellationToken cancellationToken = default)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
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
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        if (!await _db.GameLogHiddenEntries.AnyAsync(h => h.OwnerUserId == accountId && h.EntryKey == key, cancellationToken))
        {
            _db.GameLogHiddenEntries.Add(new GameLogHiddenEntry { OwnerUserId = accountId, EntryKey = key, HiddenAt = DateTimeOffset.UtcNow });
            await _db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }
}
