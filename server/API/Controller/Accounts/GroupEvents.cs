using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;

namespace VRCX.API.Controller.Accounts;

/// <summary>A group event's fields, as the original's GroupEventEditDialog sends them.</summary>
public sealed class GroupEventBody : IValidatableObject
{
    /// <summary>ISO 8601, passed on as written.</summary>
    [Required] public required string StartsAt { get; set; }
    [Required] public required string EndsAt { get; set; }
    [Required][StringLength(64, MinimumLength = 1)] public required string Title { get; set; }
    [Required][RegularExpression("^(group|public)$")] public required string AccessType { get; set; }
    [Required][StringLength(1024, MinimumLength = 1)] public required string Description { get; set; }
    [Required] public required string Category { get; set; }
    [MaxLength(5)] public List<string> Tags { get; set; } = [];
    public string? ImageId { get; set; }
    public List<string> RoleIds { get; set; } = [];
    public List<string> Platforms { get; set; } = [];
    [MaxLength(3)] public List<string> Languages { get; set; } = [];
    public bool SendCreationNotification { get; set; }
    [Range(0, 60)] public int HostEarlyJoinMinutes { get; set; }
    [Range(0, 60)] public int GuestEarlyJoinMinutes { get; set; }
    [Range(0, 60)] public int CloseInstanceAfterEndMinutes { get; set; }
    /// <summary>A new series: frequency, interval, timezone, and daysOfWeek / end when set. Sent with occurrenceKind "series".</summary>
    public JsonObject? Recurrence { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!DateTimeOffset.TryParse(StartsAt, out var starts)) yield return new ValidationResult("startsAt is not a date", [nameof(StartsAt)]);
        else if (!DateTimeOffset.TryParse(EndsAt, out var ends)) yield return new ValidationResult("endsAt is not a date", [nameof(EndsAt)]);
        else if (ends <= starts) yield return new ValidationResult("The event must end after it starts.", [nameof(EndsAt)]);
    }
}

// The original's createGroupEvent, editGroupEvent and deleteGroupEvent, plus the single event it fetches
// before an edit (Edit Parent Event, the Tools calendar's pencil).
public sealed partial class AccountsController
{
    /// <summary>One group event, as VRChat sends it.</summary>
    /// <response code="200">VRChat's answer</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{groupId}/calendar/{eventId}")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetGroupEvent([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string eventId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetCalendarEventAsync(groupId, eventId, cancellationToken));

    /// <summary>Create a group event (or a repeating series).</summary>
    /// <response code="200">The new event, as VRChat sends it</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/groups/{groupId}/calendar")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> CreateGroupEvent([FromRoute] string accountId, [FromRoute] string groupId, [FromBody] GroupEventBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.CreateCalendarEventAsync(groupId, EventJson(groupId, body), cancellationToken));

    /// <summary>Edit a group event.</summary>
    /// <response code="200">The event after, as VRChat sends it</response>
    /// <response code="409">No live session</response>
    [HttpPut("{accountId}/groups/{groupId}/calendar/{eventId}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> UpdateGroupEvent([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string eventId, [FromBody] GroupEventBody body, CancellationToken cancellationToken)
    {
        var json = EventJson(groupId, body);
        json["eventId"] = eventId;
        return ReadLiveAsync(accountId, client => client.UpdateCalendarEventAsync(groupId, eventId, json, cancellationToken));
    }

    /// <summary>Delete a group event.</summary>
    /// <response code="204">Deleted</response>
    /// <response code="409">No live session</response>
    [HttpDelete("{accountId}/groups/{groupId}/calendar/{eventId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> DeleteGroupEvent([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string eventId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.DeleteCalendarEventAsync(groupId, eventId, cancellationToken));

    /// <summary>The original's saveEvent params, in its order.</summary>
    private static JsonObject EventJson(string groupId, GroupEventBody body)
    {
        var json = new JsonObject
        {
            ["startsAt"] = body.StartsAt,
            ["endsAt"] = body.EndsAt,
            ["title"] = body.Title.Trim(),
            ["accessType"] = body.AccessType,
            ["description"] = body.Description.Trim(),
            ["category"] = body.Category,
            ["tags"] = Strings(body.Tags.Select(t => t.Trim())),
            ["imageId"] = NullIfEmpty(body.ImageId),
            ["roleIds"] = Strings(body.RoleIds),
            ["platforms"] = Strings(body.Platforms),
            ["languages"] = Strings(body.Languages.Take(3)),
            ["sendCreationNotification"] = body.SendCreationNotification,
            ["hostEarlyJoinMinutes"] = body.HostEarlyJoinMinutes,
            ["guestEarlyJoinMinutes"] = body.GuestEarlyJoinMinutes,
            ["closeInstanceAfterEndMinutes"] = body.CloseInstanceAfterEndMinutes,
            ["groupId"] = groupId,
        };
        if (body.Recurrence is { } recurrence)
        {
            json["occurrenceKind"] = "series";
            json["recurrence"] = recurrence.DeepClone();
        }
        return json;
    }
}
