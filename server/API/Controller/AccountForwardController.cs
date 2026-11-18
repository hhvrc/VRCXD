using Microsoft.AspNetCore.Mvc;
using VRCX.Common;
using VRCX.Common.Features.Accounts;
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
    /// <summary>Runs an action on VRChat with the account's own session and answers 204, or the problem it failed with.</summary>
    [NonAction]
    protected async Task<IActionResult> RunAsync(string accountId, Func<VrchatApiClient, Task> action) =>
        await runner.RunAsync(accountId, action, HttpContext.RequestAborted) is { } problem ? Problem(problem) : NoContent();

    /// <summary>Reads something live from VRChat and answers 200 with it, or the problem it failed with.</summary>
    [NonAction]
    protected async Task<IActionResult> ReadLiveAsync<T>(string accountId, Func<VrchatApiClient, Task<T>> read)
    {
        var result = await runner.ReadAsync(accountId, read, HttpContext.RequestAborted);
        return result.Problem is { } problem ? Problem(problem) : Ok(result.Value);
    }

    /// <summary>The same for a fetch that may have nothing to answer: 204 then, 200 with the value otherwise.</summary>
    [NonAction]
    protected async Task<IActionResult> FetchAsync(string accountId, Func<VrchatApiClient, Task<object>> fetch)
    {
        var result = await runner.ReadAsync(accountId, fetch, HttpContext.RequestAborted);
        if (result.Problem is { } problem) return Problem(problem);
        return result.Value is null ? NoContent() : Ok(result.Value);
    }

    /// <summary>An uploaded file's bytes, for the uploads VRChat takes as one request.</summary>
    [NonAction]
    protected static async Task<byte[]> ReadAllAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
