using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Options;

namespace VRCX.API.Controller.Status;

public sealed record ErrorReportingResponse(string? Dsn);

/// <summary>The opt-in anonymous error reporting, which needs no VRChat login.</summary>
[ApiController]
[Tags("Status")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/error-reporting")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class ErrorReportingController(ErrorReportingOptions options) : VrcxControllerBase
{
    /// <summary>
    /// Where opted-in clients send error reports: the server's VRCX:ErrorReporting:Dsn, or null when
    /// error reporting is off. Clients neither ask for consent nor load the reporter while it is null.
    /// </summary>
    /// <response code="200">The DSN, or null</response>
    [HttpGet]
    [ProducesResponseType<ErrorReportingResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public IActionResult Get() =>
        Ok(new ErrorReportingResponse(string.IsNullOrWhiteSpace(options.Dsn) ? null : options.Dsn.Trim()));
}
