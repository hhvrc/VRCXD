using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Services.Sessions;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.API.Controller.Worlds;

/// <summary>
/// Worlds, as stored here.
/// </summary>
[ApiController]
[Tags("Worlds")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/worlds")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed partial class WorldsController : VrcxControllerBase
{
    private readonly VrcxContext _db;
    private readonly IAccountSessionManager _sessions;
    private readonly IAccountSyncService _sync;

    public WorldsController(VrcxContext db, IAccountSessionManager sessions, IAccountSyncService sync)
    {
        _db = db;
        _sessions = sessions;
        _sync = sync;
    }

    /// <summary>
    /// A world as last seen. With refresh=true, GET worlds/{id} is fetched from VRChat first.
    /// </summary>
    /// <response code="200">The world</response>
    /// <response code="404">Never seen, or VRChat has no such world</response>
    /// <response code="409">refresh=true but no account is logged in</response>
    [HttpGet("{worldId}")]
    [ProducesResponseType<WorldResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetWorld([FromRoute] string worldId, [FromQuery] bool refresh, [FromQuery] string? account, CancellationToken cancellationToken)
    {
        if (refresh)
        {
            var via = PickAccount(account, _sessions);
            if (via is null) return Problem(AccountError.NoAccounts);
            try
            {
                await _sync.RefreshWorldAsync(via, worldId, cancellationToken);
            }
            catch (VrchatApiException ex)
            {
                return VrchatProblem(ex);
            }
        }

        var world = await _db.Worlds.AsNoTracking().Include(w => w.Author).FirstOrDefaultAsync(w => w.Id == worldId, cancellationToken);
        if (world is null) return Problem(WorldError.WorldNotFound);
        return Ok(WorldResponse.From(world));
    }
}
