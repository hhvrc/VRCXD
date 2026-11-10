using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VRCX.Common.Authentication;
using VRCX.Common.VrcxDb;
using VRCX.Common;

namespace VRCX.API.Controller.Status;

/// <summary>The server's database, read only.</summary>
[ApiController]
[Tags("Status")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/database")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class DatabaseController(VrcxContext db) : VrcxControllerBase
{
    /// <summary>
    /// Row counts for the original's Settings, Advanced, Database panel (its SQLite table sizes), over
    /// the tables that hold the same history here, for every account on the server. GPS and
    /// online/offline are presence events; location is the accounts' own stays and join/leave everyone
    /// else's; friend log history is friendships started and ended plus friend requests.
    /// </summary>
    /// <response code="200">The counts</response>
    [HttpGet("table-sizes")]
    [ProducesResponseType<TableSizesResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> TableSizes(CancellationToken cancellationToken)
    {
        var accountUsers = db.Accounts.Select(a => a.UserId);
        // One query at a time: a DbContext does not run them in parallel.
        var gps = await db.PresenceEvents.LongCountAsync(e => e.Kind == PresenceKind.Location, cancellationToken);
        var onlineOffline = await db.PresenceEvents.LongCountAsync(e => e.Kind == PresenceKind.Online || e.Kind == PresenceKind.Offline, cancellationToken);
        var status = await db.UserStatusChanges.LongCountAsync(cancellationToken);
        var bio = await db.UserBioChanges.LongCountAsync(cancellationToken);
        var avatar = await db.UserAvatarChanges.LongCountAsync(cancellationToken);
        var friendships = await db.Friendships.LongCountAsync(cancellationToken);
        var ended = await db.Friendships.LongCountAsync(f => f.EndedAt != null, cancellationToken);
        var requests = await db.FriendRequestEvents.LongCountAsync(cancellationToken);
        var notification = await db.Notifications.LongCountAsync(cancellationToken);
        var location = await db.InstanceVisits.LongCountAsync(v => accountUsers.Contains(v.UserId), cancellationToken);
        var joinLeave = await db.InstanceVisits.LongCountAsync(v => !accountUsers.Contains(v.UserId), cancellationToken);
        var portalSpawn = await db.PortalSpawns.LongCountAsync(cancellationToken);
        var videoPlay = await db.VideoPlays.LongCountAsync(cancellationToken);
        var gameEvent = await db.GameLogEvents.LongCountAsync(e => e.Kind == "Event", cancellationToken);
        var external = await db.GameLogEvents.LongCountAsync(e => e.Kind == "External", cancellationToken);
        return Ok(new TableSizesResponse
        {
            Gps = gps,
            Status = status,
            Bio = bio,
            Avatar = avatar,
            OnlineOffline = onlineOffline,
            FriendLogHistory = friendships + ended + requests,
            Notification = notification,
            Location = location,
            JoinLeave = joinLeave,
            PortalSpawn = portalSpawn,
            VideoPlay = videoPlay,
            Event = gameEvent,
            External = external,
        });
    }
}

/// <summary>The original's table size names, with the server's counts.</summary>
public sealed class TableSizesResponse
{
    public required long Gps { get; init; }
    public required long Status { get; init; }
    public required long Bio { get; init; }
    public required long Avatar { get; init; }
    public required long OnlineOffline { get; init; }
    public required long FriendLogHistory { get; init; }
    public required long Notification { get; init; }
    public required long Location { get; init; }
    public required long JoinLeave { get; init; }
    public required long PortalSpawn { get; init; }
    public required long VideoPlay { get; init; }
    public required long Event { get; init; }
    public required long External { get; init; }
}
