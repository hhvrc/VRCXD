using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Internal.Common.Problems;
using VRCX.Common;
using VRCX.Common.Authentication;
using VRCX.Common.Errors;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Features.Notifications;
using VRCX.Common.Utils.Pagination;

namespace VRCX.API.Controller.Notifications;

public sealed record NotificationResponseBody(string ResponseType, string ResponseData);

public sealed record InviteResponseBody(int ResponseSlot);

/// <summary>
/// The account's notifications and what the Notifications page does to one. Everything is done by
/// Common/Features/Notifications; the routes stay under /accounts, where they were while they were
/// part of the accounts controller.
/// </summary>
[ApiController]
[Tags("Accounts")]
[ApiVersion("1")]
[Route("/{version:apiVersion}/accounts")]
[Authorize(AuthenticationSchemes = VrcxAuthSchemes.ApiToken)]
public sealed class NotificationsController(AccountQueries accounts, NotificationQueries queries, NotificationActions actions) : VrcxControllerBase
{
    /// <summary>The account's notifications (both feeds), newest first.</summary>
    /// <response code="200">A page of notifications</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/notifications")]
    [ProducesResponseType<PagedResult<NotificationResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListNotifications([FromRoute] string accountId, [FromQuery] PaginationQuery pagination, [FromQuery] bool includeHidden, CancellationToken cancellationToken)
    {
        if (!await accounts.ExistsAsync(accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        return Ok(await queries.Notifications(accountId, includeHidden).ToPagedResultAsync(pagination, cancellationToken));
    }

    /// <summary>Fetch every notification and ignored friend request from VRChat again.</summary>
    /// <response code="204">Refreshed</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/notifications/refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> RefreshNotifications([FromRoute] string accountId, CancellationToken cancellationToken) =>
        Done(await actions.RefreshAsync(accountId, cancellationToken));

    /// <summary>Tell VRChat the unseen notifications have been seen.</summary>
    /// <response code="204">Done</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/notifications/see-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SeeAllNotifications([FromRoute] string accountId, CancellationToken cancellationToken) =>
        Done(await actions.SeeAllAsync(accountId, cancellationToken));

    /// <summary>Tell VRChat one notification has been seen, as the notification center does for each one it showed.</summary>
    /// <response code="204">Done</response>
    /// <response code="404">No such notification</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/notifications/{notificationId}/see")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SeeNotification([FromRoute] string accountId, [FromRoute] string notificationId, CancellationToken cancellationToken) =>
        Done(await actions.SeeAsync(accountId, notificationId, cancellationToken));

    /// <summary>Accept the friend request in a notification.</summary>
    /// <response code="204">Accepted</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/notifications/{notificationId}/accept")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> AcceptNotification([FromRoute] string accountId, [FromRoute] string notificationId, CancellationToken cancellationToken) =>
        Done(await actions.AcceptAsync(accountId, notificationId, cancellationToken));

    /// <summary>Decline a notification on VRChat. The row stays in the log, marked hidden.</summary>
    /// <response code="204">Declined</response>
    /// <response code="404">No such notification</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/notifications/{notificationId}/hide")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> HideNotification([FromRoute] string accountId, [FromRoute] string notificationId, CancellationToken cancellationToken) =>
        Done(await actions.HideAsync(accountId, notificationId, cancellationToken));

    /// <summary>Answer a notification with one of the responses VRChat offered on it.</summary>
    /// <response code="204">Answered, or VRChat refused and the notification was hidden, as the original does</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/notifications/{notificationId}/respond")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> RespondToNotification([FromRoute] string accountId, [FromRoute] string notificationId, [FromBody] NotificationResponseBody body, CancellationToken cancellationToken) =>
        Done(await actions.RespondAsync(accountId, notificationId, body.ResponseType, body.ResponseData, cancellationToken));

    /// <summary>Answer an invite or invite request with a saved message, then hide it.</summary>
    /// <response code="204">Sent</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/notifications/{notificationId}/invite-response")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SendInviteResponse([FromRoute] string accountId, [FromRoute] string notificationId, [FromBody] InviteResponseBody body, CancellationToken cancellationToken) =>
        Done(await actions.SendInviteResponseAsync(accountId, notificationId, body.ResponseSlot, cancellationToken));

    /// <summary>Take a notification off the page. The row stays in the database.</summary>
    /// <response code="204">Removed</response>
    /// <response code="404">No such notification</response>
    [HttpDelete("{accountId}/notifications/{notificationId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> RemoveNotificationFromLog([FromRoute] string accountId, [FromRoute] string notificationId, CancellationToken cancellationToken) =>
        Done(await actions.RemoveFromLogAsync(accountId, notificationId, cancellationToken));

    private IActionResult Done(OpenShockProblem? problem) => problem is null ? NoContent() : Problem(problem);
}
