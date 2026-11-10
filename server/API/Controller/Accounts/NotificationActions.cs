using System.Net;
using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Services;
using VRCX.Common.Errors;
using VRCX.Common.Services.Ingestion;
using VRCX.Vrchat;

namespace VRCX.API.Controller.Accounts;

public sealed record NotificationResponseBody(string ResponseType, string ResponseData);

public sealed record InviteResponseBody(int ResponseSlot);

// What the Notifications page does to a notification. Nothing here removes a row: declining marks it
// hidden, and deleting from the log only takes it off the page.
public sealed partial class AccountsController
{
    private const int NotificationPageSize = 100;
    private const int NotificationPages = 50;

    /// <summary>Fetch every notification and ignored friend request from VRChat again.</summary>
    /// <response code="204">Refreshed</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/notifications/refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> RefreshNotifications([FromRoute] string accountId, [FromServices] VrchatStore store, [FromServices] NotificationRefreshGate gate, CancellationToken cancellationToken) =>
        gate.RunAsync(accountId, () => RefreshNotificationsAsync(accountId, store, gate, cancellationToken), NoContent, cancellationToken);

    private Task<IActionResult> RefreshNotificationsAsync(string accountId, VrchatStore store, NotificationRefreshGate gate, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            try
            {
                await FetchAllNotificationsAsync(client, accountId, store, cancellationToken);
            }
            catch (VrchatApiException e) when (e.StatusCode == HttpStatusCode.TooManyRequests)
            {
                gate.RateLimited(accountId);
                throw;
            }
        });

    private async Task FetchAllNotificationsAsync(VrchatApiClient client, string accountId, VrchatStore store, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var requests = new HashSet<string>();
        async Task PageAsync<T>(Func<int, Task<IReadOnlyList<T>>> fetch, Func<T, Task> save)
        {
            for (var page = 0; page < NotificationPages; page++)
            {
                var rows = await fetch(page * NotificationPageSize);
                foreach (var row in rows) await save(row);
                if (rows.Count < NotificationPageSize) break;
            }
        }

        await PageAsync(offset => client.GetNotificationsAsync(NotificationPageSize, offset, false, cancellationToken), async n =>
        {
            if (n.Type == "friendRequest") requests.Add(n.Id);
            await store.UpsertNotificationAsync(_db, accountId, n, now, cancellationToken);
        });
        await PageAsync(offset => client.GetNotificationsV2Async(NotificationPageSize, offset, cancellationToken), n =>
            store.UpsertNotificationAsync(_db, accountId, n, now, cancellationToken));
        await PageAsync(offset => client.GetNotificationsAsync(NotificationPageSize, offset, true, cancellationToken), async n =>
        {
            requests.Add(n.Id);
            await store.UpsertNotificationAsync(_db, accountId, n with { Type = "ignoredFriendRequest" }, now, cancellationToken);
        });
        await _db.SaveChangesAsync(cancellationToken);

        // A friend request VRChat no longer lists was answered somewhere: the original drops those on refresh.
        await _db.Notifications
            .Where(n => n.OwnerUserId == accountId && (n.Type == "friendRequest" || n.Type == "ignoredFriendRequest") && !n.Hidden && !requests.Contains(n.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Hidden, true), cancellationToken);
    }

    /// <summary>Tell VRChat the unseen notifications have been seen.</summary>
    /// <response code="204">Done</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/notifications/see-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SeeAllNotifications([FromRoute] string accountId, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            var unseen = await _db.Notifications.Where(n => n.OwnerUserId == accountId && !n.Seen && !n.Hidden).ToListAsync(cancellationToken);
            foreach (var row in unseen)
            {
                try
                {
                    if (row.Version >= 2) await client.SeeNotificationV2Async(row.Id, cancellationToken);
                    else await client.SeeNotificationAsync(row.Id, cancellationToken);
                }
                catch (VrchatApiException e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    // Gone on VRChat's side: nothing left to tell it about.
                }
                row.Seen = true;
            }
            await _db.SaveChangesAsync(cancellationToken);
        });

    /// <summary>Tell VRChat one notification has been seen, as the notification center does for each one it showed.</summary>
    /// <response code="204">Done</response>
    /// <response code="404">No such notification</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/notifications/{notificationId}/see")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> SeeNotification([FromRoute] string accountId, [FromRoute] string notificationId, CancellationToken cancellationToken)
    {
        var row = await _db.Notifications.FirstOrDefaultAsync(n => n.OwnerUserId == accountId && n.Id == notificationId, cancellationToken);
        if (row is null) return Problem(AccountError.NotificationNotFound);
        if (row.Seen) return NoContent();
        return await RunAsync(accountId, async client =>
        {
            try
            {
                if (row.Version >= 2) await client.SeeNotificationV2Async(row.Id, cancellationToken);
                else await client.SeeNotificationAsync(row.Id, cancellationToken);
            }
            catch (VrchatApiException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                // Gone on VRChat's side: nothing left to tell it about.
            }
            row.Seen = true;
            await _db.SaveChangesAsync(cancellationToken);
        });
    }

    /// <summary>Accept the friend request in a notification.</summary>
    /// <response code="204">Accepted</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/notifications/{notificationId}/accept")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> AcceptNotification([FromRoute] string accountId, [FromRoute] string notificationId, [FromServices] VrchatStore store, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            try
            {
                await client.AcceptFriendRequestNotificationAsync(notificationId, cancellationToken);
            }
            catch (VrchatApiException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                // Already gone: the row just stops being actionable, as in the original.
            }
            await store.MarkNotificationAsync(_db, accountId, notificationId, seen: true, hidden: true, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        });

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
    public async Task<IActionResult> HideNotification([FromRoute] string accountId, [FromRoute] string notificationId, CancellationToken cancellationToken)
    {
        var row = await _db.Notifications.FirstOrDefaultAsync(n => n.OwnerUserId == accountId && n.Id == notificationId, cancellationToken);
        if (row is null) return Problem(AccountError.NotificationNotFound);
        return await RunAsync(accountId, async client =>
        {
            if (row.Type == "ignoredFriendRequest" && row.SenderUserId is { } sender) await client.DeleteHiddenFriendRequestAsync(sender, cancellationToken);
            else if (row.Version >= 2) await client.HideNotificationV2Async(notificationId, cancellationToken);
            else await client.HideNotificationAsync(notificationId, cancellationToken);
            row.Hidden = true;
            await _db.SaveChangesAsync(cancellationToken);
        });
    }

    /// <summary>Answer a notification with one of the responses VRChat offered on it.</summary>
    /// <response code="204">Answered, or VRChat refused and the notification was hidden, as the original does</response>
    /// <response code="409">No live session</response>
    [HttpPost("{accountId}/notifications/{notificationId}/respond")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> RespondToNotification([FromRoute] string accountId, [FromRoute] string notificationId, [FromBody] NotificationResponseBody body, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            try
            {
                await client.RespondToNotificationV2Async(notificationId, body.ResponseType, body.ResponseData, cancellationToken);
            }
            catch (VrchatApiException e) when (e is not VrchatUnauthorizedException)
            {
                try
                {
                    await client.HideNotificationV2Async(notificationId, cancellationToken);
                }
                catch (VrchatApiException inner) when (inner is not VrchatUnauthorizedException)
                {
                    // It is expired here whatever VRChat says about it now.
                }
            }
            var row = await _db.Notifications.FirstOrDefaultAsync(n => n.OwnerUserId == accountId && n.Id == notificationId, cancellationToken);
            if (row is not null)
            {
                row.ExpiresAt = DateTimeOffset.UtcNow;
                row.Seen = true;
                await _db.SaveChangesAsync(cancellationToken);
            }
        });

    /// <summary>Answer an invite or invite request with a saved message, then hide it.</summary>
    /// <response code="204">Sent</response>
    /// <response code="409">No live session</response>
    /// <response code="502">VRChat refused</response>
    [HttpPost("{accountId}/notifications/{notificationId}/invite-response")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public Task<IActionResult> SendInviteResponse([FromRoute] string accountId, [FromRoute] string notificationId, [FromBody] InviteResponseBody body, [FromServices] VrchatStore store, CancellationToken cancellationToken) =>
        RunAsync(accountId, async client =>
        {
            await client.SendInviteResponseAsync(notificationId, body.ResponseSlot, cancellationToken);
            await client.HideNotificationAsync(notificationId, cancellationToken);
            await store.MarkNotificationAsync(_db, accountId, notificationId, seen: null, hidden: true, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        });

    /// <summary>Take a notification off the page. The row stays in the database.</summary>
    /// <response code="204">Removed</response>
    /// <response code="404">No such notification</response>
    [HttpDelete("{accountId}/notifications/{notificationId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> RemoveNotificationFromLog([FromRoute] string accountId, [FromRoute] string notificationId, CancellationToken cancellationToken)
    {
        var updated = await _db.Notifications.Where(n => n.OwnerUserId == accountId && n.Id == notificationId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.LogHidden, true), cancellationToken);
        return updated == 0 ? Problem(AccountError.NotificationNotFound) : NoContent();
    }
}
