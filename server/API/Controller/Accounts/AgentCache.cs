using System.Net.Mime;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Live;
using VRCX.Common.Services.Live;

namespace VRCX.API.Controller.Accounts;

/// <summary>One asset bundle, as the original's cacheCoordinator picks it from a world's or avatar's unity packages.</summary>
public sealed record CacheEntryBody(string Id, int Version, string? Variant, int VariantVersion);

// VRChat's asset bundle cache on the PC (the original's AssetBundleManager).
public sealed partial class AccountsController
{
    /// <summary>The cache's size and folder (GetCacheSize).</summary>
    /// <response code="200">{"size": bytes, "location": "..."}</response>
    [HttpGet("{accountId}/agent/cache")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentCacheInfo([FromRoute] string accountId, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        // Adding up a big cache takes a while.
        AgentActionAsync(accountId, Capability.Cache, desktops, new ClientAction { GetCacheInfo = new LogNoData() }, cancellationToken, TimeSpan.FromMinutes(2));

    /// <summary>Whether a bundle is cached (CheckVRChatCache).</summary>
    /// <response code="200">{"size": bytes or -1, "locked": bool, "path": "..."}</response>
    [HttpPost("{accountId}/agent/cache/check")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentCheckCache([FromRoute] string accountId, [FromBody] CacheEntryBody body, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        AgentActionAsync(accountId, Capability.Cache, desktops, new ClientAction { CheckCache = Entry(body) }, cancellationToken);

    /// <summary>Deletes a bundle from the cache (DeleteCache).</summary>
    [HttpPost("{accountId}/agent/cache/delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentDeleteCacheEntry([FromRoute] string accountId, [FromBody] CacheEntryBody body, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        AgentActionAsync(accountId, Capability.Cache, desktops, new ClientAction { DeleteCacheEntry = Entry(body) }, cancellationToken);

    /// <summary>Empties the whole cache (DeleteAllCache).</summary>
    [HttpPost("{accountId}/agent/cache/clear")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentClearCache([FromRoute] string accountId, [FromBody] ConfirmBody body, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        AgentActionAsync(accountId, Capability.Cache, desktops, new ClientAction { ClearCache = new Confirm { Confirm_ = body.Confirm } }, cancellationToken, TimeSpan.FromMinutes(5));

    /// <summary>Deletes old versions from the cache (SweepCache).</summary>
    /// <response code="200">The removed folders</response>
    [HttpPost("{accountId}/agent/cache/sweep")]
    [ProducesResponseType<string[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentSweepCache([FromRoute] string accountId, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        AgentActionAsync(accountId, Capability.Cache, desktops, new ClientAction { SweepCache = new LogNoData() }, cancellationToken, TimeSpan.FromMinutes(5));

    /// <summary>Cuts every saved print in the UGC folder to its picture (the original's CropAllPrints).</summary>
    /// <response code="200">{"cropped": how many}</response>
    /// <response code="400">Not confirmed</response>
    [HttpPost("{accountId}/agent/prints/crop")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentCropPrints([FromRoute] string accountId, [FromBody] ConfirmBody body, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        AgentActionAsync(accountId, Capability.SaveMedia, desktops, new ClientAction { CropPrints = new CropPrints { Confirm = body.Confirm } }, cancellationToken, TimeSpan.FromMinutes(10));

    private static CacheEntry Entry(CacheEntryBody body) => new() { Id = body.Id, Version = body.Version, Variant = body.Variant ?? "", VariantVersion = body.VariantVersion };
}
