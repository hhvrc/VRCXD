using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common;
using VRCX.Common.Authentication;

namespace VRCX.API.Controller.Auth;

/// <summary>
/// The cookie the page's socket and images are let in by (<see cref="MediaCookie"/>). Any request
/// with the token sets it; the page asks for it here before its first socket connects, and takes
/// it off when the token is forgotten.
/// </summary>
[ApiController]
[Tags("Auth")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/auth/media-cookie")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class MediaCookieController(MediaCookie cookie) : VrcxControllerBase
{
    /// <summary>Sets the cookie for /1/ws and /1/images.</summary>
    /// <response code="204">Set</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [MapToApiVersion("1")]
    public IActionResult Set()
    {
        cookie.Set(HttpContext);
        return NoContent();
    }

    /// <summary>Takes the cookie off, so this browser's socket and images need the token again.</summary>
    /// <response code="204">Taken off</response>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [MapToApiVersion("1")]
    public IActionResult Clear()
    {
        MediaCookie.Clear(HttpContext);
        return NoContent();
    }
}
