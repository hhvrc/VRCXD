using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Features.Database;

namespace VRCX.API.Controller.Status;

/// <summary>The server's database, read only. The counting is done by Common/Features/Database.</summary>
[ApiController]
[Tags("Status")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/database")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class DatabaseController(DatabaseQueries queries) : VrcxControllerBase
{
    /// <summary>
    /// Row counts for the original's Settings, Advanced, Database panel (its SQLite table sizes), over
    /// the tables that hold the same history here, for every account on the server. GPS and
    /// online/offline are presence events; location is the accounts' own stays and join/leave everyone
    /// else's; friend log history is friendships started and ended plus friend requests.
    /// </summary>
    /// <response code="200">The counts</response>
    [HttpGet("table-sizes")]
    [ProducesResponseType<TableSizesResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> TableSizes(CancellationToken cancellationToken) =>
        Ok(await queries.TableSizesAsync(cancellationToken));
}
