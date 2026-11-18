using System.Net;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.Common.Features.Notifications;

/// <summary>
/// What the Notifications page does to a notification, done on VRChat with the account's own
/// session and then recorded here. Nothing removes a row: declining marks it hidden, and deleting
/// from the log only takes it off the page. A method returns null when it worked, else the problem
/// to answer with.
/// </summary>
public sealed class NotificationActions(VrcxContext db, AccountSessionRunner runner, VrchatStore store, NotificationRefreshGate gate)
{
    private const int PageSize = 100;
    private const int Pages = 50;

    /// <summary>
    /// Fetches every notification and ignored friend request from VRChat again. A refresh asked for
    /// while one runs, or while the account backs off after a 429, does nothing and still succeeds.
    /// </summary>
    public Task<OpenShockProblem?> RefreshAsync(string accountId, CancellationToken ct) =>
        gate.RunAsync<OpenShockProblem?>(accountId, () => runner.RunAsync(accountId, async client =>
        {
            try
            {
                await FetchAllAsync(client, accountId, ct);
            }
            catch (VrchatApiException e) when (e.StatusCode == HttpStatusCode.TooManyRequests)
            {
                gate.RateLimited(accountId);
                throw;
            }
        }, ct), () => null, ct);

    private async Task FetchAllAsync(VrchatApiClient client, string accountId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var requests = new HashSet<string>();
        async Task PageAsync<T>(Func<int, Task<IReadOnlyList<T>>> fetch, Func<T, Task> save)
        {
            for (var page = 0; page < Pages; page++)
            {
                var rows = await fetch(page * PageSize);
                foreach (var row in rows) await save(row);
                if (rows.Count < PageSize) break;
            }
        }

        await PageAsync(offset => client.GetNotificationsAsync(PageSize, offset, false, ct), async n =>
        {
            if (n.Type == "friendRequest") requests.Add(n.Id);
            await store.UpsertNotificationAsync(db, accountId, n, now, ct);
        });
        await PageAsync(offset => client.GetNotificationsV2Async(PageSize, offset, ct), n =>
            store.UpsertNotificationAsync(db, accountId, n, now, ct));
        await PageAsync(offset => client.GetNotificationsAsync(PageSize, offset, true, ct), async n =>
        {
            requests.Add(n.Id);
            await store.UpsertNotificationAsync(db, accountId, n with { Type = "ignoredFriendRequest" }, now, ct);
        });
        await db.SaveChangesAsync(ct);

        // A friend request VRChat no longer lists was answered somewhere: the original drops those on refresh.
        await db.Notifications
            .Where(n => n.OwnerUserId == accountId && (n.Type == "friendRequest" || n.Type == "ignoredFriendRequest") && !n.Hidden && !requests.Contains(n.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Hidden, true), ct);
    }

    /// <summary>Tells VRChat the unseen notifications have been seen.</summary>
    public Task<OpenShockProblem?> SeeAllAsync(string accountId, CancellationToken ct) =>
        runner.RunAsync(accountId, async client =>
        {
            var unseen = await db.Notifications.Where(n => n.OwnerUserId == accountId && !n.Seen && !n.Hidden).ToListAsync(ct);
            foreach (var row in unseen)
            {
                await SeeOnVrchatAsync(client, row, ct);
                row.Seen = true;
            }
            await db.SaveChangesAsync(ct);
        }, ct);

