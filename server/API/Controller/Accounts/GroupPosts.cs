using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;

namespace VRCX.API.Controller.Accounts;

/// <summary>A group post's fields, as the original's GroupPostEditDialog sends them.</summary>
public sealed class GroupPostBody
{
    [Required] public required string Title { get; set; }
    [Required] public required string Text { get; set; }
    public List<string> RoleIds { get; set; } = [];
    [Required][RegularExpression("^(public|group)$")] public required string Visibility { get; set; }
    /// <summary>Create only: notify the members.</summary>
    public bool SendNotification { get; set; }
    public string? ImageId { get; set; }
}

// The original's createGroupPost, editGroupPost and deleteGroupPost. Its bodies carry the group id (and
// the post id when editing) as well as the path does.
public sealed partial class AccountsController
{
    /// <summary>Create a group post.</summary>
    /// <response code="200">The new post, as VRChat sends it</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/groups/{groupId}/posts")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> CreateGroupPost([FromRoute] string accountId, [FromRoute] string groupId, [FromBody] GroupPostBody body, CancellationToken cancellationToken)
    {
        var json = new JsonObject
        {
            ["groupId"] = groupId,
            ["title"] = body.Title,
            ["text"] = body.Text,
            ["roleIds"] = Strings(body.RoleIds),
            ["visibility"] = body.Visibility,
            ["sendNotification"] = body.SendNotification,
            ["imageId"] = NullIfEmpty(body.ImageId),
        };
        return ReadLiveAsync(accountId, client => client.CreateGroupPostAsync(groupId, json, cancellationToken));
    }

    /// <summary>Edit a group post.</summary>
    /// <response code="200">The post after, as VRChat sends it</response>
    /// <response code="409">No live session</response>
    [HttpPut("{accountId}/groups/{groupId}/posts/{postId}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> UpdateGroupPost([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string postId, [FromBody] GroupPostBody body, CancellationToken cancellationToken)
    {
        var json = new JsonObject
        {
            ["groupId"] = groupId,
            ["postId"] = postId,
            ["title"] = body.Title,
            ["text"] = body.Text,
            ["roleIds"] = Strings(body.RoleIds),
            ["visibility"] = body.Visibility,
            ["imageId"] = NullIfEmpty(body.ImageId),
        };
        return ReadLiveAsync(accountId, client => client.UpdateGroupPostAsync(groupId, postId, json, cancellationToken));
    }

    /// <summary>Delete a group post.</summary>
    /// <response code="204">Deleted</response>
    /// <response code="409">No live session</response>
    [HttpDelete("{accountId}/groups/{groupId}/posts/{postId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> DeleteGroupPost([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string postId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.DeleteGroupPostAsync(groupId, postId, cancellationToken));

    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)v).ToArray());

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
