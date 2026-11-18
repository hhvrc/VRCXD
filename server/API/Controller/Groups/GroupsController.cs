using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Features.Content;

namespace VRCX.API.Controller.Groups;

/// <summary>
/// Groups, as stored here. Read by Common/Features/Content, after it has fetched from VRChat when asked to.
/// </summary>
[ApiController]
[Tags("Groups")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/groups")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class GroupsController(ContentQueries content, ContentRefresh fetch) : VrcxControllerBase
{
    /// <summary>
    /// A group with its roles and current members. With refresh=true, GET groups/{id}?includeRoles=true is fetched from VRChat first, unless it was less than 30 seconds ago.
    /// </summary>
    /// <response code="200">The group</response>
    /// <response code="404">Never seen, or VRChat has no such group</response>
    /// <response code="409">refresh=true but no account is logged in</response>
    [HttpGet("{groupId}")]
    [ProducesResponseType<GroupResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetGroup([FromRoute] string groupId, [FromQuery] bool refresh, [FromQuery] string? account, CancellationToken cancellationToken)
    {
        if (refresh && await fetch.GroupAsync(account, groupId, cancellationToken) is { } problem) return Problem(problem);
        var group = await content.GroupDetailsAsync(groupId, cancellationToken);
        return group is null ? Problem(GroupError.GroupNotFound) : Ok(group);
    }
}
