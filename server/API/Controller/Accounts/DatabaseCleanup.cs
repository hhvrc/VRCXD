using System.Net;
using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Services.Feed;

namespace VRCX.API.Controller.Accounts;

/// <summary>
/// What to purge: OlderThanDays deletes the rows older than that many days, All deletes every row.
/// One of the two must be given, so an empty body deletes nothing.
/// </summary>
public sealed record PurgeAvatarFeedBody(int? OlderThanDays, bool All);

public sealed record PurgeAvatarFeedResponse(int Deleted);

public sealed partial class AccountsController
{
    /// <summary>
    /// The original's Purge Avatar Feed Data (Settings, Advanced, Database Cleanup): deletes the
    /// account's avatar feed rows older than the given days, or all of them. Rows of friends another
    /// account on the server also has are kept, since they are in that account's feed too.
    /// </summary>
    /// <response code="200">How many rows were deleted</response>
    /// <response code="400">Neither a positive day count nor All was given</response>
    /// <response code="404">No such account</response>
    [HttpPost("{accountId}/database/purge-avatar-feed")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<PurgeAvatarFeedResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> PurgeAvatarFeed([FromRoute] string accountId, [FromBody] PurgeAvatarFeedBody body, [FromServices] AvatarFeedCleanup cleanup, [FromServices] TimeProvider time, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        DateTimeOffset? cutoff;
        if (body.All) cutoff = null;
        else if (body.OlderThanDays is > 0 and var days) cutoff = time.GetUtcNow().AddDays(-days);
        else return Problem(new OpenShockProblem("Database.PurgeNothingChosen", "Give olderThanDays or all", HttpStatusCode.BadRequest));

        var deleted = await cleanup.PurgeAsync(accountId, cutoff, cancellationToken);
        return Ok(new PurgeAvatarFeedResponse(deleted));
    }
}
