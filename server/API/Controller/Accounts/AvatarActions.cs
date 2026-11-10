using System.Net.Mime;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;

namespace VRCX.API.Controller.Accounts;

public sealed record SaveAvatarBody(string? Name, string? Description, string? ReleaseStatus,
    string? PrimaryStyle = null, string? SecondaryStyle = null, IReadOnlyList<string>? Tags = null);

public sealed record GalleryOrderBody(IReadOnlyList<string> Ids);

public sealed class AvatarGalleryUploadForm
{
    /// <summary>The picture, already cropped and fitted to VRChat's limits by the client.</summary>
    [System.ComponentModel.DataAnnotations.Required] public required IFormFile File { get; set; }
}

// The avatar pop-up: what only VRChat knows right now, and the actions its menu offers.
public sealed partial class AccountsController
{
    /// <summary>An avatar as VRChat has it now, and this account's own standing with it.</summary>
    /// <response code="200">The details. The avatar is missing when VRChat won't show it.</response>
    /// <response code="404">No such account</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/avatars/{avatarId}/details")]
    [ProducesResponseType<AvatarDetailsResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetAvatarDetails([FromRoute] string accountId, [FromRoute] string avatarId, CancellationToken cancellationToken)
    {
        var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == accountId, cancellationToken);
        if (account is null) return Problem(AccountError.AccountNotFound);
        var usage = await _db.AvatarUsages.AsNoTracking()
            .Where(u => u.OwnerUserId == accountId && u.AvatarId == avatarId)
            .Select(u => u.TimeSpent)
            .FirstOrDefaultAsync(cancellationToken);
        var isCurrent = account.CurrentAvatarId == avatarId;
        if (isCurrent && account.CurrentAvatarSince is { } since) usage += DateTimeOffset.UtcNow - since;

        return await ReadLiveAsync(accountId, async client =>
        {
            VRCX.Vrchat.Models.Avatar? avatar;
            try
            {
                avatar = await client.GetAvatarAsync(avatarId, cancellationToken);
            }
            catch (VrchatApiException ex) when (ex is not VrchatUnauthorizedException)
            {
                avatar = null;
            }
            var moderations = await client.GetAvatarModerationsAsync(cancellationToken);
            return new AvatarDetailsResponse
            {
                Avatar = avatar,
                IsBlocked = moderations.Any(m => m.TargetAvatarId == avatarId && m.AvatarModerationType == "block"),
                IsCurrent = isCurrent,
                TimeSpentMs = (long)usage.TotalMilliseconds
            };
        });
    }

    /// <summary>Block an avatar, so it never shows on other players.</summary>
    /// <response code="204">Blocked</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/avatars/{avatarId}/block")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> BlockAvatar([FromRoute] string accountId, [FromRoute] string avatarId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.BlockAvatarAsync(avatarId, cancellationToken));

    /// <summary>Unblock an avatar.</summary>
    /// <response code="204">Unblocked</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpDelete("{accountId}/avatars/{avatarId}/block")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> UnblockAvatar([FromRoute] string accountId, [FromRoute] string avatarId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.UnblockAvatarAsync(avatarId, cancellationToken));

    /// <summary>Make an avatar this account's fallback.</summary>
    /// <response code="204">Selected</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/avatars/{avatarId}/fallback")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SelectFallbackAvatar([FromRoute] string accountId, [FromRoute] string avatarId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.SelectFallbackAvatarAsync(avatarId, cancellationToken));

