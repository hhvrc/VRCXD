using System.Net.Mime;
using System.Text.Json;
using System.Text.RegularExpressions;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;

namespace VRCX.API.Controller.Accounts;

public sealed partial class AccountsController
{
    /// <summary>
    /// The original's getBundleDateSize: for each platform, VRChat's analysis (file size, uncompressed
    /// size, avatar stats) of the newest standard bundle built with a Unity version the SDK supports.
    /// </summary>
    /// <response code="200">Analysis by platform (standalonewindows, android, ios); platforms without one are left out</response>
    /// <response code="404">No such world or avatar</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/{kind:regex(^(worlds|avatars)$)}/{id}/file-analysis")]
    [ProducesResponseType<Dictionary<string, JsonElement>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetFileAnalysis([FromRoute] string accountId, [FromRoute] string kind, [FromRoute] string id, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            IReadOnlyList<UnityPackage>? packages = kind == "worlds"
                ? (await client.GetWorldAsync(id, cancellationToken))?.UnityPackages
                : (await client.GetAvatarAsync(id, cancellationToken))?.UnityPackages;
            var found = new Dictionary<string, JsonElement>();
            if (packages is null) return found;
            var config = await client.GetConfigAsync(cancellationToken);
            var sdkUnityVersion = SdkUnityVersionOf(config);
            foreach (var (platform, fileId, version, variant) in PickAnalysedBundles(packages, sdkUnityVersion))
            {
                JsonElement? analysis;
                try { analysis = await client.GetFileAnalysisAsync(fileId, version, variant, cancellationToken); }
                // The original ignores analysis errors: a bundle without one just shows no size.
                catch (VrchatApiException) { continue; }
                if (analysis is { ValueKind: JsonValueKind.Object } json && json.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True)
                    found[platform] = json;
            }
            Response.Headers.CacheControl = "private, max-age=3600";
            return found;
        });

    /// <summary>
    /// The Unity version VRChat's SDK builds with (config's sdkUnityVersion), which the original's
    /// compareUnityVersion holds each package against for the pop-ups' Platform line.
    /// </summary>
    /// <response code="200">The version, null when the config has none</response>
    /// <response code="409">No live session</response>
    [HttpGet("{accountId}/sdk-unity-version")]
    [ProducesResponseType<SdkUnityVersionResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> GetSdkUnityVersion([FromRoute] string accountId, CancellationToken cancellationToken) =>
        ReadLiveAsync(accountId, async client =>
        {
            var config = await client.GetConfigAsync(cancellationToken);
            Response.Headers.CacheControl = "private, max-age=3600";
            return new SdkUnityVersionResponse(SdkUnityVersionOf(config));
        });

    private static string? SdkUnityVersionOf(JsonElement config) =>
        config.ValueKind == JsonValueKind.Object && config.TryGetProperty("sdkUnityVersion", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Newest package first, one per platform: standard or security variants the SDK's Unity can open.</summary>
    internal static IEnumerable<(string Platform, string FileId, int Version, string Variant)> PickAnalysedBundles(IReadOnlyList<UnityPackage> packages, string? sdkUnityVersion)
    {
        var maxSort = UnitySortNumber(sdkUnityVersion);
        if (maxSort is null) yield break;
        var seen = new HashSet<string>();
        for (var i = packages.Count - 1; i >= 0; i--)
        {
            var package = packages[i];
            if (package.Variant is { Length: > 0 } and not "standard" and not "security") continue;
            if (package.UnitySortNumber is not { } sort || sort > maxSort) continue;
            if (seen.Contains(package.Platform)) continue;
            var fileId = package.AssetUrl is { } url ? FileIdPattern().Match(url).Value : "";
            var versionMatch = package.AssetUrl is { } u ? FileVersionPattern().Match(u) : Match.Empty;
            if (fileId.Length == 0 || !versionMatch.Success || !int.TryParse(versionMatch.Groups[1].Value, out var version) || version == 0) continue;
            seen.Add(package.Platform);
            yield return (package.Platform, fileId, version, package.Variant is { Length: > 0 } and not "standard" ? package.Variant : "security");
        }
    }

    /// <summary>The original's compareUnityVersion: "2022.3.22f1" becomes 2022032200000 to compare with unitySortNumber.</summary>
    internal static long? UnitySortNumber(string? unityVersion)
    {
        var parts = unityVersion?.Split('.');
        if (parts is not { Length: >= 3 }) return null;
        var number = parts[0] + parts[1].PadLeft(2, '0');
        var letter = parts[2].IndexOfAny("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray());
        if (letter > -1)
            number += parts[2][..letter].PadLeft(2, '0') + "0" + (parts[2][letter] == 'p' ? "1" : "0") + "0";
        else
            number += "000";
        var digits = new string(number.Where(char.IsAsciiDigit).ToArray());
        return long.TryParse(digits, out var sort) ? sort : null;
    }

    [GeneratedRegex("file_[0-9A-Za-z-]+")]
    private static partial Regex FileIdPattern();

    [GeneratedRegex(@"/file_[0-9A-Za-z-]+/([0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FileVersionPattern();
}

/// <summary>VRChat config's sdkUnityVersion.</summary>
public sealed record SdkUnityVersionResponse(string? SdkUnityVersion);
