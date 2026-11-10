using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;

namespace VRCX.API.Controller.Accounts;

/// <summary>Create Group's fields. Empty image ids are left out, as the original sends undefined for them.</summary>
public sealed class CreateGroupBody
{
    [Required][StringLength(64, MinimumLength = 3)] public required string Name { get; set; }
    [Required][RegularExpression("^[A-Z0-9]{3,6}$")] public required string ShortCode { get; set; }
    [StringLength(250)] public string Description { get; set; } = "";
    [Required][RegularExpression("^(open|request|invite|closed)$")] public required string JoinState { get; set; }
    public string? BannerId { get; set; }
    public string? IconId { get; set; }
    [Required][RegularExpression("^(default|private)$")] public required string Privacy { get; set; }
    [Required][RegularExpression("^(default|managedFree|managedInvite|managedRequest)$")] public required string RoleTemplate { get; set; }
}

/// <summary>Edit Group's fields. Empty image ids are left out, as the original sends undefined for them.</summary>
public sealed class UpdateGroupBody
{
    [Required][StringLength(64, MinimumLength = 3)] public required string Name { get; set; }
    [Required][RegularExpression("^[A-Z0-9]{3,6}$")] public required string ShortCode { get; set; }
    [StringLength(250)] public string Description { get; set; } = "";
    [Required][RegularExpression("^(open|request|invite|closed)$")] public required string JoinState { get; set; }
    public string? BannerId { get; set; }
    public string? IconId { get; set; }
    [MaxLength(3)] public List<string> Languages { get; set; } = [];
    [StringLength(2048)] public string Rules { get; set; } = "";
    [MaxLength(3)] public List<string> Links { get; set; } = [];
    public bool AllowGroupJoinPrompt { get; set; }
}

// The original's GroupEditDialog: Create Group and Edit Group, each one VRChat call with the body in
// the original's order (its editGroup also sends the group id in the body).
public sealed partial class AccountsController
{
    /// <summary>The role templates Create Group offers, as VRChat sends them.</summary>
    /// <response code="200">VRChat's answer</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/role-templates")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetGroupRoleTemplates([FromRoute] string accountId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetGroupRoleTemplatesAsync(cancellationToken));

    /// <summary>Create a group.</summary>
    /// <response code="200">The new group, as VRChat sends it</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/groups")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> CreateGroup([FromRoute] string accountId, [FromBody] CreateGroupBody body, CancellationToken cancellationToken)
    {
        var json = GroupCommon(null, body.Name, body.ShortCode, body.Description, body.JoinState, body.BannerId, body.IconId);
        json["privacy"] = body.Privacy;
        json["roleTemplate"] = body.RoleTemplate;
        return ReadLiveAsync(accountId, client => client.CreateGroupAsync(json, cancellationToken));
    }

    /// <summary>Edit a group.</summary>
    /// <response code="200">The group after, as VRChat sends it</response>
    /// <response code="409">No live session</response>
    [HttpPut("{accountId}/groups/{groupId}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> UpdateGroup([FromRoute] string accountId, [FromRoute] string groupId, [FromBody] UpdateGroupBody body, CancellationToken cancellationToken)
    {
        var json = GroupCommon(groupId, body.Name, body.ShortCode, body.Description, body.JoinState, body.BannerId, body.IconId);
        json["languages"] = new JsonArray(body.Languages.Select(l => (JsonNode?)l).ToArray());
        json["rules"] = body.Rules;
        json["links"] = new JsonArray(body.Links.Select(l => (JsonNode?)l).ToArray());
        json["allowGroupJoinPrompt"] = body.AllowGroupJoinPrompt;
        return ReadLiveAsync(accountId, client => client.UpdateGroupAsync(groupId, json, cancellationToken));
    }

    private static JsonObject GroupCommon(string? id, string name, string shortCode, string description, string joinState, string? bannerId, string? iconId)
    {
        var json = new JsonObject();
        if (id is not null) json["id"] = id;
        foreach (var (key, value) in new (string, string)[] { ("name", name), ("shortCode", shortCode), ("description", description), ("joinState", joinState) })
            json[key] = value;
        if (!string.IsNullOrEmpty(bannerId)) json["bannerId"] = bannerId;
        if (!string.IsNullOrEmpty(iconId)) json["iconId"] = iconId;
        return json;
    }
}
