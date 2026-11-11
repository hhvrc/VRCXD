using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Live;
using VRCX.Common.Services.Images;
using VRCX.Common.Services.Live;

namespace VRCX.API.Controller.Accounts;

/// <summary>
/// A notification for the PC (the original's displayDesktopToast, displayXSNotification and
/// displayOvrtNotification). The page decides whether to show it; Timeout is in seconds and
/// Opacity from 0 to 1, as the original passes them.
/// </summary>
public sealed record AgentNotificationBody(
    string Title,
    string Body,
    string OverlayText,
    string? ImageUrl,
    bool Desktop,
    bool Xsoverlay,
    bool OvrtoolkitHud,
    bool OvrtoolkitWrist,
    int Timeout,
    double Opacity,
    bool SteamvrOverlay = false,
    string? Position = null,
    bool Dark = false);

public sealed partial class AccountsController
{
    /// <summary>Shows a notification on the account's PC: the desktop toast, XSOverlay, OVR Toolkit and the agent's own SteamVR overlay. The image is downloaded here and sent along.</summary>
    /// <response code="204">Shown</response>
    /// <response code="409">No PC that can</response>
    [HttpPost("{accountId}/agent/notify")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> AgentNotify([FromRoute] string accountId, [FromBody] AgentNotificationBody body, [FromServices] DesktopClients desktops, [FromServices] ImageCache images, CancellationToken cancellationToken)
    {
        var notification = new ShowNotification
        {
            Title = body.Title,
            Body = body.Body,
            OverlayText = body.OverlayText,
            ImageUrl = body.ImageUrl ?? "",
            Desktop = body.Desktop,
            Xsoverlay = body.Xsoverlay,
            OvrtoolkitHud = body.OvrtoolkitHud,
            OvrtoolkitWrist = body.OvrtoolkitWrist,
            Timeout = body.Timeout,
            Opacity = body.Opacity,
            SteamvrOverlay = body.SteamvrOverlay,
            Position = body.Position ?? "topCenter",
            Dark = body.Dark,
        };
        // notySaveImage: a picture that cannot be had leaves the notification without one.
        if (body.ImageUrl is { Length: > 0 } url && await images.GetAsync(url, cancellationToken) is { } image)
            notification.Image = Google.Protobuf.ByteString.CopyFrom(await System.IO.File.ReadAllBytesAsync(image.Path, cancellationToken));
        return await AgentActionAsync(accountId, Capability.Notifications, desktops, new ClientAction { ShowNotification = notification }, cancellationToken);
    }
}
