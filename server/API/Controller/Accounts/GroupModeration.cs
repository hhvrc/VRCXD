using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.Vrchat.Models;

namespace VRCX.API.Controller.Accounts;

/// <summary>Accept or reject a join request; block also blocks the user from asking again (reject only).</summary>
public sealed record GroupJoinRequestResponseBody(string Action, bool Block = false);

// The original's GroupMemberModerationDialog: members, bans, sent invites, join requests, blocked
// requests and the audit log, and the bulk actions on the selected users. Each call is one VRChat
// call, as the original makes them; the paging loops and the progress stay in the browser.
public sealed partial class AccountsController
{
    /// <summary>One member of a group, or 204 when the user is not one.</summary>
    /// <response code="200">The membership</response>
    /// <response code="204">VRChat has no membership for this user</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{groupId}/members/{userId}")]
    [ProducesResponseType<GroupMember>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetGroupMember([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetGroupMemberAsync(groupId, userId, cancellationToken));

    /// <summary>Search a group's members by name, VRChat's own index.</summary>
    /// <response code="200">The matches</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{groupId}/members/search")]
    [ProducesResponseType<GroupMemberSearchResult>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SearchGroupMembers([FromRoute] string accountId, [FromRoute] string groupId, [FromQuery] string query, CancellationToken cancellationToken, [FromQuery] int offset = 0, [FromQuery] int n = 100) =>
        ReadLiveAsync(accountId, client => client.SearchGroupMembersAsync(groupId, query, Math.Clamp(n, 1, 100), Math.Max(offset, 0), cancellationToken));

    /// <summary>Change a member: the manager note, visibility or announcement subscriptions. Fields left out are kept.</summary>
    /// <response code="200">The member after</response>
    /// <response code="409">No live session</response>
    [HttpPut("{accountId}/groups/{groupId}/members/{userId}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<GroupMember>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> UpdateGroupMember([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string userId, [FromBody] GroupMemberUpdate body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.UpdateGroupMemberAsync(groupId, userId, body, cancellationToken));

    /// <summary>Remove a member from a group: the original's Kick, and Delete Blocked Requests.</summary>
    /// <response code="204">Removed</response>
    /// <response code="409">No live session</response>
    [HttpDelete("{accountId}/groups/{groupId}/members/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> RemoveGroupMember([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.RemoveGroupMemberAsync(groupId, userId, cancellationToken));

    /// <summary>Give a member a role.</summary>
    /// <response code="200">The member's role ids after</response>
    /// <response code="409">No live session</response>
    [HttpPut("{accountId}/groups/{groupId}/members/{userId}/roles/{roleId}")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AddGroupMemberRole([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string userId, [FromRoute] string roleId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.AddGroupMemberRoleAsync(groupId, userId, roleId, cancellationToken));

    /// <summary>Take a role from a member.</summary>
    /// <response code="200">The member's role ids after</response>
    /// <response code="409">No live session</response>
    [HttpDelete("{accountId}/groups/{groupId}/members/{userId}/roles/{roleId}")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> RemoveGroupMemberRole([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string userId, [FromRoute] string roleId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.RemoveGroupMemberRoleAsync(groupId, userId, roleId, cancellationToken));

    /// <summary>A page of a group's bans.</summary>
    /// <response code="200">The banned memberships</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{groupId}/bans")]
    [ProducesResponseType<IReadOnlyList<GroupMember>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetGroupBans([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken, [FromQuery] int offset = 0, [FromQuery] int n = 100) =>
        ReadLiveAsync(accountId, client => client.GetGroupBansAsync(groupId, Math.Clamp(n, 1, 100), Math.Max(offset, 0), cancellationToken));

    /// <summary>Ban a user from a group.</summary>
    /// <response code="200">The banned membership</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/groups/{groupId}/bans/{userId}")]
    [ProducesResponseType<GroupMember>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> BanGroupMember([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.BanGroupMemberAsync(groupId, userId, cancellationToken));

    /// <summary>Lift a group ban.</summary>
    /// <response code="204">Unbanned</response>
    /// <response code="409">No live session</response>
    [HttpDelete("{accountId}/groups/{groupId}/bans/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> UnbanGroupMember([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.UnbanGroupMemberAsync(groupId, userId, cancellationToken));

    /// <summary>A page of the invites a group has sent.</summary>
    /// <response code="200">The invited memberships</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{groupId}/invites")]
    [ProducesResponseType<IReadOnlyList<GroupMember>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetGroupInvites([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken, [FromQuery] int offset = 0, [FromQuery] int n = 100) =>
        ReadLiveAsync(accountId, client => client.GetGroupInvitesAsync(groupId, Math.Clamp(n, 1, 100), Math.Max(offset, 0), cancellationToken));

    /// <summary>Take back an invite the group sent.</summary>
    /// <response code="204">Deleted</response>
    /// <response code="409">No live session</response>
    [HttpDelete("{accountId}/groups/{groupId}/invites/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> DeleteGroupInvite([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string userId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.DeleteGroupInviteAsync(groupId, userId, cancellationToken));

    /// <summary>A page of a group's join requests, or with blocked=true the blocked ones.</summary>
    /// <response code="200">The requesting memberships</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{groupId}/requests")]
    [ProducesResponseType<IReadOnlyList<GroupMember>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetGroupJoinRequests([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken, [FromQuery] bool blocked = false, [FromQuery] int offset = 0, [FromQuery] int n = 100) =>
        ReadLiveAsync(accountId, client => client.GetGroupJoinRequestsAsync(groupId, blocked, Math.Clamp(n, 1, 100), Math.Max(offset, 0), cancellationToken));

    /// <summary>Answer a join request: accept, reject, or reject and block.</summary>
    /// <response code="204">Answered</response>
    /// <response code="400">The action is neither accept nor reject</response>
    /// <response code="409">No live session</response>
    [HttpPut("{accountId}/groups/{groupId}/requests/{userId}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> RespondGroupJoinRequest([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string userId, [FromBody] GroupJoinRequestResponseBody body, CancellationToken cancellationToken)
    {
        if (body.Action is not ("accept" or "reject"))
        {
            ModelState.AddModelError(nameof(body.Action), "The action must be accept or reject");
            return Task.FromResult<IActionResult>(ValidationProblem(ModelState));
        }
        return RunAsync(accountId, client => client.RespondGroupJoinRequestAsync(groupId, userId, body.Action, body.Block, cancellationToken));
    }

    /// <summary>The audit log event types the group has, for the Logs filter.</summary>
    /// <response code="200">The event types</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{groupId}/audit-log-types")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetGroupAuditLogTypes([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetGroupAuditLogTypesAsync(groupId, cancellationToken));

    /// <summary>A page of a group's audit log, optionally only some event types (comma separated).</summary>
    /// <response code="200">The page, with whether there is a next one</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{groupId}/audit-logs")]
    [ProducesResponseType<GroupAuditLogPage>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetGroupAuditLogs([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken, [FromQuery] string? eventTypes = null, [FromQuery] int offset = 0, [FromQuery] int n = 100) =>
        ReadLiveAsync(accountId, client => client.GetGroupAuditLogsAsync(groupId, Math.Clamp(n, 1, 100), Math.Max(offset, 0), eventTypes, cancellationToken));
}