    /// <summary>
    /// Tells VRChat one notification has been seen, as the notification center does for each one it
    /// showed. One that is already seen needs no session and no call.
    /// </summary>
    public async Task<OpenShockProblem?> SeeAsync(string accountId, string notificationId, CancellationToken ct)
    {
        var row = await db.Notifications.FirstOrDefaultAsync(n => n.OwnerUserId == accountId && n.Id == notificationId, ct);
        if (row is null) return AccountError.NotificationNotFound;
        if (row.Seen) return null;
        return await runner.RunAsync(accountId, async client =>
        {
            await SeeOnVrchatAsync(client, row, ct);
            row.Seen = true;
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    private static async Task SeeOnVrchatAsync(VrchatApiClient client, Notification row, CancellationToken ct)
    {
        try
        {
            if (row.Version >= 2) await client.SeeNotificationV2Async(row.Id, ct);
            else await client.SeeNotificationAsync(row.Id, ct);
        }
        catch (VrchatApiException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            // Gone on VRChat's side: nothing left to tell it about.
        }
    }

    /// <summary>Accepts the friend request in a notification.</summary>
    public Task<OpenShockProblem?> AcceptAsync(string accountId, string notificationId, CancellationToken ct) =>
        runner.RunAsync(accountId, async client =>
        {
            try
            {
                await client.AcceptFriendRequestNotificationAsync(notificationId, ct);
            }
            catch (VrchatApiException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                // Already gone: the row just stops being actionable, as in the original.
            }
            await store.MarkNotificationAsync(db, accountId, notificationId, seen: true, hidden: true, ct);
            await db.SaveChangesAsync(ct);
        }, ct);

    /// <summary>Declines a notification on VRChat. The row stays in the log, marked hidden.</summary>
    public async Task<OpenShockProblem?> HideAsync(string accountId, string notificationId, CancellationToken ct)
    {
        var row = await db.Notifications.FirstOrDefaultAsync(n => n.OwnerUserId == accountId && n.Id == notificationId, ct);
        if (row is null) return AccountError.NotificationNotFound;
        return await runner.RunAsync(accountId, async client =>
        {
            if (row.Type == "ignoredFriendRequest" && row.SenderUserId is { } sender) await client.DeleteHiddenFriendRequestAsync(sender, ct);
            else if (row.Version >= 2) await client.HideNotificationV2Async(notificationId, ct);
            else await client.HideNotificationAsync(notificationId, ct);
            row.Hidden = true;
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    /// <summary>
    /// Answers a notification with one of the responses VRChat offered on it. When VRChat refuses,
    /// the notification is hidden there instead and still counts as answered, as the original does.
    /// </summary>
    public Task<OpenShockProblem?> RespondAsync(string accountId, string notificationId, string responseType, string responseData, CancellationToken ct) =>
        runner.RunAsync(accountId, async client =>
        {
            try
            {
                await client.RespondToNotificationV2Async(notificationId, responseType, responseData, ct);
            }
            catch (VrchatApiException e) when (e is not VrchatUnauthorizedException)
            {
                try
                {
                    await client.HideNotificationV2Async(notificationId, ct);
                }
                catch (VrchatApiException inner) when (inner is not VrchatUnauthorizedException)
                {
                    // It is expired here whatever VRChat says about it now.
                }
            }
            var row = await db.Notifications.FirstOrDefaultAsync(n => n.OwnerUserId == accountId && n.Id == notificationId, ct);
            if (row is not null)
            {
                row.ExpiresAt = DateTimeOffset.UtcNow;
                row.Seen = true;
                await db.SaveChangesAsync(ct);
            }
        }, ct);

    /// <summary>Answers an invite or invite request with a saved message, then hides it.</summary>
    public Task<OpenShockProblem?> SendInviteResponseAsync(string accountId, string notificationId, int responseSlot, CancellationToken ct) =>
        runner.RunAsync(accountId, async client =>
        {
            await client.SendInviteResponseAsync(notificationId, responseSlot, ct);
            await client.HideNotificationAsync(notificationId, ct);
            await store.MarkNotificationAsync(db, accountId, notificationId, seen: null, hidden: true, ct);
            await db.SaveChangesAsync(ct);
        }, ct);

    /// <summary>Takes a notification off the page. The row stays in the database.</summary>
    public async Task<OpenShockProblem?> RemoveFromLogAsync(string accountId, string notificationId, CancellationToken ct)
    {
        var updated = await db.Notifications.Where(n => n.OwnerUserId == accountId && n.Id == notificationId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.LogHidden, true), ct);
        return updated == 0 ? AccountError.NotificationNotFound : null;
    }
}
