using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Services.Sessions;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.Common.Features.Friends;

/// <summary>
/// What an account does about its friends through VRCXD: friend requests, unfriending, loading a
/// friend's profile and reading the friend list again. Each is done on VRChat with the account's own
/// session and then recorded through <see cref="FriendWriter"/>, so the friends endpoints hold no
/// session or database code. A method returns null when it worked, else the problem to answer with.
/// </summary>
public sealed class FriendActions(VrcxContext db, IAccountSessionManager sessions, IAccountSyncService sync, FriendWriter writer)
{
    /// <summary>Sends a friend request and logs it in the friend log.</summary>
    public Task<OpenShockProblem?> SendRequestAsync(string accountId, string userId, CancellationToken ct) =>
        RunAsync(accountId, async client =>
        {
            await client.SendFriendRequestAsync(userId, ct);
            await FriendWriter.LogRequestAsync(db, accountId, userId, "FriendRequest", DateTimeOffset.UtcNow, ct);
        }, ct);

    /// <summary>Withdraws a friend request the account sent and logs it in the friend log.</summary>
    public Task<OpenShockProblem?> CancelRequestAsync(string accountId, string userId, CancellationToken ct) =>
        RunAsync(accountId, async client =>
        {
            await client.CancelFriendRequestAsync(userId, ct);
            await FriendWriter.LogRequestAsync(db, accountId, userId, "CancelFriendRequest", DateTimeOffset.UtcNow, ct);
        }, ct);

    /// <summary>
    /// Unfriends on VRChat and ends the friendship here at once, rather than when the pipeline's
    /// friend-delete arrives.
    /// </summary>
    public Task<OpenShockProblem?> UnfriendAsync(string accountId, string userId, CancellationToken ct) =>
        RunAsync(accountId, async client =>
        {
            await client.UnfriendAsync(userId, ct);
            await writer.UnfriendAsync(db, accountId, userId, DateTimeOffset.UtcNow, ct);
        }, ct);

    /// <summary>Fetches a friend's full user object and profile from VRChat now: the Friend List's load missing entries.</summary>
    public Task<OpenShockProblem?> LoadAsync(string accountId, string userId, CancellationToken ct) =>
        RunAsync(accountId, async _ => await sync.RefreshUserAsync(accountId, userId, ct), ct);

    /// <summary>Reads the friend list from VRChat again, as the sidebar's refresh button does.</summary>
    public Task<OpenShockProblem?> RefreshAsync(string accountId, CancellationToken ct) =>
        RunAsync(accountId, _ => sync.SyncFriendsAsync(accountId, ct), ct);

    /// <summary>Removes a row from the account's friend log view.</summary>
    public Task HideLogEntryAsync(string accountId, string key, CancellationToken ct) =>
        FriendWriter.HideLogEntryAsync(db, accountId, key, DateTimeOffset.UtcNow, ct);

    private async Task<OpenShockProblem?> RunAsync(string accountId, Func<VrchatApiClient, Task> action, CancellationToken ct)
    {
        var client = sessions.Get(accountId);
        if (client is null) return AccountError.NoLiveSession;
        try
        {
            await action(client);
        }
        catch (VrchatUnauthorizedException)
        {
            await sessions.InvalidateAsync(accountId, ct);
            return VrchatError.SessionExpired;
        }
        catch (VrchatApiException ex)
        {
            return VrchatError.Upstream(ex.Message);
        }
        return null;
    }
}
