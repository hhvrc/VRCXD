using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Services.Sessions;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.API.Controller.Avatars;

/// <summary>
/// Avatars, as stored here.
/// </summary>
[ApiController]
[Tags("Avatars")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/avatars")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed partial class AvatarsController : VrcxControllerBase
{
    private readonly VrcxContext _db;
    private readonly IAccountSessionManager _sessions;
    private readonly IAccountSyncService _sync;

    public AvatarsController(VrcxContext db, IAccountSessionManager sessions, IAccountSyncService sync)
    {
        _db = db;
        _sessions = sessions;
        _sync = sync;
    }

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
        if (refresh)
        {
            var via = PickAccount(account, _sessions);
            if (via is null) return Problem(AccountError.NoAccounts);
            try
            {
                await _sync.RefreshAvatarAsync(via, avatarId, cancellationToken);
            }
            catch (VrchatApiException ex)
            {
                return VrchatProblem(ex);
            }
        }

        var avatar = await _db.Avatars.AsNoTracking().Include(a => a.Author).FirstOrDefaultAsync(a => a.Id == avatarId, cancellationToken);
        if (avatar is null) return Problem(AvatarError.AvatarNotFound);
        return Ok(AvatarResponse.From(avatar));
    }
}
