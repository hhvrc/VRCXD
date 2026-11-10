using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Services.Sessions;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;

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
}
