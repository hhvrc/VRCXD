using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.Utils.Pagination;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Controller.Accounts;

// The account's own instance visits (its game log).
public sealed partial class AccountsController
{
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
