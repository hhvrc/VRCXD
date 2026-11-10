using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Authentication;
using VRCX.Common;
using VRCX.Common.Services;

namespace VRCX.API.Controller.Status;

/// <summary>VRChat's own service status, which needs no VRChat login.</summary>
[ApiController]
[Tags("Status")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/vrchat-status")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class VrchatStatusController(VrchatStatus status) : VrcxControllerBase
{
    /// <summary>
    /// status.vrchat.com as the original's status bar and login page show it: empty status when all
    /// systems are operational, else the description, indicator and the affected components.
    /// </summary>
    /// <response code="200">The status</response>
    [HttpGet]
    [ProducesResponseType<VrchatStatusReport>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) => Ok(await status.GetAsync(cancellationToken));
}
