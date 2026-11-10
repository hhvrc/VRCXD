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

namespace VRCX.API.Controller.Instances;

/// <summary>
/// Instances (one row per location tag) and who is in them.
/// </summary>
[ApiController]
[Tags("Instances")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/instances")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class InstancesController : VrcxControllerBase
{
    private readonly VrcxContext _db;
    private readonly IAccountSessionManager _sessions;
    private readonly IAccountSyncService _sync;

    public InstancesController(VrcxContext db, IAccountSessionManager sessions, IAccountSyncService sync)
    {
        _db = db;
        _sessions = sessions;
        _sync = sync;
    }

    /// <summary>
    /// An instance by its location tag ("wrld_…:12345~region(eu)"), with everyone we believe is there now.
    /// With refresh=true, GET instances/{location} is fetched from VRChat first.
    /// </summary>
    /// <response code="200">The instance</response>
    /// <response code="400">The tag is not an instance location</response>
    /// <response code="404">Never seen, or VRChat has no such instance</response>
    /// <response code="409">refresh=true but no account is logged in</response>
    [HttpGet("{location}")]
    [ProducesResponseType<InstanceResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetInstance([FromServices] VRCX.Common.Services.Presence.IPresenceCache presence, [FromRoute] string location, [FromQuery] bool refresh, [FromQuery] string? account, CancellationToken cancellationToken)
    {
        var parsed = VrchatLocation.Parse(location);
        if (!parsed.IsRealInstance) return Problem(InstanceError.NotAnInstance);

        if (refresh)
        {
            var via = PickAccount(account, _sessions);
            if (via is null) return Problem(AccountError.NoAccounts);
            try
            {
                await _sync.RefreshInstanceAsync(via, parsed.InstanceLocation, cancellationToken);
            }
            catch (VrchatApiException ex)
            {
                return VrchatProblem(ex);
            }
        }

        var key = parsed.InstanceLocation;
        var instance = await _db.Instances.AsNoTracking().Include(i => i.World).FirstOrDefaultAsync(i => i.Location == key, cancellationToken);
        if (instance is null) return Problem(InstanceError.InstanceNotFound);

        // The game log's own record of who it saw, plus who VRChat's pipeline places here right now.
        var seen = await _db.InstanceVisits.AsNoTracking()
            .Where(v => v.InstanceLocation == key && v.Source == VisitSource.GameLog && v.LeftAt == null)
            .Select(v => new InstancePresentResponse { UserId = v.UserId, DisplayName = v.User.DisplayName, JoinedAt = v.JoinedAt, Source = v.Source })
            .ToListAsync(cancellationToken);
        var occupants = await presence.GetOccupantsAsync(key, cancellationToken);
        var occupantIds = occupants.Select(o => o.UserId).ToArray();
        var names = await _db.Users.AsNoTracking().Where(u => occupantIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var present = seen
            .Concat(occupants.Where(o => names.ContainsKey(o.UserId)).Select(o => new InstancePresentResponse
            {
                UserId = o.UserId, DisplayName = names[o.UserId], JoinedAt = o.Since ?? DateTimeOffset.UtcNow, Source = VisitSource.Pipeline,
            }))
            .OrderBy(p => p.JoinedAt)
            .ToList();

        return Ok(new InstanceResponse
        {
            Location = instance.Location, WorldId = instance.WorldId, WorldName = instance.World.Name, InstanceName = instance.InstanceName,
            Type = instance.Type, Region = instance.Region, OwnerUserId = instance.OwnerUserId, GroupId = instance.GroupId,
            GroupAccessType = instance.GroupAccessType, CanRequestInvite = instance.CanRequestInvite, AgeGate = instance.AgeGate,
            Name = instance.Name, DisplayName = instance.DisplayName, Capacity = instance.Capacity, ClosedAt = instance.ClosedAt,
            FirstSeenAt = instance.FirstSeenAt, FetchedAt = instance.FetchedAt, Present = present,
        });
    }
}
