using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Vrchat;

namespace VRCX.API.Controller.Accounts;

public sealed record SaveWorldBody(string? Name, string? Description, int? Capacity, int? RecommendedCapacity, string? PreviewYoutubeId,
    IReadOnlyList<string>? Tags = null, IReadOnlyList<string>? DisabledPropAbilities = null, IReadOnlyList<string>? UrlList = null);

public sealed class ContentImageForm
{
    /// <summary>The picture, already cropped and fitted to VRChat's limits by the client (the original's ResizeImageToFitLimits).</summary>
    [Required] public required IFormFile Image { get; set; }
    /// <summary>The image URL the avatar or world has now: its file gets the new version.</summary>
    [Required] public required string ImageUrl { get; set; }
}

// The world pop-up: what only VRChat knows right now, and the actions its menu offers.
public sealed partial class AccountsController
{
    /// <summary>A world as VRChat has it now, with its momentary counts and open instances.</summary>
    /// <response code="200">The world, or nothing if VRChat has no such world</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/worlds/{worldId}/live")]
    [ProducesResponseType<VRCX.Vrchat.Models.World>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetLiveWorld([FromRoute] string accountId, [FromRoute] string worldId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetWorldAsync(worldId, cancellationToken));

    /// <summary>This account's own history in a world, from its stored visits.</summary>
    /// <response code="200">The totals</response>
    [HttpGet("{accountId}/worlds/{worldId}/stats")]
    [ProducesResponseType<WorldStatsResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetWorldStats([FromRoute] string accountId, [FromRoute] string worldId, CancellationToken cancellationToken)
    {
        var visits = await _db.InstanceVisits.AsNoTracking()
            .Where(v => v.UserId == accountId && v.Instance.WorldId == worldId)
            .Select(v => new { v.JoinedAt, v.LeftAt })
            .ToListAsync(cancellationToken);
        return Ok(new WorldStatsResponse
        {
            VisitCount = visits.Count,
            LastVisit = visits.Count == 0 ? null : visits.Max(v => v.JoinedAt),
            TimeSpentMs = visits.Where(v => v.LeftAt is not null).Sum(v => (long)(v.LeftAt!.Value - v.JoinedAt).TotalMilliseconds)
        });
    }

    /// <summary>Whether this account has saved data in a world, live from VRChat.</summary>
    /// <response code="200">True or false</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/worlds/{worldId}/persist")]
    [ProducesResponseType<bool>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> HasWorldPersistData([FromRoute] string accountId, [FromRoute] string worldId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.HasWorldPersistDataAsync(accountId, worldId, cancellationToken));

    /// <summary>Delete this account's saved data in a world.</summary>
    /// <response code="204">Deleted</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpDelete("{accountId}/worlds/{worldId}/persist")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> DeleteWorldPersistData([FromRoute] string accountId, [FromRoute] string worldId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.DeleteWorldPersistDataAsync(accountId, worldId, cancellationToken));

    /// <summary>This account's like or dislike of a world, live from VRChat.</summary>
    /// <response code="200">The rating: like, dislike or null</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/worlds/{worldId}/rating")]
    [ProducesResponseType<VRCX.Vrchat.Models.WorldRating>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetWorldRating([FromRoute] string accountId, [FromRoute] string worldId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetWorldRatingAsync(worldId, cancellationToken));

    /// <summary>Like or dislike a world.</summary>
    /// <param name="accountId">The account to rate as</param>
    /// <param name="worldId">The world to rate</param>
    /// <param name="value">like or dislike</param>
    /// <param name="cancellationToken">Cancels the request</param>
    /// <response code="200">The rating VRChat now has</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPut("{accountId}/worlds/{worldId}/rating")]
    [ProducesResponseType<VRCX.Vrchat.Models.WorldRating>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SetWorldRating([FromRoute] string accountId, [FromRoute] string worldId,
        [FromQuery][Required][RegularExpression("^(like|dislike)$")] string value, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.SetWorldRatingAsync(worldId, value, cancellationToken));

    /// <summary>Clear this account's like or dislike of a world.</summary>
    /// <response code="200">The rating VRChat now has</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpDelete("{accountId}/worlds/{worldId}/rating")]
    [ProducesResponseType<VRCX.Vrchat.Models.WorldRating>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> DeleteWorldRating([FromRoute] string accountId, [FromRoute] string worldId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.DeleteWorldRatingAsync(worldId, cancellationToken));

    /// <summary>Change a world this account made. Only the fields given are sent.</summary>
    /// <response code="204">Saved</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPut("{accountId}/worlds/{worldId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SaveWorld([FromRoute] string accountId, [FromRoute] string worldId, [FromBody] SaveWorldBody body, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            // The original's saveWorld sends the id with the fields.
            var changes = new Dictionary<string, object> { ["id"] = worldId };
            if (body.Name is not null) changes["name"] = body.Name;
            if (body.Description is not null) changes["description"] = body.Description;
            if (body.Capacity is not null) changes["capacity"] = body.Capacity;
            if (body.RecommendedCapacity is not null) changes["recommendedCapacity"] = body.RecommendedCapacity;
            if (body.PreviewYoutubeId is not null) changes["previewYoutubeId"] = body.PreviewYoutubeId;
            // SetWorldTagsDialog and WorldAllowedDomainsDialog.
            if (body.Tags is not null) changes["tags"] = body.Tags;
            if (body.DisabledPropAbilities is not null) changes["disabledPropAbilities"] = body.DisabledPropAbilities;
            if (body.UrlList is not null) changes["urlList"] = body.UrlList;
            await client.SaveWorldAsync(worldId, changes, cancellationToken);
            await _sync.RefreshWorldAsync(accountId, worldId, cancellationToken);
        });

    /// <summary>Change the image of a world this account made: the original's uploadImageLegacy (file and signature uploads, then the world's imageUrl).</summary>
    /// <response code="200">The new image URL</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused a step</response>
    [HttpPost("{accountId}/worlds/{worldId}/image")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ContentImageResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ChangeWorldImage([FromRoute] string accountId, [FromRoute] string worldId, [FromForm] ContentImageForm form, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var imageUrl = await VrchatContentImage.ChangeAsync(client, "world", worldId, form.ImageUrl, await ReadAllAsync(form.Image, cancellationToken), cancellationToken);
            await _sync.RefreshWorldAsync(accountId, worldId, cancellationToken);
            return new ContentImageResponse { ImageUrl = imageUrl };
        });

    /// <summary>A world's store listings, for its Store tab (GET economy/store, listings hydrated).</summary>
    /// <response code="200">The listings</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/worlds/{worldId}/store")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetWorldStore([FromRoute] string accountId, [FromRoute] string worldId, [FromQuery] string storeId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var store = await client.GetJsonAsync("economy/store", [new("storeId", storeId), new("hydrateListings", "true"), new("hydrateContext", "false")], cancellationToken);
            return store.ValueKind == JsonValueKind.Object && store.TryGetProperty("listings", out var listings) && listings.ValueKind == JsonValueKind.Array
                ? listings
                : JsonSerializer.SerializeToElement(Array.Empty<object>());
        });

    private static async Task<byte[]> ReadAllAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>Publish a world this account made to Community Labs.</summary>
    /// <response code="204">Published</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPut("{accountId}/worlds/{worldId}/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> PublishWorld([FromRoute] string accountId, [FromRoute] string worldId, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            await client.PublishWorldAsync(worldId, cancellationToken);
            await _sync.RefreshWorldAsync(accountId, worldId, cancellationToken);
        });

