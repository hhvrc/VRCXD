using System.Net.Mime;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using VRCX.Common.VrcxDb;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.Vrchat;
using VRCX.API.Models.Response;

namespace VRCX.API.Controller.Accounts;

public sealed record CreateInstanceBody(
    string WorldId,
    string Type,
    bool CanRequestInvite,
    string Region,
    string? GroupId,
    string? GroupAccessType,
    bool QueueEnabled,
    IReadOnlyList<string>? RoleIds,
    string? MinimumAvatarPerformance,
    bool AgeGate,
    string? DisplayName,
    string? Description = null,
    IReadOnlyList<string>? Languages = null,
    string? CategoryId = null,
    IReadOnlyList<string>? VibeIds = null);

public sealed record SelfInviteBody(string Location, string? ShortName);
public sealed record InstanceLocationBody(string Location);
public sealed record RenameInstanceBody(string Location, string? DisplayName);

/// <summary>VRChat's instance as it sends it, and this account's permissions in the group owning it.</summary>
public sealed class LiveInstanceResponse
{
    public required JsonElement Instance { get; init; }
    /// <summary>Null unless a group owns the instance.</summary>
    public IReadOnlyList<string>? GroupPermissions { get; init; }
    /// <summary>The account's game is in this instance, by its desktop app (announcements need that).</summary>
    public bool InThisInstance { get; init; }
    /// <summary>When the account last joined this instance in the past week, by its game log (the original's Last Join).</summary>
    public DateTimeOffset? LastJoinedAt { get; init; }
}

public sealed record InstanceCalendarLinkBody(string Location, string? CalendarEntryId);
/// <summary>ImageUrl is a VRChat file URL (a gallery photo); without one the group's banner is used, as in the original.</summary>
public sealed record InstanceAnnouncementBody(string Location, string Message, string? ImageUrl);

// The original's New Instance, Launch and Invite dialogs: everything they ask VRChat for.
public sealed partial class AccountsController
{
    private static readonly string[] InstanceCreatePermissions = ["group-instance-public-create", "group-instance-plus-create", "group-instance-open-create"];

