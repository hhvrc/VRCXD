using Microsoft.AspNetCore.Mvc;
using VRCX.Common.Errors;
using VRCX.Common.Services.Sessions;
using VRCX.Vrchat;

namespace VRCX.Common;

// Inherits Problem(OpenShockProblem) from OpenShock.Internal.Common.OpenShockControllerBase;
// the members below are VRCX-specific and stay local.
public abstract class VrcxControllerBase : OpenShock.Internal.Common.OpenShockControllerBase
{
    /// <summary>
    /// Picks the VRChat session a read-through should use: the requested account when it has a
    /// live session, otherwise any live account, otherwise null (the caller answers with
    /// <see cref="AccountError.NoAccounts"/>).
    /// </summary>
    [NonAction]
    protected static string? PickAccount(string? requested, IAccountSessionManager sessions) =>
        requested is not null && sessions.Get(requested) is not null ? requested : sessions.ActiveAccountIds.FirstOrDefault();

    /// <summary>What a feature handed back, as the response: 200 with the value, 204 for a read that found nothing, else the problem.</summary>
    [NonAction]
    protected IActionResult Answer<T>(Result<T> result) => result switch
    {
        VrcxProblem problem => Problem(problem),
        null => NoContent(),
        T value => Ok(value),
    };

    /// <summary>The same with the value shaped for the response first.</summary>
    [NonAction]
    protected IActionResult Answer<T>(Result<T> result, Func<T, IActionResult> found) where T : notnull => result switch
    {
        VrcxProblem problem => Problem(problem),
        T value => found(value),
    };

    /// <summary>Maps a failed VRChat call onto a problem response.</summary>
    [NonAction]
    protected ObjectResult VrchatProblem(VrchatApiException exception) =>
        Problem(exception is VrchatUnauthorizedException ? VrchatError.SessionExpired : VrchatError.Upstream(exception.Message));
}
