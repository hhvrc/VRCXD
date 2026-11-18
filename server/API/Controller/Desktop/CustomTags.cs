using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VRCX.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Features.Desktop;
using VRCX.Common.Live;
using VRCX.Common.Services.Live;

namespace VRCX.API.Controller.Desktop;

public sealed record ShowUserDialogBody(string UserId);

// The VRCX named pipe on the account's PC (the original's Dotnet/IPC): the tags programs on it set,
// and what VRCX tells them.
public sealed partial class AccountDesktopController
{
    /// <summary>The account's custom user tags (the original's customUserTags).</summary>
    /// <response code="200">The tags</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/custom-tags")]
    [ProducesResponseType<CustomTagResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<VrcxProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListCustomTags([FromRoute] string accountId, CancellationToken cancellationToken)
    {
        if (!await accounts.ExistsAsync(accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await desktop.CustomTagsAsync(accountId, cancellationToken));
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
        if (await desktop.FindAsync(accountId, cancellationToken) is { } pc)
            await desktops.PostAsync(pc.ClientId, new ClientAction { SendIpc = new SendIpc { Type = "VrcxMessage", MsgType = "ShowUserDialog", Data = body.UserId } }, cancellationToken);
        return NoContent();
    }
}
