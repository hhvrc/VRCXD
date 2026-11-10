using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Vrchat;

namespace VRCX.API.Controller.Users;

public sealed partial class UsersController
{
    /// <summary>
    /// A user as last seen. With refresh=true, GET users/{id} is fetched from VRChat first (through the given account's session, or any live one).
    /// </summary>
    /// <param name="userId">usr_…</param>
    /// <param name="refresh">Fetch from VRChat before answering</param>
    /// <param name="account">Which logged-in account's session to fetch with</param>
    /// <param name="presence">Where the user is right now.</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">The user</response>
    /// <response code="404">Never seen, or VRChat has no such user</response>
    /// <response code="409">refresh=true but no account is logged in</response>
    [HttpGet("{userId}")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetUser([FromRoute] string userId, [FromQuery] bool refresh, [FromQuery] string? account, [FromServices] VRCX.Common.Services.Presence.IPresenceCache presence, CancellationToken cancellationToken)
    {
        if (refresh)
        {
            var via = PickAccount(account, _sessions);
            if (via is null) return Problem(AccountError.NoAccounts);
            try
            {
                await _sync.RefreshUserAsync(via, userId, cancellationToken);
            }
            catch (VrchatApiException ex)
            {
                return VrchatProblem(ex);
            }
        }

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return Problem(UserError.UserNotFound);
        return Ok(UserResponse.From(user, await VRCX.Common.Services.Presence.PresenceCacheExtensions.GetAsync(presence, userId, cancellationToken)));
    }

    /// <summary>
    /// Everything that changed about a user while we were watching: names, trust, status, bio, avatars, presence, instance visits.
    /// </summary>
    /// <response code="200">The history (each list newest first, capped at 100 entries)</response>
    /// <response code="404">Never seen</response>
    [HttpGet("{userId}/history")]
    [ProducesResponseType<UserHistoryResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetUserHistory([FromRoute] string userId, CancellationToken cancellationToken)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == userId, cancellationToken)) return Problem(UserError.UserNotFound);

        const int cap = 100;
        return Ok(new UserHistoryResponse
        {
            DisplayNames = await _db.UserDisplayNameChanges.AsNoTracking().Where(c => c.UserId == userId).OrderByDescending(c => c.ChangedAt).Take(cap).ToListAsync(cancellationToken),
            Trust = await _db.UserTrustChanges.AsNoTracking().Where(c => c.UserId == userId).OrderByDescending(c => c.ChangedAt).Take(cap).ToListAsync(cancellationToken),
            Status = await _db.UserStatusChanges.AsNoTracking().Where(c => c.UserId == userId).OrderByDescending(c => c.ChangedAt).Take(cap).ToListAsync(cancellationToken),
            Bio = await _db.UserBioChanges.AsNoTracking().Where(c => c.UserId == userId).OrderByDescending(c => c.ChangedAt).Take(cap).ToListAsync(cancellationToken),
            Avatars = await _db.UserAvatarChanges.AsNoTracking().Where(c => c.UserId == userId).OrderByDescending(c => c.ChangedAt).Take(cap).ToListAsync(cancellationToken),
            Presence = await _db.PresenceEvents.AsNoTracking().Where(c => c.UserId == userId).OrderByDescending(c => c.At).Take(cap).ToListAsync(cancellationToken),
            Visits = await _db.InstanceVisits.AsNoTracking().Where(v => v.UserId == userId).OrderByDescending(v => v.JoinedAt).Take(cap)
                .Select(v => new VisitResponse { InstanceLocation = v.InstanceLocation, WorldName = v.Instance.World.Name, JoinedAt = v.JoinedAt, LeftAt = v.LeftAt, Source = v.Source })
                .ToListAsync(cancellationToken),
        });
    }
}
