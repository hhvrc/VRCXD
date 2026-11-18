using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.Services.Queries;
using VRCX.Common.Utils.Pagination;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Controller.Accounts;

// The per-account lists VRChat keeps and the full sync mirrors: notifications, player
// moderations, favorites - and the account's own instance visits (its game log).
public sealed partial class AccountsController
{
    /// <summary>The account's notifications (both feeds), newest first.</summary>
    /// <response code="200">A page of notifications</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/notifications")]
    [ProducesResponseType<PagedResult<NotificationResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListNotifications([FromRoute] string accountId, [FromQuery] PaginationQuery pagination, [FromQuery] bool includeHidden, [FromServices] AccountListQueries lists, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await lists.Notifications(accountId, includeHidden).ToPagedResultAsync(pagination, cancellationToken));
    }

    /// <summary>Blocks, mutes and avatar hides the account has applied, as VRChat reports them.</summary>
    /// <response code="200">The moderations</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/moderations")]
    [ProducesResponseType<PlayerModerationResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListModerations([FromRoute] string accountId, [FromServices] AccountListQueries lists, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await lists.ModerationsAsync(accountId, cancellationToken));
    }

    /// <summary>The account's VRChat favorites of one kind, with their groups.</summary>
    /// <param name="accountId"></param>
    /// <param name="type">friend, world or avatar</param>
    /// <param name="lists"></param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">Groups and favorites of that kind</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/favorites")]
    [ProducesResponseType<FavoritesResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListFavorites([FromRoute] string accountId, [FromQuery] FavoriteType type, [FromServices] AccountListQueries lists, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await lists.FavoritesAsync(accountId, type, cancellationToken));
    }

    /// <summary>The account's own instance visits (its game log), newest first.</summary>
    /// <response code="200">A page of visits</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/visits")]
    [ProducesResponseType<PagedResult<InstanceVisitResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListVisits([FromRoute] string accountId, [FromQuery] PaginationQuery pagination, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);

        var page = await _db.InstanceVisits.AsNoTracking()
            .Where(v => v.UserId == accountId)
            .OrderByDescending(v => v.JoinedAt).ThenByDescending(v => v.Id)
            .Select(v => new InstanceVisitResponse
            {
                Id = v.Id, UserId = v.UserId, DisplayName = v.User.DisplayName, InstanceLocation = v.InstanceLocation,
                WorldId = v.Instance.WorldId, WorldName = v.Instance.World.Name, AccessType = v.Instance.Type,
                JoinedAt = v.JoinedAt, LeftAt = v.LeftAt, Source = v.Source,
            })
            .ToPagedResultAsync(pagination, cancellationToken);
        return Ok(page);
    }
}
