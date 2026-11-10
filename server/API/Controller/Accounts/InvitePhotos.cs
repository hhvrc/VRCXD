using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Services.Ingestion;

namespace VRCX.API.Controller.Accounts;

public sealed class InvitePhotoForm
{
    /// <summary>The picture, already fitted to VRChat's limits by the client as the original's ResizeImageToFitLimits does.</summary>
    [Required] public required IFormFile Image { get; set; }
    [Required] public required string Location { get; set; }
    public string WorldName { get; set; } = "";
    public int? MessageSlot { get; set; }
}

public sealed class RequestInvitePhotoForm
{
    [Required] public required IFormFile Image { get; set; }
    public int? RequestSlot { get; set; }
}

public sealed class InviteResponsePhotoForm
{
    [Required] public required IFormFile Image { get; set; }
    public int ResponseSlot { get; set; }
}

// VRC+ invites, invite requests and invite answers with a picture attached: the original's
// sendInvitePhoto, sendRequestInvitePhoto and sendInviteResponsePhoto.
public sealed partial class AccountsController
{
    /// <summary>Invite a user to an instance with a picture.</summary>
    /// <response code="204">Sent</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/invites/{userId}/photo")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SendInvitePhoto([FromRoute] string accountId, [FromRoute] string userId, [FromForm] InvitePhotoForm form, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            var data = new Dictionary<string, object> { ["instanceId"] = form.Location, ["worldId"] = form.Location, ["worldName"] = form.WorldName };
            if (form.MessageSlot is { } slot) data["messageSlot"] = slot;
            await using var image = form.Image.OpenReadStream();
            await client.SendPhotoAsync($"invite/{Uri.EscapeDataString(userId)}/photo", data, image, cancellationToken);
        });

    /// <summary>Ask a user for an invite with a picture.</summary>
    /// <response code="204">Sent</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/invite-requests/{userId}/photo")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SendRequestInvitePhoto([FromRoute] string accountId, [FromRoute] string userId, [FromForm] RequestInvitePhotoForm form, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            var data = new Dictionary<string, object> { ["platform"] = "standalonewindows" };
            if (form.RequestSlot is { } slot) data["requestSlot"] = slot;
            await using var image = form.Image.OpenReadStream();
            await client.SendPhotoAsync($"requestInvite/{Uri.EscapeDataString(userId)}/photo", data, image, cancellationToken);
        });

    /// <summary>Answer an invite or invite request with a saved message and a picture, then hide it.</summary>
    /// <response code="204">Sent</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/notifications/{notificationId}/invite-response/photo")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SendInviteResponsePhoto([FromRoute] string accountId, [FromRoute] string notificationId, [FromForm] InviteResponsePhotoForm form, [FromServices] VrchatStore store, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            await using (var image = form.Image.OpenReadStream())
                await client.SendPhotoAsync($"invite/{Uri.EscapeDataString(notificationId)}/response/photo", new Dictionary<string, object> { ["responseSlot"] = form.ResponseSlot, ["rsvp"] = true }, image, cancellationToken);
            await client.HideNotificationAsync(notificationId, cancellationToken);
            await store.MarkNotificationAsync(_db, accountId, notificationId, seen: null, hidden: true, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        });
}
