using System.Net.Mime;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Live;
using VRCX.Common.Services.Live;

namespace VRCX.API.Controller.Desktop;

public sealed record OpenFolderBody(string Folder, string? RelativePath);

public sealed record PickPathBody(string Purpose, string? Start = null);

/// <summary>For destructive actions: set only after the user agreed.</summary>
public sealed record ConfirmBody(bool Confirm);

// The PC features the original did itself (folders, VRChat's config and registry, the cache, ...).
// Each is a ClientAction the account's agent carries out; the web client offers one only when the
// connected agent declared its capability.
public sealed partial class AccountDesktopController
{
    /// <summary>Opens one of VRChat's folders on the account's PC, selecting an item inside it when given.</summary>
    /// <response code="204">Opened</response>
    /// <response code="404">That folder or item is not on the PC</response>
    [HttpPost("{accountId}/agent/open-folder")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentOpenFolder([FromRoute] string accountId, [FromBody] OpenFolderBody body, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        !Enum.TryParse<Folder>(body.Folder.Replace("_", ""), ignoreCase: true, out var folder) || folder == Folder.Unspecified
            ? Task.FromResult<IActionResult>(Problem(AgentError.NotFound("No such folder")))
            : AgentActionAsync(accountId, Capability.Folders,
                new ClientAction { OpenFolder = new OpenFolder { Folder = folder, RelativePath = body.RelativePath ?? "" } }, cancellationToken);

    /// <summary>Shows a folder or file picker on the PC and keeps the choice as the agent's setting.</summary>
    /// <response code="200">{"path": "..."}, empty when cancelled</response>
    [HttpPost("{accountId}/agent/pick-path")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentPickPath([FromRoute] string accountId, [FromBody] PickPathBody body, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        !Enum.TryParse<PickPurpose>(body.Purpose.Replace("_", ""), ignoreCase: true, out var purpose) || purpose == PickPurpose.Unspecified
            ? Task.FromResult<IActionResult>(Problem(AgentError.NotFound("No such picker")))
            // A person picks: give them longer than an action's answer usually takes.
            : AgentActionAsync(accountId, Capability.PickPath,
                new ClientAction { PickPath = new PickPath { Purpose = purpose, Start = body.Start ?? "" } }, cancellationToken, TimeSpan.FromMinutes(5));

    /// <summary>
    /// Takes VRChat's and VRCX's metadata out of every picture in the PC's photos folder (the
    /// original's DeleteAllScreenshotMetadata); <c>confirm</c> says the user agreed.
    /// </summary>
    /// <response code="200">{"changed": n}</response>
    [HttpPost("{accountId}/agent/screenshot-metadata/delete-all")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentDeleteAllScreenshotMetadata([FromRoute] string accountId, [FromBody] ConfirmBody body, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        // A whole photos folder takes a while.
        AgentActionAsync(accountId, Capability.Screenshots,
            new ClientAction { DeleteScreenshotMetadata = new DeleteScreenshotMetadata { All = true, Confirm = body.Confirm } }, cancellationToken, TimeSpan.FromMinutes(10));

    /// <summary>
    /// Relays one action to the account's PC: 204 when it answered with nothing, 200 with the JSON it
    /// answered, else the problem matching its status.
    /// </summary>
    private async Task<IActionResult> AgentActionAsync(string accountId, Capability capability, ClientAction action, CancellationToken ct, TimeSpan? timeout = null) =>
        Desktop(await desktop.RequestAsync(accountId, capability, action, ct, timeout));
}