    /// <summary>Change an avatar this account made. Only the fields given are sent.</summary>
    /// <response code="204">Saved</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPut("{accountId}/avatars/{avatarId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SaveAvatar([FromRoute] string accountId, [FromRoute] string avatarId, [FromBody] SaveAvatarBody body, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            // The original's saveAvatar sends the id with the fields.
            var changes = new Dictionary<string, object> { ["id"] = avatarId };
            if (body.Name is not null) changes["name"] = body.Name;
            if (body.Description is not null) changes["description"] = body.Description;
            if (body.ReleaseStatus is "public" or "private") changes["releaseStatus"] = body.ReleaseStatus;
            // SetAvatarStylesDialog sends style ids ("" clears one) and the tags; SetAvatarTagsDialog the tags.
            if (body.PrimaryStyle is not null) changes["primaryStyle"] = body.PrimaryStyle;
            if (body.SecondaryStyle is not null) changes["secondaryStyle"] = body.SecondaryStyle;
            if (body.Tags is not null) changes["tags"] = body.Tags;
            await client.SaveAvatarAsync(avatarId, changes, cancellationToken);
            await _sync.RefreshAvatarAsync(accountId, avatarId, cancellationToken);
        });

    /// <summary>Change the image of an avatar this account made: the original's uploadImageLegacy (file and signature uploads, then the avatar's imageUrl).</summary>
    /// <response code="200">The new image URL</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused a step</response>
    [HttpPost("{accountId}/avatars/{avatarId}/image")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ContentImageResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ChangeAvatarImage([FromRoute] string accountId, [FromRoute] string avatarId, [FromForm] ContentImageForm form, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var imageUrl = await VrchatContentImage.ChangeAsync(client, "avatar", avatarId, form.ImageUrl, await ReadAllAsync(form.Image, cancellationToken), cancellationToken);
            await _sync.RefreshAvatarAsync(accountId, avatarId, cancellationToken);
            return new ContentImageResponse { ImageUrl = imageUrl };
        });

    /// <summary>An avatar's gallery images, as VRChat has them (GET files, tag avatargallery).</summary>
    /// <response code="200">The files</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/avatars/{avatarId}/gallery")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetAvatarGallery([FromRoute] string accountId, [FromRoute] string avatarId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetJsonAsync("files",
            [new("tag", "avatargallery"), new("galleryId", avatarId), new("n", "100"), new("offset", "0")], cancellationToken));

    /// <summary>Add an image to an avatar's gallery (POST file/image, tag avatargallery).</summary>
    /// <response code="200">The new file</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/avatars/{avatarId}/gallery")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<UserFile>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> UploadAvatarGalleryImage([FromRoute] string accountId, [FromRoute] string avatarId, [FromForm] AvatarGalleryUploadForm form, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            await using var image = form.File.OpenReadStream();
            return await client.UploadImageAsync(image, new Dictionary<string, string> { ["tag"] = "avatargallery", ["galleryId"] = avatarId }, cancellationToken);
        });

    /// <summary>Reorder an avatar's gallery (PUT files/order).</summary>
    /// <response code="204">Saved</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPut("{accountId}/avatars/{avatarId}/gallery/order")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SetAvatarGalleryOrder([FromRoute] string accountId, [FromRoute] string avatarId, [FromBody] GalleryOrderBody body, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.SetFileOrderAsync(body.Ids, avatarId, cancellationToken));

    /// <summary>The avatar styles VRChat offers (GET avatarStyles), for Change Styles and Author Tags.</summary>
    /// <response code="200">The styles</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/avatar-styles")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetAvatarStyles([FromRoute] string accountId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetJsonAsync("avatarStyles", [], cancellationToken));

    /// <summary>Delete an avatar this account made, on VRChat. The stored copy and its logs stay, marked hidden as VRChat marks deleted avatars, so My Avatars drops it as the original's cache did.</summary>
    /// <response code="204">Deleted</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpDelete("{accountId}/avatars/{avatarId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> DeleteAvatar([FromRoute] string accountId, [FromRoute] string avatarId, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            await client.DeleteAvatarAsync(avatarId, cancellationToken);
            await _db.Avatars.Where(a => a.Id == avatarId)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.ReleaseStatus, "hidden"), cancellationToken);
        });

    /// <summary>Delete an avatar's impostor.</summary>
    /// <response code="204">Deleted</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpDelete("{accountId}/avatars/{avatarId}/impostor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> DeleteImpostor([FromRoute] string accountId, [FromRoute] string avatarId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.DeleteImpostorAsync(avatarId, cancellationToken));
}
