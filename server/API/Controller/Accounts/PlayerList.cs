using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Services.Queries;

namespace VRCX.API.Controller.Accounts;

// The Player List page: the instance the account is in right now, and everyone the game log says is there.
public sealed partial class AccountsController
{
    /// <summary>The account's current instance and who is in it.</summary>
    /// <response code="200">The instance, or empty fields when the account is not in one</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/player-list")]
    [ProducesResponseType<PlayerListResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetPlayerList([FromRoute] string accountId, [FromServices] GameLogQueries gameLog, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await gameLog.PlayerListAsync(accountId, cancellationToken));
    }
}
