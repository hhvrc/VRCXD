using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;

namespace VRCX.API.Controller.Accounts;

/// <summary>
/// A role's fields. Each is optional: the original's GroupRolesDialog leaves out what the role does not
/// let you change (a default role's details, an owner role's flags and permissions), and a reorder sends
/// only the order.
/// </summary>
public sealed class GroupRoleBody
{
    [StringLength(64)] public string? Name { get; set; }
    [StringLength(512)] public string? Description { get; set; }
    public bool? RequiresTwoFactor { get; set; }
    public bool? IsSelfAssignable { get; set; }
    public bool? IsAddedOnJoin { get; set; }
    public bool? RequiresPurchase { get; set; }
    public List<string>? Permissions { get; set; }
    public int? Order { get; set; }
}

// The original's GroupRolesDialog: getGroupPermissionList, createGroupRole, editGroupRole and
// deleteGroupRole. VRChat answers each write with the group's roles.
public sealed partial class AccountsController
{
    /// <summary>Every permission a role in this group can hold, as VRChat sends them.</summary>
    /// <response code="200">VRChat's answer</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{groupId}/permissions")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetGroupPermissionList([FromRoute] string accountId, [FromRoute] string groupId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetGroupPermissionListAsync(groupId, cancellationToken));

    /// <summary>Create a role.</summary>
    /// <response code="200">VRChat's answer</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/groups/{groupId}/roles")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> CreateGroupRole([FromRoute] string accountId, [FromRoute] string groupId, [FromBody] GroupRoleBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.CreateGroupRoleAsync(groupId, RoleJson(body), cancellationToken));

    /// <summary>Edit or reorder a role.</summary>
    /// <response code="200">VRChat's answer</response>
    /// <response code="409">No live session</response>
    [HttpPut("{accountId}/groups/{groupId}/roles/{roleId}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> UpdateGroupRole([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string roleId, [FromBody] GroupRoleBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.UpdateGroupRoleAsync(groupId, roleId, RoleJson(body), cancellationToken));

    /// <summary>Delete a role.</summary>
    /// <response code="200">VRChat's answer</response>
    /// <response code="409">No live session</response>
    [HttpDelete("{accountId}/groups/{groupId}/roles/{roleId}")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> DeleteGroupRole([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string roleId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.DeleteGroupRoleAsync(groupId, roleId, cancellationToken));

    /// <summary>The fields given, in the original's order.</summary>
    private static JsonObject RoleJson(GroupRoleBody body)
    {
        var json = new JsonObject();
        if (body.Name is not null) json["name"] = body.Name;
        if (body.Description is not null) json["description"] = body.Description;
        if (body.RequiresTwoFactor is { } twoFactor) json["requiresTwoFactor"] = twoFactor;
        if (body.IsSelfAssignable is { } selfAssignable) json["isSelfAssignable"] = selfAssignable;
        if (body.IsAddedOnJoin is { } addedOnJoin) json["isAddedOnJoin"] = addedOnJoin;
        if (body.RequiresPurchase is { } purchase) json["requiresPurchase"] = purchase;
        if (body.Permissions is not null) json["permissions"] = Strings(body.Permissions);
        if (body.Order is { } order) json["order"] = order;
        return json;
    }
}
