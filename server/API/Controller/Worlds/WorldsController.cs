using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Features.Content;

namespace VRCX.API.Controller.Worlds;

/// <summary>
/// Worlds, as stored here. Read by Common/Features/Content, after it has fetched from VRChat when asked to.
/// </summary>
[ApiController]
[Tags("Worlds")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/worlds")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class WorldsController(ContentQueries content, ContentRefresh fetch) : VrcxControllerBase
{
    /// <summary>
    /// A world as last seen. With refresh=true, GET worlds/{id} is fetched from VRChat first, unless it was less than 30 seconds ago.
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
        if (refresh && await fetch.WorldAsync(account, worldId, cancellationToken) is { } problem) return Problem(problem);
        var world = await content.FindWorldAsync(worldId, cancellationToken);
        return world is null ? Problem(WorldError.WorldNotFound) : Ok(WorldResponse.From(world));
    }
}
