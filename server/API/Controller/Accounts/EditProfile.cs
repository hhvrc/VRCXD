using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Services.Ingestion;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;

namespace VRCX.API.Controller.Accounts;

/// <summary>What the Edit Profile dialog opens with: the account, its self profile and VRChat's options.</summary>
public sealed class EditProfileResponse
{
    public required string Status { get; init; }
    public required string StatusDescription { get; init; }
    public required string Pronouns { get; init; }
    public required IReadOnlyList<string> StatusHistory { get; init; }
    public string? IconUrl { get; init; }
    public string? BannerUrl { get; init; }
    public string? BannerType { get; init; }
    public required string CurrentAvatarThumbnailImageUrl { get; init; }
    /// <summary>The original's $isModerator, which may also pick Offline as a status.</summary>
    public required bool IsModerator { get; init; }
    /// <summary>VRC+ supporters may pick every profile background.</summary>
    public required bool IsVrcPlus { get; init; }
    /// <summary>Spoken language codes, from the account's language_ tags.</summary>
    public required IReadOnlyList<string> Languages { get; init; }
    /// <summary>GET profile/{id}?asSelf=true as VRChat sent it.</summary>
    public required JsonElement Profile { get; init; }
    /// <summary>The config's constants.LANGUAGE.SPOKEN_LANGUAGE_OPTIONS: code to name.</summary>
    public required IReadOnlyDictionary<string, string> LanguageOptions { get; init; }
    /// <summary>The config's profileDefaults (theme and gradient colors), when it has them.</summary>
    public JsonElement? ProfileDefaults { get; init; }
}

/// <summary>The fields of VRChat's UpdateProfileRequest; only the ones present are sent.</summary>
public sealed class SaveProfileBody
{
    [StringLength(512)] public string? Bio { get; set; }
    [MaxLength(3)] public List<string>? BioLinks { get; set; }
    public string? BannerColor { get; set; }
    public string? BannerCustomUrl { get; set; }
    public string? BannerType { get; set; }
    public string? UserIcon { get; set; }
    public string? ThemeId { get; set; }
    public string? BackgroundType { get; set; }
    public string? BackgroundTextureId { get; set; }
    public string? BackgroundGradientBottom { get; set; }
    public string? BackgroundGradientTop { get; set; }
    public string? NameplateEffect { get; set; }
    public string? ProfileEffect { get; set; }
    public string? IconFrame { get; set; }

    /// <summary>The request body in the original's order, without the fields left out.</summary>
    public Dictionary<string, object> ToChanges()
    {
        var changes = new Dictionary<string, object>();
        void Add(string key, object? value)
        {
            if (value is not null) changes[key] = value;
        }
        Add("bio", Bio);
        Add("bioLinks", BioLinks);
        Add("bannerColor", BannerColor);
        Add("bannerCustomUrl", BannerCustomUrl);
        Add("bannerType", BannerType);
        Add("userIcon", UserIcon);
        Add("themeId", ThemeId);
        Add("backgroundType", BackgroundType);
        Add("backgroundTextureId", BackgroundTextureId);
        Add("backgroundGradientBottom", BackgroundGradientBottom);
        Add("backgroundGradientTop", BackgroundGradientTop);
        Add("nameplateEffect", NameplateEffect);
        Add("profileEffect", ProfileEffect);
        Add("iconFrame", IconFrame);
        return changes;
    }
}

public sealed class ProfileThemeBody
{
    [Required(AllowEmptyStrings = true)][StringLength(12)] public required string Name { get; set; }
    public string? ButtonColor { get; set; }
    public string? IconColor { get; set; }
    public string? SubtextColor { get; set; }
}

public sealed class AddLanguageBody
{
    [Required] public required string Language { get; set; }
}

