using System.Net.Mime;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Live;
using VRCX.Common.Services.Live;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Controller.Accounts;

public sealed record VrchatConfigBody(string Json);

/// <summary>A registry backup to write, as GET agent/vrchat-registry answers it.</summary>
public sealed record RegistryRestoreBody(JsonElement Data, bool Confirm);

/// <summary>One PlayerPrefs value: the value and the original's type number (3 binary, 4 DWORD, 100 float).</summary>
public sealed record RegistryKeyBody(JsonElement Value, int Type);

public sealed record LocalModerationBody(int Type);

public sealed record RegistryBackupBody(string Name);

public sealed record RegistryBackupResponse(long Id, string Name, DateTimeOffset Date, JsonElement Data);

public sealed record RegistryCheckResponse(bool AskRestore);

// VRChat's own settings on the PC: config.json, the registry (with backups kept here) and the
// local show/hide avatar list.
public sealed partial class AccountsController
{
    /// <summary>VRChat's config.json on the PC (ReadConfigFile).</summary>
    /// <response code="200">{"exists": bool, "json": "the file"}</response>
    [HttpGet("{accountId}/agent/vrchat-config")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentReadVrchatConfig([FromRoute] string accountId, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        AgentActionAsync(accountId, Capability.VrchatConfig, desktops, new ClientAction { ReadVrchatConfig = new LogNoData() }, cancellationToken);

    /// <summary>Writes VRChat's config.json on the PC (WriteConfigFile); a JSON object only.</summary>
    [HttpPut("{accountId}/agent/vrchat-config")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentWriteVrchatConfig([FromRoute] string accountId, [FromBody] VrchatConfigBody body, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        AgentActionAsync(accountId, Capability.VrchatConfig, desktops, new ClientAction { WriteVrchatConfig = new WriteVrchatConfig { Json = body.Json } }, cancellationToken);

    /// <summary>VRChat's whole registry on the PC (GetVRChatRegistry); 404 when VRChat has none.</summary>
    [HttpGet("{accountId}/agent/vrchat-registry")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentReadVrchatRegistry([FromRoute] string accountId, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        AgentActionAsync(accountId, Capability.Registry, desktops, new ClientAction { ReadVrchatRegistry = new LogNoData() }, cancellationToken);

    /// <summary>Writes a backup into VRChat's registry (SetVRChatRegistry), e.g. one restored from a file.</summary>
    [HttpPost("{accountId}/agent/vrchat-registry/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentRestoreVrchatRegistry([FromRoute] string accountId, [FromBody] RegistryRestoreBody body, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        AgentActionAsync(accountId, Capability.Registry, desktops,
            new ClientAction { WriteVrchatRegistry = new WriteVrchatRegistry { Json = body.Data.GetRawText(), Confirm = body.Confirm } }, cancellationToken);

    /// <summary>Deletes VRChat's registry key on the PC (DeleteVRChatRegistryFolder): VRChat starts over with its defaults.</summary>
    [HttpPost("{accountId}/agent/vrchat-registry/delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentDeleteVrchatRegistry([FromRoute] string accountId, [FromBody] ConfirmBody body, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        AgentActionAsync(accountId, Capability.Registry, desktops, new ClientAction { DeleteVrchatRegistry = new Confirm { Confirm_ = body.Confirm } }, cancellationToken);

    /// <summary>One PlayerPrefs value (GetVRChatRegistryKey).</summary>
    /// <response code="200">{"value": the value or null}</response>
    [HttpGet("{accountId}/agent/vrchat-registry/keys/{key}")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentReadVrchatRegistryKey([FromRoute] string accountId, [FromRoute] string key, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        AgentActionAsync(accountId, Capability.Registry, desktops, new ClientAction { ReadVrchatRegistryKey = new ReadVrchatRegistryKey { Key = key } }, cancellationToken);

    /// <summary>Sets one PlayerPrefs value (SetVRChatRegistryKey); 404 when VRChat has no registry key yet.</summary>
    [HttpPut("{accountId}/agent/vrchat-registry/keys/{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentSetVrchatRegistryKey([FromRoute] string accountId, [FromRoute] string key, [FromBody] RegistryKeyBody body, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        AgentActionAsync(accountId, Capability.Registry, desktops,
            new ClientAction { SetVrchatRegistryKey = new SetVrchatRegistryKey { Key = key, ValueJson = body.Value.GetRawText(), Type = body.Type } }, cancellationToken);

    /// <summary>The account's local show/hide avatar list on the PC (GetVRChatModerations).</summary>
    /// <response code="200">{"usr_...": 4 (hide) | 5 (show)}, or null when VRChat has no list yet</response>
    [HttpGet("{accountId}/agent/local-moderations")]
    [ProducesResponseType<JsonElement>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentReadLocalModerations([FromRoute] string accountId, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        AgentActionAsync(accountId, Capability.LocalModerations, desktops,
            new ClientAction { ReadLocalModerations = new ReadLocalModerations { CurrentUserId = accountId } }, cancellationToken);

    /// <summary>Hides (4) or shows (5) a user's avatar, or 0 to go back to the default (SetVRChatUserModeration).</summary>
    [HttpPut("{accountId}/agent/local-moderations/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AgentSetLocalModeration([FromRoute] string accountId, [FromRoute] string userId, [FromBody] LocalModerationBody body, [FromServices] DesktopClients desktops, CancellationToken cancellationToken) =>
        AgentActionAsync(accountId, Capability.LocalModerations, desktops,
            new ClientAction { SetLocalModeration = new SetLocalModeration { CurrentUserId = accountId, UserId = userId, Type = body.Type } }, cancellationToken);

    /// <summary>The account's registry backups, newest first.</summary>
    [HttpGet("{accountId}/registry-backups")]
    [ProducesResponseType<RegistryBackupResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListRegistryBackups([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        var rows = await _db.VrchatRegistryBackups.AsNoTracking().Where(b => b.AccountId == accountId)
            .OrderByDescending(b => b.CreatedAt).ToListAsync(cancellationToken);
        return Ok(rows.Select(ToResponse));
    }

    /// <summary>Backs up the PC's registry now under a name (backupVrcRegistry).</summary>
    [HttpPost("{accountId}/registry-backups")]
    [ProducesResponseType<RegistryBackupResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> CreateRegistryBackup([FromRoute] string accountId, [FromBody] RegistryBackupBody body, [FromServices] DesktopClients desktops, [FromServices] RegistryBackups backups, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Name)) return Problem(AgentError.NotConfirmed);
        if (await FindDesktopAsync(accountId, desktops, cancellationToken) is not { } desktop) return Problem(DesktopError.NotConnected);
        if (!desktop.Can(Capability.Registry)) return Problem(AgentError.Unsupported);
        var read = await backups.ReadAsync(desktop, cancellationToken);
        if (read.Missing) return Problem(AgentError.NotFound(read.Error));
        if (read.Data is null) return Problem(DesktopError.ActionFailed(read.Error));
        var backup = new VrchatRegistryBackup { AccountId = accountId, Name = body.Name.Trim(), CreatedAt = DateTimeOffset.UtcNow, Data = read.Data };
        _db.VrchatRegistryBackups.Add(backup);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(backup));
    }

    /// <summary>Writes a kept backup back into the PC's registry (restoreVrcRegistryBackup).</summary>
    [HttpPost("{accountId}/registry-backups/{id:long}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> RestoreRegistryBackup([FromRoute] string accountId, [FromRoute] long id, [FromBody] ConfirmBody body, [FromServices] DesktopClients desktops, CancellationToken cancellationToken)
    {
        var backup = await _db.VrchatRegistryBackups.AsNoTracking().FirstOrDefaultAsync(b => b.AccountId == accountId && b.Id == id, cancellationToken);
        if (backup is null) return Problem(AgentError.NotFound("No such backup"));
        return await AgentActionAsync(accountId, Capability.Registry, desktops,
            new ClientAction { WriteVrchatRegistry = new WriteVrchatRegistry { Json = backup.Data.RootElement.GetRawText(), Confirm = body.Confirm } }, cancellationToken);
    }

    /// <summary>Forgets a backup (deleteVrcRegistryBackup).</summary>
    [HttpDelete("{accountId}/registry-backups/{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> DeleteRegistryBackup([FromRoute] string accountId, [FromRoute] long id, CancellationToken cancellationToken)
    {
        await _db.VrchatRegistryBackups.Where(b => b.AccountId == accountId && b.Id == id).ExecuteDeleteAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// checkAutoBackupRestoreVrcRegistry, called as the web client starts: askRestore says VRChat's
    /// registry is gone and the backups should be offered; otherwise an auto backup is made when due.
    /// </summary>
    [HttpPost("{accountId}/registry-backups/check")]
    [ProducesResponseType<RegistryCheckResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> CheckRegistryBackups([FromRoute] string accountId, [FromServices] DesktopClients desktops, [FromServices] RegistryBackups backups, CancellationToken cancellationToken)
    {
        if (await FindDesktopAsync(accountId, desktops, cancellationToken) is not { } desktop || !desktop.Can(Capability.Registry))
            return Ok(new RegistryCheckResponse(false));
        return Ok(new RegistryCheckResponse(await backups.CheckAsync(accountId, desktop, cancellationToken)));
    }

    private static RegistryBackupResponse ToResponse(VrchatRegistryBackup b) => new(b.Id, b.Name, b.CreatedAt, b.Data.RootElement.Clone());
}
