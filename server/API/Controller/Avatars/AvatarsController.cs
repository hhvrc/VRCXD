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

namespace VRCX.API.Controller.Avatars;

/// <summary>
/// Avatars, as stored here. Read by Common/Features/Content, after it has fetched from VRChat when asked to.
/// </summary>
[ApiController]
[Tags("Avatars")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/avatars")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class AvatarsController(ContentQueries content, ContentRefresh fetch) : VrcxControllerBase
{
    /// <summary>
    /// An avatar as last seen. With refresh=true, GET avatars/{id} is fetched from VRChat first.
    /// </summary>
    /// <response code="200">The avatar</response>
    /// <response code="404">Never seen, or VRChat has no such avatar (or it is private)</response>
    /// <response code="409">refresh=true but no account is logged in</response>
    [HttpGet("{avatarId}")]
    [ProducesResponseType<AvatarResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetAvatar([FromRoute] string avatarId, [FromQuery] bool refresh, [FromQuery] string? account, CancellationToken cancellationToken)
    {
        if (refresh && await fetch.AvatarAsync(account, avatarId, cancellationToken) is { } problem) return Problem(problem);
        var avatar = await content.FindAvatarAsync(avatarId, cancellationToken);
        return avatar is null ? Problem(AvatarError.AvatarNotFound) : Ok(AvatarResponse.From(avatar));
    }
}
