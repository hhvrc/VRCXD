using System.Net;
using System.Net.Mime;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.Services.Ingestion;
using VRCX.Vrchat;

namespace VRCX.API.Controller.Groups;

public sealed record GroupRepresentationBody(bool IsRepresenting);

// The group pop-up, and turning an avatar picture back into the avatar it shows.
public sealed partial class AccountGroupsController
{
    /// <summary>A group as VRChat has it now, its open instances, and this account's history with it.</summary>
    /// <response code="200">The details</response>
    /// <response code="404">VRChat has no such group</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{groupId}/details")]
    [ProducesResponseType<GroupDetailsResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetGroupDetails([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken)
    {
        var (joinCount, lastVisit) = await groupQueries.VisitsAsync(accountId, groupId, cancellationToken);

        return await ReadLiveAsync(accountId, async client =>
        {
            var group = await client.GetGroupAsync(groupId, true, cancellationToken)
                ?? throw new VrchatApiException(HttpStatusCode.NotFound, "GET", $"groups/{groupId}", "Group not found");
            JsonElement? instances = null;
            try
            {
                instances = await client.GetGroupInstancesAsync(accountId, groupId, cancellationToken);
            }
            catch (VrchatApiException ex) when (ex is not VrchatUnauthorizedException)
            {
                // Hidden from this account. The rest of the group still shows.
            }
            return new GroupDetailsResponse { Group = group, Instances = instances, JoinCount = joinCount, LastVisit = lastVisit };
        });
    }

    /// <summary>A page of a group's posts, newest first.</summary>
    /// <response code="200">VRChat's answer, unchanged</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{groupId}/posts")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetGroupPosts([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken, [FromQuery] int offset = 0, [FromQuery] int n = 100) =>
        ReadLiveAsync(accountId, client => client.GetGroupPostsAsync(groupId, Math.Clamp(n, 1, 100), Math.Max(offset, 0), cancellationToken));

    /// <summary>A page of a group's members, newest first unless sort says otherwise, optionally only one role's.</summary>
    /// <response code="200">The members</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{groupId}/members")]
    [ProducesResponseType<IReadOnlyList<VRCX.Vrchat.Models.GroupMember>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetGroupMembers([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken, [FromQuery] int offset = 0, [FromQuery] int n = 100, [FromQuery] string? sort = null, [FromQuery] string? roleId = null) =>
        ReadLiveAsync(accountId, client => client.GetGroupMembersAsync(groupId, Math.Clamp(n, 1, 100), Math.Max(offset, 0), string.IsNullOrEmpty(sort) ? null : sort, string.IsNullOrEmpty(roleId) ? null : roleId, cancellationToken));

    /// <summary>Invite a user to a group, the original's InviteGroupDialog sends one of these per user.</summary>
    /// <response code="204">Invited</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/groups/{groupId}/invites/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SendGroupInvite([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.SendGroupInviteAsync(groupId, userId, cancellationToken));

    /// <summary>Leave a group.</summary>
    /// <response code="204">Left</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/groups/{groupId}/leave")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> LeaveGroup([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.LeaveGroupAsync(groupId, cancellationToken));

    /// <summary>Start or stop representing a group.</summary>
    /// <response code="204">Saved</response>
    /// <response code="409">No live session</response>
    [HttpPut("{accountId}/groups/{groupId}/representation")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SetGroupRepresentation([FromRoute] string accountId, [FromRoute] string groupId, [FromBody] GroupRepresentationBody body, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.SetGroupRepresentationAsync(groupId, body.IsRepresenting, cancellationToken));

    /// <summary>VRChat's spoken language options, code to name: the original's subsetOfLanguages, which the group pop-up, Edit Group and the event editor name languages with.</summary>
    /// <response code="200">The options</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/languages")]
    [ProducesResponseType<IReadOnlyDictionary<string, string>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetLanguageOptions([FromRoute] string accountId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client => (IReadOnlyDictionary<string, string>)VrchatConfig.SpokenLanguages(await client.GetConfigAsync(cancellationToken)));

    /// <summary>VRChat's group limits from its config (constants.GROUPS), as the original's groups tab reads them; VRChat's defaults when the config leaves one out.</summary>
    /// <response code="200">The limits</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/limits")]
    [ProducesResponseType<GroupLimits>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetGroupLimits([FromRoute] string accountId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client => GroupLimits.FromConfig(await client.GetConfigAsync(cancellationToken)));

    /// <summary>Delete a group, the sole owner's Delete Group in place of Leave Group. VRChat soft-deletes it.</summary>
    /// <response code="204">Deleted</response>
    /// <response code="409">No live session</response>
    [HttpDelete("{accountId}/groups/{groupId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> DeleteGroup([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.DeleteGroupAsync(groupId, cancellationToken));

    /// <summary>The group's events from a day ago on (up to 100), for the instance calendar link dialog.</summary>
    /// <response code="200">The events as VRChat has them</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{groupId}/calendar/recent")]
    [ProducesResponseType<IReadOnlyList<JsonElement>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetRecentGroupCalendar([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var page = await client.GetGroupCalendarAfterAsync(groupId, DateTimeOffset.UtcNow.AddDays(-1), 100, cancellationToken);
            return page.ValueKind == JsonValueKind.Object && page.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
                ? (IReadOnlyList<JsonElement>)results.EnumerateArray().Select(e => e.Clone()).ToList()
                : [];
        });

    /// <summary>The group's calendar for its Upcoming and Past Events: each event fetched again, as the original does, for this account's interest in it.</summary>
    /// <response code="200">The events as VRChat has them</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{groupId}/calendar")]
    [ProducesResponseType<IReadOnlyList<JsonElement>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetGroupCalendar([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var page = await client.GetGroupCalendarAsync(groupId, cancellationToken);
            if (page.ValueKind != JsonValueKind.Object || !page.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return (IReadOnlyList<JsonElement>)[];
            var events = await Task.WhenAll(results.EnumerateArray().Select(async item =>
            {
                if (!item.TryGetProperty("id", out var id) || id.GetString() is not { Length: > 0 } eventId) return item;
                try
                {
                    return await client.GetCalendarEventAsync(groupId, eventId, cancellationToken);
                }
                catch (VrchatApiException ex) when (ex is not VrchatUnauthorizedException)
                {
                    // The list's copy, without the interest, as the original keeps it when the fetch fails.
                    return item;
                }
            }));
            return (IReadOnlyList<JsonElement>)events;
        });
}
