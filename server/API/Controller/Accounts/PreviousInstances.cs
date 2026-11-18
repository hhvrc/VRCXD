using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Services.Queries;

namespace VRCX.API.Controller.Accounts;

// The original's Previous Instances dialogs, read from the stored visits (PreviousInstanceQueries).
public sealed partial class AccountsController
{
    /// <summary>Instances a user was seen in, or this account's own instances of a world or group.</summary>
    /// <response code="200">One row per instance, newest first</response>
    [HttpGet("{accountId}/previous-instances")]
    [ProducesResponseType<IReadOnlyList<PreviousInstanceResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListPreviousInstances([FromRoute] string accountId, [FromQuery] string? userId, [FromQuery] string? worldId, [FromQuery] string? groupId, [FromServices] PreviousInstanceQueries previous, CancellationToken cancellationToken) =>
        Ok(await previous.ListAsync(accountId, userId, worldId, groupId, cancellationToken));

    /// <summary>Who was seen in one instance: a total per player, and each stay for the chart.</summary>
    /// <response code="200">The players</response>
    [HttpGet("{accountId}/previous-instances/players")]
    [ProducesResponseType<PreviousInstancePlayersResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListPreviousInstancePlayers([FromRoute] string accountId, [FromQuery] string location, [FromServices] PreviousInstanceQueries previous, CancellationToken cancellationToken) =>
        Ok(await previous.PlayersAsync(accountId, location, cancellationToken));

    private static long StayMs(DateTimeOffset joined, DateTimeOffset? left) => PreviousInstanceQueries.StayMs(joined, left);
}