    /// <summary>What the New Instance dialog offers this account: its groups with their permissions, and whether it has VRC+.</summary>
    /// <response code="200">The options</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/instances/options")]
    [ProducesResponseType<InstanceOptionsResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetInstanceOptions([FromRoute] string accountId, [FromServices] VRCX.Common.Services.Presence.IPresenceCache presence, CancellationToken cancellationToken)
    {
        var own = await presence.GetManyAsync([accountId], cancellationToken);
        var currentLocation = own.TryGetValue(accountId, out var mine) ? mine.CurrentInstanceLocation : null;
        return await ReadLiveAsync(accountId, async client =>
        {
            var me = await client.GetCurrentUserAsync(cancellationToken);
            var groups = await client.GetUserGroupsAsync(accountId, cancellationToken);
            var permissions = await client.GetGroupPermissionsAsync(accountId, cancellationToken);
            return new InstanceOptionsResponse
            {
                DisplayName = me.DisplayName,
                IsSupporter = me.Tags.Contains("system_supporter"),
                AgeVerified = me.AgeVerificationStatus == "18+",
                CurrentLocation = currentLocation,
                Groups = groups.Select(g =>
                {
                    IReadOnlyList<string> allowed = permissions.TryGetValue(g.GroupId, out var held) ? held : [];
                    return new InstanceOptionGroup
                    {
                        Id = g.GroupId,
                        Name = g.Name,
                        IconUrl = g.IconUrl,
                        Privacy = g.Privacy,
                        Permissions = allowed,
                        CanCreateInstance = allowed.Contains("*") || InstanceCreatePermissions.Any(allowed.Contains),
                    };
                }).ToList(),
            };
        });
    }

    /// <summary>A group's roles, for a members-only instance.</summary>
    /// <response code="200">The roles</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{groupId}/roles")]
    [ProducesResponseType<IReadOnlyList<VRCX.Vrchat.Models.GroupRole>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListGroupRoles([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetGroupRolesAsync(groupId, cancellationToken));

    /// <summary>Create an instance. The owner is this account, or the group for a group instance.</summary>
    /// <response code="200">The instance</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/instances")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<VRCX.Vrchat.Models.Instance>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> CreateInstance([FromRoute] string accountId, [FromBody] CreateInstanceBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var request = new Dictionary<string, object>
            {
                ["type"] = body.Type,
                ["canRequestInvite"] = body.CanRequestInvite,
                ["worldId"] = body.WorldId,
                ["ownerId"] = accountId,
                ["region"] = body.Region,
            };
            if (body.Type == "group")
            {
                request["ownerId"] = body.GroupId ?? "";
                request["groupAccessType"] = body.GroupAccessType ?? "plus";
                request["queueEnabled"] = body.QueueEnabled;
                if (body.GroupAccessType == "members") request["roleIds"] = body.RoleIds ?? [];
                // The caller checks the group's age-gated create permission.
                if (body.AgeGate) request["ageGate"] = true;
            }
            // Any other instance can be age gated by an 18+ verified creator.
            else if (body.AgeGate && (await client.GetCurrentUserAsync(cancellationToken)).AgeVerificationStatus == "18+")
                request["ageGate"] = true;
            if (!string.IsNullOrEmpty(body.MinimumAvatarPerformance)) request["minimumAvatarPerformance"] = body.MinimumAvatarPerformance;
            if (!string.IsNullOrEmpty(body.DisplayName)) request["displayName"] = body.DisplayName;
            if (!string.IsNullOrEmpty(body.Description)) request["description"] = body.Description;
            if (body.Languages is { Count: > 0 } languages) request["languages"] = languages.Take(3).ToList();
            if (!string.IsNullOrEmpty(body.CategoryId)) request["categoryId"] = body.CategoryId;
            if (body.VibeIds is { Count: > 0 } vibeIds) request["vibeIds"] = vibeIds.Take(3).ToList();
            return await client.CreateInstanceAsync(request, cancellationToken);
        });

    /// <summary>The instance categories VRChat offers (instanceCategories), for New Instance and the instance tooltip.</summary>
    /// <response code="200">The categories, kept as VRChat sends them</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/instances/categories")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetInstanceCategories([FromRoute] string accountId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetInstanceCategoriesAsync(cancellationToken));

    /// <summary>The instance vibes VRChat offers (instanceVibes), for New Instance and the instance tooltip.</summary>
    /// <response code="200">The vibes, kept as VRChat sends them</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/instances/vibes")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetInstanceVibes([FromRoute] string accountId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetInstanceVibesAsync(cancellationToken));

    /// <summary>An instance's short and secure names, which a launch link needs for a closed instance.</summary>
    /// <response code="200">The names, kept as VRChat sends them</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/instances/short-name")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetInstanceShortName([FromRoute] string accountId, [FromQuery] string location, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetInstanceShortNameAsync(location, cancellationToken));

    /// <summary>
    /// An instance as VRChat sends it now, for the original's InstanceActionBar: player and platform counts,
    /// languages, queue, disabled content, hype train; with the owning group's permissions for Close and Rename.
    /// </summary>
    /// <response code="200">The instance</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/instances/live")]
    [ProducesResponseType<LiveInstanceResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetLiveInstance([FromRoute] string accountId, [FromQuery] string location, [FromServices] VRCX.Common.Services.Automation.AccountAutomation automation, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var instance = await client.GetInstanceJsonAsync(location, cancellationToken);
            var owner = instance.TryGetProperty("ownerId", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : null;
            IReadOnlyList<string>? permissions = null;
            if (owner?.StartsWith("grp_", StringComparison.Ordinal) == true)
                permissions = (await client.GetGroupPermissionsAsync(accountId, cancellationToken)).TryGetValue(owner, out var held) ? held : [];
            var here = await automation.GameLocationAsync(accountId, cancellationToken);
            var weekAgo = DateTimeOffset.UtcNow.AddDays(-7);
            var lastJoined = await _db.InstanceVisits.AsNoTracking()
                .Where(v => v.UserId == accountId && v.Source == VisitSource.GameLog && v.InstanceLocation == location && v.JoinedAt > weekAgo)
                .MaxAsync(v => (DateTimeOffset?)v.JoinedAt, cancellationToken);
            return new LiveInstanceResponse { Instance = instance, GroupPermissions = permissions, InThisInstance = here is not null && here == location, LastJoinedAt = lastJoined };
        });

    /// <summary>The instance join queues this account is waiting in, oldest first (the original's queuedInstances).</summary>
    /// <response code="200">The queues</response>
    [HttpGet("{accountId}/instance-queue")]
    [ProducesResponseType<IReadOnlyList<VRCX.Common.Services.Live.QueuedInstance>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public IActionResult ListInstanceQueues([FromRoute] string accountId, [FromServices] VRCX.Common.Services.Live.InstanceQueues queues) =>
        Ok(queues.List(accountId));

    /// <summary>Link a group calendar event to an instance, or unlink with null (the original's Calendar Link).</summary>
    /// <response code="200">The instance, as VRChat sends it</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPut("{accountId}/instances/calendar-event")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> LinkInstanceCalendarEvent([FromRoute] string accountId, [FromBody] InstanceCalendarLinkBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.LinkInstanceCalendarEventAsync(body.Location, string.IsNullOrEmpty(body.CalendarEntryId) ? null : body.CalendarEntryId, cancellationToken));

    /// <summary>Send an announcement to everyone in a group instance (the original's Instance Announcement).</summary>
    /// <response code="204">Sent</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/instances/announcement")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> AnnounceInInstance([FromRoute] string accountId, [FromBody] InstanceAnnouncementBody body, CancellationToken cancellationToken)
    {
        var imageUrl = body.ImageUrl;
        if (string.IsNullOrEmpty(imageUrl) && VRCX.Vrchat.VrchatLocation.Parse(body.Location).GroupId is { } groupId)
            imageUrl = await _db.Groups.AsNoTracking().Where(g => g.Id == groupId).Select(g => g.BannerUrl).FirstOrDefaultAsync(cancellationToken);
        var imageId = VrchatApiClient.ExtractFileId(imageUrl);
        var imageVersion = imageId is null ? null : System.Text.RegularExpressions.Regex.Match(imageUrl!, @"/file_[0-9A-Za-z-]+/([0-9]+)") is { Success: true } m ? m.Groups[1].Value : null;
        return await RunAsync(accountId, client => client.AnnounceInInstanceAsync(body.Location, "Instance Announcement", body.Message.Trim(), imageId, imageVersion, cancellationToken));
    }

    /// <summary>Close an instance this account owns or moderates (the original's Close Instance, not a hard close).</summary>
    /// <response code="200">The closed instance, as VRChat sends it</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/instances/close")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> CloseInstance([FromRoute] string accountId, [FromBody] InstanceLocationBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.CloseInstanceAsync(body.Location, cancellationToken));

    /// <summary>Rename an instance (its display name; empty clears it), as the original's Rename Instance.</summary>
    /// <response code="200">The instance, as VRChat sends it</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPut("{accountId}/instances/display-name")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> RenameInstance([FromRoute] string accountId, [FromBody] RenameInstanceBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.RenameInstanceAsync(body.Location, string.IsNullOrWhiteSpace(body.DisplayName) ? null : body.DisplayName.Trim(), cancellationToken));

    /// <summary>Ask VRChat to invite this account to an instance.</summary>
    /// <response code="204">Sent</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/instances/self-invite")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SelfInvite([FromRoute] string accountId, [FromBody] SelfInviteBody body, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.SelfInviteAsync(body.Location, body.ShortName, cancellationToken));
}
