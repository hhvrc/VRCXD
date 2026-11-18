using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Services.Sessions;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.API.Controller.Accounts;

/// <summary>
/// The VRChat accounts logged into this server, and what is known about their friends.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed partial class AccountsController : VrcxControllerBase
{
    private readonly VrcxContext _db;
    private readonly IAccountSessionManager _sessions;
    private readonly IAccountSyncService _sync;
    private readonly SavedCredentialStore _credentials;
    private readonly ILogger<AccountsController> _logger;

    public AccountsController(VrcxContext db, IAccountSessionManager sessions, IAccountSyncService sync, SavedCredentialStore credentials, ILogger<AccountsController> logger)
    {
        _db = db;
        _sessions = sessions;
        _sync = sync;
        _credentials = credentials;
        _logger = logger;
    }

    /// <summary>Runs an action on VRChat with the account's own session and answers 204, or the problem it failed with.</summary>
    private async Task<IActionResult> RunAsync(string accountId, Func<VrchatApiClient, Task> action)
    {
        var client = _sessions.Get(accountId);
        if (client is null) return Problem(AccountError.NoLiveSession);
        try
        {
            await action(client);
        }
        catch (VrchatUnauthorizedException)
        {
            await _sessions.InvalidateAsync(accountId, HttpContext.RequestAborted);
            return Problem(VrchatError.SessionExpired);
        }
        catch (VrchatApiException ex)
        {
            return VrchatProblem(ex);
        }
        return NoContent();
    }
}
