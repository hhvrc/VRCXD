using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Services.Feed;

namespace VRCX.API.Controller.Accounts;

public sealed partial class AccountsController
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
    /// <param name="feed"></param>
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
        [FromServices] IFeedService feed,
        CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);

        var typeList = (types ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => FeedService.AllTypes.Contains(t)).ToArray();
        var rows = await feed.QueryAsync(accountId, new FeedQuery
        {
            Types = typeList, Search = search ?? "", Vip = vip, From = from, To = to, Limit = limit <= 0 ? 500 : limit,
        }, cancellationToken);
        return Ok(rows);
    }
}
