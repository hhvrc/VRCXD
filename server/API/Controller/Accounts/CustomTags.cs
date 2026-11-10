using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Live;
using VRCX.Common.Services.Live;

namespace VRCX.API.Controller.Accounts;

/// <summary>A tag another program on the account's PC gave a user over the VRCX named pipe.</summary>
public sealed record CustomTagResponse(string UserId, string Tag, string? Colour);

public sealed record ShowUserDialogBody(string UserId);

// The VRCX named pipe on the account's PC (the original's Dotnet/IPC): the tags programs on it set,
// and what VRCX tells them.
public sealed partial class AccountsController
{
    /// <summary>The account's custom user tags (the original's customUserTags).</summary>
    /// <response code="200">The tags</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/custom-tags")]
    [ProducesResponseType<CustomTagResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListCustomTags([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        var tags = await _db.CustomUserTags.AsNoTracking().Where(t => t.OwnerUserId == accountId)
            .OrderBy(t => t.UserId)
            .Select(t => new CustomTagResponse(t.UserId, t.Tag, t.Colour))
            .ToArrayAsync(cancellationToken);
        return Ok(tags);
    }

    /// <summary>
    /// Tells the programs on the account's PC pipe that a user dialog opened (the original's
    /// SendIpc ShowUserDialog). Nothing happens when no desktop app is connected.
    /// </summary>
    /// <response code="204">Sent, or nobody to send it to</response>
    [HttpPost("{accountId}/desktop/ipc/user-dialog")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SendShowUserDialog([FromRoute] string accountId, [FromBody] ShowUserDialogBody body, [FromServices] DesktopClients desktops, CancellationToken cancellationToken)
    {
        if (await FindDesktopAsync(accountId, desktops, cancellationToken) is { } desktop)
            await desktops.PostAsync(desktop.ClientId, new ClientAction { SendIpc = new SendIpc { Type = "VrcxMessage", MsgType = "ShowUserDialog", Data = body.UserId } }, cancellationToken);
        return NoContent();
    }
}
