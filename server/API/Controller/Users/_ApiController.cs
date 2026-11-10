using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Services.Sessions;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Controller.Users;

/// <summary>
/// VRChat users as stored here: the latest state plus the history of every change seen.
/// </summary>
[ApiController]
[Tags("Users")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/users")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed partial class UsersController : VrcxControllerBase
{
    private readonly VrcxContext _db;
    private readonly IAccountSessionManager _sessions;
    private readonly IAccountSyncService _sync;

    public UsersController(VrcxContext db, IAccountSessionManager sessions, IAccountSyncService sync)
    {
        _db = db;
        _sessions = sessions;
        _sync = sync;
    }
}
