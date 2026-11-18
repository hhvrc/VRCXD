using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Problems;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Features.Content;
using VRCX.Vrchat;

namespace VRCX.API.Controller.Instances;

/// <summary>
/// Instances, as stored here. Read by Common/Features/Content, after it has fetched from VRChat when asked to.
/// </summary>
[ApiController]
[Tags("Instances")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/instances")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class InstancesController(ContentQueries content, ContentRefresh fetch) : VrcxControllerBase
{
    /// <summary>
    /// An instance by its location tag ("wrld_…:12345~region(eu)"), with everyone we believe is there now.
    /// With refresh=true, GET instances/{location} is fetched from VRChat first, unless it was less than 30 seconds ago.
    /// </summary>
    /// <response code="200">The instance</response>
    /// <response code="400">The tag is not an instance location</response>
    /// <response code="404">Never seen, or VRChat has no such instance</response>
    /// <response code="409">refresh=true but no account is logged in</response>
    [HttpGet("{location}")]
    [ProducesResponseType<InstanceResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetInstance([FromRoute] string location, [FromQuery] bool refresh, [FromQuery] string? account, CancellationToken cancellationToken)
    {
        var parsed = VrchatLocation.Parse(location);
        if (!parsed.IsRealInstance) return Problem(InstanceError.NotAnInstance);

        if (refresh && await fetch.InstanceAsync(account, parsed.InstanceLocation, cancellationToken) is { } problem) return Problem(problem);
        var instance = await content.InstanceDetailsAsync(parsed.InstanceLocation, cancellationToken);
        return instance is null ? Problem(InstanceError.InstanceNotFound) : Ok(instance);
    }
}
