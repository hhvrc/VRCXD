using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;

namespace VRCX.API.Controller.Accounts;

public sealed class GalleryLimits
{
    public int Gallery { get; init; } = 64;
    public int Icons { get; init; } = 18;
    public int Emoji { get; init; } = 18;
    public int Stickers { get; init; } = 18;
    public int Prints { get; init; } = 64;
}

/// <summary>What the Inventory page needs to know about the account.</summary>
public sealed class GalleryInfo
{
    public required GalleryLimits Limits { get; init; }
    public required bool IsSupporter { get; init; }
    public string? UserIcon { get; init; }
    public string? CurrentAvatarImageUrl { get; init; }
}

public sealed class GalleryUploadForm
{
    [Required] public required IFormFile File { get; set; }
    /// <summary>gallery, icon, emoji, emojianimated or sticker.</summary>
    [Required] public required string Tag { get; set; }
    public string? MaskTag { get; set; }
    public string? AnimationStyle { get; set; }
    public int? Frames { get; set; }
    public int? FramesOverTime { get; set; }
    public string? LoopStyle { get; set; }
}

public sealed class PrintUploadForm
{
    [Required] public required IFormFile Image { get; set; }
    public string? Note { get; set; }
    [Required] public required string Timestamp { get; set; }
}

public sealed class GalleryPrints
{
    public required IReadOnlyList<VrchatPrint> Prints { get; init; }
    public required IReadOnlyList<string> Favorites { get; init; }
}

public sealed class SetUserIconBody
{
    /// <summary>The icon's file id; empty clears it.</summary>
    public string FileId { get; set; } = "";
}

public sealed class SetUserIconResponse
{
    public required string UserIcon { get; init; }
}

public sealed class RedeemRewardBody
{
    [Required] public required string Code { get; set; }
}

// The original's Inventory page. Everything but print favorites is live VRChat data.
public sealed record EmojiFileResponse(int? Frames, int? FramesOverTime, string? LoopStyle);

/// <summary>One image of a cosmetic: base (still), introAnimation (played once) or mainAnimation.</summary>
public sealed record CosmeticAsset(string Type, string Url, int? TotalDurationMs);

/// <summary>A cosmetic template from VRChat's index, as the original's IconFrame and ProfileEffect use it.</summary>
/// <remarks>A nameplate effect also has the two hex colours (no #) of the gradient behind it.</remarks>
public sealed record CosmeticResponse(string Id, string? Name, IReadOnlyList<CosmeticAsset> Assets, string? GradientStart, string? GradientEnd);

