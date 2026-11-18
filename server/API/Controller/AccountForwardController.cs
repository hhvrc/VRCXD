using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.Common;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.Desktop;
using VRCX.Vrchat;

namespace VRCX.API.Controller;

/// <summary>
/// The base of the controllers that forward to VRChat for an account: an endpoint is one call on
/// the account's client, and these turn it into the response. The session, and what a missing or
/// expired one or a refusal from VRChat answers, are <see cref="AccountSessionRunner"/>'s, so a
/// forwarding controller takes no session manager and no client of its own. An endpoint that also
/// reads or writes the database does that through its feature's classes.
/// </summary>
public abstract class AccountForwardController(AccountSessionRunner runner) : VrcxControllerBase
{
    /// <summary>For the endpoint that needs more than one call's result turned into a response.</summary>
    protected AccountSessionRunner Runner => runner;

    /// <summary>What a feature's action answered: 204 when it worked, else its problem.</summary>
    [NonAction]
    protected IActionResult Done(OpenShockProblem? problem) => problem is null ? NoContent() : Problem(problem);

    /// <summary>What the agent on the account's PC answered: 204 for nothing, 200 with its JSON, else its problem.</summary>
    [NonAction]
    protected IActionResult Desktop(Result<string?> answer) => answer switch
    {
        VrcxProblem problem => Problem(problem),
        string { Length: > 0 } json => Content(json, System.Net.Mime.MediaTypeNames.Application.Json),
        _ => NoContent(),
    };

    /// <summary>Runs an action on VRChat with the account's own session and answers 204, or the problem it failed with.</summary>
    [NonAction]
    protected async Task<IActionResult> RunAsync(string accountId, Func<VrchatApiClient, Task> action) =>
        await runner.RunAsync(accountId, action, HttpContext.RequestAborted) is { } problem ? Problem(problem) : NoContent();

    /// <summary>Reads something live from VRChat and answers 200 with it, or the problem it failed with.</summary>
    [NonAction]
    protected async Task<IActionResult> ReadLiveAsync<T>(string accountId, Func<VrchatApiClient, Task<T>> read) =>
        Answer(await runner.ReadAsync(accountId, read, HttpContext.RequestAborted));

    /// <summary>The same for a fetch that may have nothing to answer: 204 then, 200 with the value otherwise.</summary>
    [NonAction]
    protected async Task<IActionResult> FetchAsync(string accountId, Func<VrchatApiClient, Task<object>> fetch) =>
        Answer(await runner.ReadAsync(accountId, fetch, HttpContext.RequestAborted));

    /// <summary>An uploaded file's bytes, for the uploads VRChat takes as one request.</summary>
    [NonAction]
    protected static async Task<byte[]> ReadAllAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
