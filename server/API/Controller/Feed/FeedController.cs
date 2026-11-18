using System.Net;
using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.Feed;

namespace VRCX.API.Controller.Feed;

/// <summary>
/// What to purge: OlderThanDays deletes the rows older than that many days, All deletes every row.
/// One of the two must be given, so an empty body deletes nothing.
/// </summary>
public sealed record PurgeAvatarFeedBody(int? OlderThanDays, bool All);

public sealed record PurgeAvatarFeedResponse(int Deleted);

/// <summary>
/// The account's feed and the purge of its avatar rows. Everything is done by
/// Common/Features/Feed; the routes stay under /accounts, where they were while they were part of
/// the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class FeedController(AccountQueries accounts, IFeedService feed, AvatarFeedCleanup cleanup) : VrcxControllerBase
{
    /// <summary>
    /// The account's feed: friends moving between instances (GPS), going online or offline, and
    /// changing status, bio or avatar. Newest first. The same filters the original offers: a
    /// type subset, a search (a wrld_/grp_ id searches locations), favorites only, a date range.
    /// </summary>
    /// <param name="accountId"></param>
    /// <param name="types">Comma-separated subset of GPS,Online,Offline,Status,Avatar,Bio; empty = all</param>
    /// <param name="search"></param>
    /// <param name="vip">Only favorite friends</param>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <param name="limit">Rows to return (default 500, max 100000)</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">The feed rows</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/feed")]
    [ProducesResponseType<FeedEntry[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetFeed(
        [FromRoute] string accountId,
        [FromQuery] string? types,
        [FromQuery] string? search,
        [FromQuery] bool vip,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        if (!await accounts.ExistsAsync(accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await feed.QueryAsync(accountId, new FeedQuery
        {
            Types = FeedService.ParseTypes(types), Search = search ?? "", Vip = vip, From = from, To = to, Limit = limit <= 0 ? 500 : limit,
        }, cancellationToken));
    }

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
    public async Task<IActionResult> PurgeAvatarFeed([FromRoute] string accountId, [FromBody] PurgeAvatarFeedBody body, CancellationToken cancellationToken)
    {
        if (!await accounts.ExistsAsync(accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        var deleted = await cleanup.PurgeChosenAsync(accountId, body.OlderThanDays, body.All, cancellationToken);
        if (deleted is null) return Problem(new OpenShockProblem("Database.PurgeNothingChosen", "Give olderThanDays or all", HttpStatusCode.BadRequest));
        return Ok(new PurgeAvatarFeedResponse(deleted.Value));
    }
}