    /// <summary>Take a world this account made out of publication.</summary>
    /// <response code="204">Unpublished</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpDelete("{accountId}/worlds/{worldId}/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> UnpublishWorld([FromRoute] string accountId, [FromRoute] string worldId, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            await client.UnpublishWorldAsync(worldId, cancellationToken);
            await _sync.RefreshWorldAsync(accountId, worldId, cancellationToken);
        });

    /// <summary>Delete a world this account made, on VRChat. The stored copy and its logs stay.</summary>
    /// <response code="204">Deleted</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpDelete("{accountId}/worlds/{worldId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> DeleteWorld([FromRoute] string accountId, [FromRoute] string worldId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.DeleteWorldAsync(worldId, cancellationToken));

    /// <summary>Make a world this account's home.</summary>
    /// <response code="204">Saved</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPut("{accountId}/home/{worldId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SetHome([FromRoute] string accountId, [FromRoute] string worldId, CancellationToken cancellationToken) =>
        SaveHomeAsync(accountId, worldId, cancellationToken);

    /// <summary>Clear this account's home world.</summary>
    /// <response code="204">Cleared</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpDelete("{accountId}/home")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ResetHome([FromRoute] string accountId, CancellationToken cancellationToken) =>
        SaveHomeAsync(accountId, "", cancellationToken);

    private Task<IActionResult> SaveHomeAsync(string accountId, string worldId, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            await client.UpdateCurrentUserAsync(accountId, new Dictionary<string, string> { ["homeLocation"] = worldId }, cancellationToken);
            await _db.Accounts.Where(a => a.UserId == accountId)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.HomeLocation, worldId), cancellationToken);
        });
}