public sealed partial class AccountsController
{
    /// <summary>The account's upload limits, VRC+ state and current icon.</summary>
    /// <response code="200">The info</response>
    /// <response code="409">No live session</response>
    /// <summary>
    /// An emoji file's flipbook settings, so a boop's animated emoji can play as the original's Emoji
    /// component does. Frames are empty for a still emoji.
    /// </summary>
    /// <response code="200">The settings</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/emoji/{fileId}")]
    [ProducesResponseType<EmojiFileResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetEmojiFile([FromRoute] string accountId, [FromRoute] string fileId, CancellationToken cancellationToken)
    {
        // An emoji's sheet never changes once uploaded.
        Response.Headers.CacheControl = "private, max-age=86400";
        return await ReadLiveAsync(accountId, async client =>
        {
            var file = await client.GetFileAsync(fileId, cancellationToken);
            return new EmojiFileResponse(file?.Frames, file?.FramesOverTime, file?.LoopStyle);
        });
    }

    [HttpGet("{accountId}/gallery/info")]
    [ProducesResponseType<GalleryInfo>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetGalleryInfo([FromRoute] string accountId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var me = await client.GetCurrentUserAsync(cancellationToken);
            var permissions = await client.GetPermissionsAsync(cancellationToken);
            var defaults = new GalleryLimits();
            return new GalleryInfo
            {
                Limits = new GalleryLimits
                {
                    Gallery = PermissionMax(permissions, "permission-user-gallery") ?? defaults.Gallery,
                    Icons = PermissionMax(permissions, "permission-user-icons") ?? defaults.Icons,
                    Emoji = PermissionMax(permissions, "permission-user-emoji") ?? defaults.Emoji,
                    Stickers = PermissionMax(permissions, "permission-user-stickers") ?? defaults.Stickers,
                    Prints = PermissionMax(permissions, "permission-user-prints") ?? defaults.Prints,
                },
                IsSupporter = me.Tags.Contains("system_supporter"),
                UserIcon = me.UserIcon,
                CurrentAvatarImageUrl = me.CurrentAvatarImageUrl,
            };
        });

    private static int? PermissionMax(JsonElement permissions, string name)
    {
        if (permissions.ValueKind != JsonValueKind.Array) return null;
        foreach (var permission in permissions.EnumerateArray())
        {
            if (permission.TryGetProperty("name", out var n) && n.ValueEquals(name)
                && permission.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("max", out var max) && max.TryGetInt32(out var value) && value > 0)
                return value;
        }
        return null;
    }

    /// <summary>
    /// Every icon frame, profile effect or nameplate effect VRChat has, with its images: what the
    /// original's getCosmetics reads at login so users' cosmetics can be drawn.
    /// </summary>
    /// <response code="200">The templates</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/cosmetics/{kind:regex(^(iconFrame|profileEffect|nameplateEffect)$)}")]
    [ProducesResponseType<IEnumerable<CosmeticResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListCosmetics([FromRoute] string accountId, [FromRoute] string kind, CancellationToken cancellationToken)
    {
        // The catalogue changes with VRChat releases, not by the minute.
        Response.Headers.CacheControl = "private, max-age=3600";
        return await ReadLiveAsync(accountId, async client => ReadCosmetics(await client.GetCosmeticsIndexAsync(kind, cancellationToken)));
    }

    private static List<CosmeticResponse> ReadCosmetics(JsonElement index)
    {
        static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var found = new List<CosmeticResponse>();
        if (index.ValueKind != JsonValueKind.Array) return found;
        foreach (var item in index.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || Text(item, "id") is not { } id) continue;
            var assets = new List<CosmeticAsset>();
            var hasMetadata = item.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object;
            if (hasMetadata && metadata.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in list.EnumerateArray())
                {
                    if (asset.ValueKind != JsonValueKind.Object || Text(asset, "type") is not { } type || Text(asset, "url") is not { Length: > 0 } url) continue;
                    int? duration = asset.TryGetProperty("totalDurationMs", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out var ms) ? ms : null;
                    assets.Add(new CosmeticAsset(type, url, duration));
                }
            }
            found.Add(new CosmeticResponse(id, Text(item, "name"), assets,
                hasMetadata ? Text(metadata, "gradientStart") : null, hasMetadata ? Text(metadata, "gradientEnd") : null));
        }
        return found;
    }

