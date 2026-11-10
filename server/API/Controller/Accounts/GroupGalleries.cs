using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;

namespace VRCX.API.Controller.Accounts;

/// <summary>A group gallery's fields, as the original's GroupGalleryForm sends them.</summary>
public sealed class GroupGalleryBody
{
    [Required][StringLength(64, MinimumLength = 1)] public required string Name { get; set; }
    [StringLength(512)] public string Description { get; set; } = "";
    public bool MembersOnly { get; set; }
}

/// <summary>An image from your own gallery to add.</summary>
public sealed class GroupGalleryImageBody
{
    [Required] public required string FileId { get; set; }
}

// The original's GroupDialogPhotosTab and useGroupGalleries, and GroupDialogStoreTab's store.
public sealed partial class AccountsController
{
    /// <summary>A page of a gallery's images, as VRChat sends them.</summary>
    /// <response code="200">VRChat's answer</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/groups/{groupId}/galleries/{galleryId}")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetGroupGalleryImages([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string galleryId, [FromQuery, Range(1, 100)] int n = 100, [FromQuery, Range(0, int.MaxValue)] int offset = 0, CancellationToken cancellationToken = default) =>
        ReadLiveAsync(accountId, client => client.GetGroupGalleryImagesAsync(groupId, galleryId, n, offset, cancellationToken));

    /// <summary>Create a gallery.</summary>
    /// <response code="200">The new gallery, as VRChat sends it</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/groups/{groupId}/galleries")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> CreateGroupGallery([FromRoute] string accountId, [FromRoute] string groupId, [FromBody] GroupGalleryBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.CreateGroupGalleryAsync(groupId, GalleryJson(body), cancellationToken));

    /// <summary>Edit a gallery.</summary>
    /// <response code="200">The gallery after, as VRChat sends it</response>
    /// <response code="409">No live session</response>
    [HttpPut("{accountId}/groups/{groupId}/galleries/{galleryId}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> UpdateGroupGallery([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string galleryId, [FromBody] GroupGalleryBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.UpdateGroupGalleryAsync(groupId, galleryId, GalleryJson(body), cancellationToken));

    /// <summary>Delete a gallery and its images.</summary>
    /// <response code="204">Deleted</response>
    /// <response code="409">No live session</response>
    [HttpDelete("{accountId}/groups/{groupId}/galleries/{galleryId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> DeleteGroupGallery([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string galleryId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.DeleteGroupGalleryAsync(groupId, galleryId, cancellationToken));

    /// <summary>Add an image from your own gallery. VRChat may hold it for approval.</summary>
    /// <response code="200">The image, as VRChat sends it</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/groups/{groupId}/galleries/{galleryId}/images")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AddGroupGalleryImage([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string galleryId, [FromBody] GroupGalleryImageBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.AddGroupGalleryImageAsync(groupId, galleryId, body.FileId, cancellationToken));

    /// <summary>Delete a gallery image.</summary>
    /// <response code="204">Deleted</response>
    /// <response code="409">No live session</response>
    [HttpDelete("{accountId}/groups/{groupId}/galleries/{galleryId}/images/{imageId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> DeleteGroupGalleryImage([FromRoute] string accountId, [FromRoute] string groupId, [FromRoute] string galleryId, [FromRoute] string imageId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.DeleteGroupGalleryImageAsync(groupId, galleryId, imageId, cancellationToken));

    /// <summary>A store with its listings (a group's Store tab), as VRChat sends it.</summary>
    /// <response code="200">VRChat's answer</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/stores/{storeId}")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetStore([FromRoute] string accountId, [FromRoute] string storeId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetStoreAsync(storeId, cancellationToken));

    private static JsonObject GalleryJson(GroupGalleryBody body) => new()
    {
        ["name"] = body.Name.Trim(),
        ["description"] = body.Description.Trim(),
        ["membersOnly"] = body.MembersOnly,
    };
}