// The original's EditProfileDialog: the self profile it opens with, its save, profile themes and
// spoken languages. Everything is live VRChat data.
public sealed partial class AccountsController
{
    /// <summary>The account's own profile as the Edit Profile dialog needs it.</summary>
    /// <response code="200">The profile</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/profile")]
    [ProducesResponseType<EditProfileResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetEditProfile([FromRoute] string accountId, [FromServices] VrchatStore store, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var me = await client.GetCurrentUserAsync(cancellationToken);
            await store.UpsertCurrentUserAsync(_db, me, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            var profile = await client.GetSelfProfileAsync(accountId, cancellationToken);
            var config = await client.GetConfigAsync(cancellationToken);
            return new EditProfileResponse
            {
                Status = me.Status,
                StatusDescription = me.StatusDescription,
                Pronouns = me.Pronouns,
                StatusHistory = me.StatusHistory,
                IconUrl = me.IconUrl,
                BannerUrl = me.BannerUrl,
                BannerType = me.BannerType,
                CurrentAvatarThumbnailImageUrl = me.CurrentAvatarThumbnailImageUrl,
                IsModerator = (me.DeveloperType is { Length: > 0 } type && type != "none") || me.Tags.Contains("admin_moderator"),
                IsVrcPlus = me.Tags.Contains("system_supporter"),
                Languages = LanguagesOf(me),
                Profile = profile,
                LanguageOptions = SpokenLanguages(config),
                ProfileDefaults = config.ValueKind == JsonValueKind.Object && config.TryGetProperty("profileDefaults", out var defaults) && defaults.ValueKind == JsonValueKind.Object ? defaults : null,
            };
        });

    private static IReadOnlyList<string> LanguagesOf(CurrentUser me) =>
        me.Tags.Where(t => t.StartsWith("language_", StringComparison.Ordinal)).Select(t => t["language_".Length..]).ToList();

    private static Dictionary<string, string> SpokenLanguages(JsonElement config)
    {
        var options = new Dictionary<string, string>();
        if (config.ValueKind == JsonValueKind.Object
            && config.TryGetProperty("constants", out var constants) && constants.ValueKind == JsonValueKind.Object
            && constants.TryGetProperty("LANGUAGE", out var language) && language.ValueKind == JsonValueKind.Object
            && language.TryGetProperty("SPOKEN_LANGUAGE_OPTIONS", out var spoken) && spoken.ValueKind == JsonValueKind.Object)
        {
            foreach (var option in spoken.EnumerateObject())
                if (option.Value.ValueKind == JsonValueKind.String) options[option.Name] = option.Value.GetString()!;
        }
        return options;
    }

    /// <summary>Saves the profile fields that changed: PUT profile/{id} with exactly the fields given.</summary>
    /// <response code="200">VRChat's answer</response>
    /// <response code="409">No live session</response>
    [HttpPut("{accountId}/profile")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SaveProfile([FromRoute] string accountId, [FromBody] SaveProfileBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.UpdateProfileAsync(accountId, body.ToChanges(), cancellationToken));

    /// <summary>Creates a profile theme, as the dialog's Create Theme does.</summary>
    /// <response code="200">The new theme</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/profile/themes")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> CreateProfileTheme([FromRoute] string accountId, [FromBody] ProfileThemeBody body, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, client => client.CreateProfileThemeAsync(new { name = body.Name, buttonColor = body.ButtonColor, iconColor = body.IconColor, subtextColor = body.SubtextColor }, cancellationToken));

    /// <summary>Renames or recolors a profile theme.</summary>
    /// <response code="204">Saved</response>
    /// <response code="409">No live session</response>
    [HttpPut("{accountId}/profile/themes/{themeId}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SaveProfileTheme([FromRoute] string accountId, [FromRoute] string themeId, [FromBody] ProfileThemeBody body, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.SaveProfileThemeAsync(themeId, new { themeId, name = body.Name, buttonColor = body.ButtonColor, iconColor = body.IconColor, subtextColor = body.SubtextColor }, cancellationToken));

    /// <summary>Deletes a profile theme.</summary>
    /// <response code="204">Deleted</response>
    /// <response code="409">No live session</response>
    [HttpDelete("{accountId}/profile/themes/{themeId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> DeleteProfileTheme([FromRoute] string accountId, [FromRoute] string themeId, CancellationToken cancellationToken) =>
        RunAsync(accountId, client => client.DeleteProfileThemeAsync(themeId, cancellationToken));

    /// <summary>Adds a spoken language: the language_ tag on VRChat.</summary>
    /// <response code="200">The account's languages after the change</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/profile/languages")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AddLanguage([FromRoute] string accountId, [FromBody] AddLanguageBody body, [FromServices] VrchatStore store, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var me = await client.AddUserTagsAsync(accountId, [$"language_{body.Language}"], cancellationToken);
            await store.UpsertCurrentUserAsync(_db, me, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return LanguagesOf(me);
        });

    /// <summary>Removes a spoken language.</summary>
    /// <response code="200">The account's languages after the change</response>
    /// <response code="409">No live session</response>
    [HttpDelete("{accountId}/profile/languages/{language}")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> RemoveLanguage([FromRoute] string accountId, [FromRoute] string language, [FromServices] VrchatStore store, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var me = await client.RemoveUserTagsAsync(accountId, [$"language_{language}"], cancellationToken);
            await store.UpsertCurrentUserAsync(_db, me, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return LanguagesOf(me);
        });
}