    /// <summary>The account's uploaded images with a tag, newest first.</summary>
    /// <response code="200">The files</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/gallery/files/{tag:regex(^(gallery|icon|emoji|sticker)$)}")]
    [ProducesResponseType<IEnumerable<UserFile>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListGalleryFiles([FromRoute] string accountId, [FromRoute] string tag, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client => Enumerable.Reverse(await client.GetFilesAsync(tag, cancellationToken)).ToList());

    /// <summary>Uploads an image the client has already cropped and sized.</summary>
    /// <response code="200">The new file</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/gallery/files")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<UserFile>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> UploadGalleryFile([FromRoute] string accountId, [FromForm] GalleryUploadForm form, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var fields = new Dictionary<string, string> { ["tag"] = form.Tag };
            if (form.MaskTag is { Length: > 0 } mask) fields["maskTag"] = mask;
            if (form.AnimationStyle is { Length: > 0 } style) fields["animationStyle"] = style;
            if (form.Frames is { } frames) fields["frames"] = frames.ToString();
            if (form.FramesOverTime is { } fps) fields["framesOverTime"] = fps.ToString();
            if (form.LoopStyle is { Length: > 0 } loop) fields["loopStyle"] = loop;
            await using var image = form.File.OpenReadStream();
            return await client.UploadImageAsync(image, fields, cancellationToken);
        });

    /// <summary>Deletes an uploaded image.</summary>
    /// <response code="204">Deleted</response>
    /// <response code="409">No live session</response>
    [HttpDelete("{accountId}/gallery/files/{fileId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> DeleteGalleryFile([FromRoute] string accountId, [FromRoute] string fileId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.DeleteFileAsync(fileId, cancellationToken));

    /// <summary>Sets or clears the profile icon.</summary>
    /// <response code="200">The icon VRChat now has</response>
    /// <response code="409">No live session</response>
    [HttpPut("{accountId}/gallery/icon")]
    [ProducesResponseType<SetUserIconResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SetUserIcon([FromRoute] string accountId, [FromBody] SetUserIconBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var icon = body.FileId.Length > 0 ? $"{VrchatApiClient.BaseAddress}file/{Uri.EscapeDataString(body.FileId)}/1" : "";
            await client.SaveProfileAsync(accountId, new Dictionary<string, string> { ["userIcon"] = icon }, cancellationToken);
            return new SetUserIconResponse { UserIcon = icon };
        });

    /// <summary>The account's prints, newest first, and which of them are favorites.</summary>
    /// <response code="200">The prints</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/gallery/prints")]
    [ProducesResponseType<GalleryPrints>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListPrints([FromRoute] string accountId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var prints = await client.GetPrintsAsync(accountId, cancellationToken);
            var favorites = await _db.PrintFavorites.AsNoTracking().Where(f => f.OwnerUserId == accountId).Select(f => f.PrintId).ToListAsync(cancellationToken);
            return new GalleryPrints { Prints = prints.OrderByDescending(p => p.Timestamp).ToList(), Favorites = favorites };
        });

    /// <summary>Uploads a print the client has already sized to 1920x1080.</summary>
    /// <response code="200">The new print</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/gallery/prints")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<VrchatPrint>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> UploadPrint([FromRoute] string accountId, [FromForm] PrintUploadForm form, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            await using var image = form.Image.OpenReadStream();
            return await client.UploadPrintAsync(image, form.Note ?? "", form.Timestamp, cancellationToken);
        });

    /// <summary>Deletes a print.</summary>
    /// <response code="204">Deleted</response>
    /// <response code="409">No live session</response>
    [HttpDelete("{accountId}/gallery/prints/{printId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> DeletePrint([FromRoute] string accountId, [FromRoute] string printId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.DeletePrintAsync(printId, cancellationToken));

    /// <summary>Marks a print as a favorite, which protects it from deletion.</summary>
    /// <response code="204">Marked</response>
    /// <response code="404">Unknown account</response>
    [HttpPut("{accountId}/gallery/prints/{printId}/favorite")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> AddPrintFavorite([FromRoute] string accountId, [FromRoute] string printId, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        if (!await _db.PrintFavorites.AnyAsync(f => f.OwnerUserId == accountId && f.PrintId == printId, cancellationToken))
        {
            _db.PrintFavorites.Add(new PrintFavorite { OwnerUserId = accountId, PrintId = printId, CreatedAt = DateTimeOffset.UtcNow });
            await _db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }

    /// <summary>Removes a print from the favorites.</summary>
    /// <response code="204">Removed</response>
    [HttpDelete("{accountId}/gallery/prints/{printId}/favorite")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> RemovePrintFavorite([FromRoute] string accountId, [FromRoute] string printId, CancellationToken cancellationToken)
    {
        await _db.PrintFavorites.Where(f => f.OwnerUserId == accountId && f.PrintId == printId).ExecuteDeleteAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>The account's inventory, newest first, followed by VRChat's global items.</summary>
    /// <response code="200">The items</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/inventory")]
    [ProducesResponseType<IEnumerable<InventoryItem>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ListInventory([FromRoute] string accountId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.GetAllInventoryAsync(cancellationToken));

    /// <summary>Opens a bundle.</summary>
    /// <response code="204">Opened</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/inventory/{inventoryId}/consume")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> ConsumeInventoryBundle([FromRoute] string accountId, [FromRoute] string inventoryId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.ConsumeInventoryBundleAsync(inventoryId, cancellationToken));

    /// <summary>Redeems a reward code.</summary>
    /// <response code="204">Redeemed</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/inventory/redeem")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> RedeemReward([FromRoute] string accountId, [FromBody] RedeemRewardBody body, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.RedeemRewardAsync(body.Code.Trim(), cancellationToken));
}
