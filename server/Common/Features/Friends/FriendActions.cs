using VRCX.Common.Problems;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Friends;

/// <summary>
/// What an account does about its friends through VRCXD: friend requests, unfriending, loading a
/// friend's profile and reading the friend list again. Each is done on VRChat with the account's own
/// session and then recorded through <see cref="FriendWriter"/>, so the friends endpoints hold no
/// session or database code. A method returns null when it worked, else the problem to answer with.
/// </summary>
public sealed class FriendActions(VrcxContext db, AccountSessionRunner runner, IAccountSyncService sync, FriendWriter writer)
{
    /// <summary>Sends a friend request and logs it in the friend log.</summary>
    public Task<VrcxProblem?> SendRequestAsync(string accountId, string userId, CancellationToken ct) =>
        runner.RunAsync(accountId, async client =>
        {
            await client.SendFriendRequestAsync(userId, ct);
            await FriendWriter.LogRequestAsync(db, accountId, userId, "FriendRequest", DateTimeOffset.UtcNow, ct);
        }, ct);

    /// <summary>Withdraws a friend request the account sent and logs it in the friend log.</summary>
    public Task<VrcxProblem?> CancelRequestAsync(string accountId, string userId, CancellationToken ct) =>
        runner.RunAsync(accountId, async client =>
        {
            await client.CancelFriendRequestAsync(userId, ct);
            await FriendWriter.LogRequestAsync(db, accountId, userId, "CancelFriendRequest", DateTimeOffset.UtcNow, ct);
        }, ct);

    /// <summary>
    /// Unfriends on VRChat and ends the friendship here at once, rather than when the pipeline's
    /// friend-delete arrives.
    /// </summary>
    public Task<VrcxProblem?> UnfriendAsync(string accountId, string userId, CancellationToken ct) =>
        runner.RunAsync(accountId, async client =>
        {
            await client.UnfriendAsync(userId, ct);
            await writer.UnfriendAsync(db, accountId, userId, DateTimeOffset.UtcNow, ct);
        }, ct);

    /// <summary>Fetches a friend's full user object and profile from VRChat now: the Friend List's load missing entries.</summary>
    public Task<VrcxProblem?> LoadAsync(string accountId, string userId, CancellationToken ct) =>
        runner.RunAsync(accountId, async _ => await sync.RefreshUserAsync(accountId, userId, ct), ct);

    /// <summary>Reads the friend list from VRChat again, as the sidebar's refresh button does.</summary>
    public Task<VrcxProblem?> RefreshAsync(string accountId, CancellationToken ct) =>
        runner.RunAsync(accountId, _ => sync.SyncFriendsAsync(accountId, ct), ct);

    /// <summary>Removes a row from the account's friend log view.</summary>
    public Task HideLogEntryAsync(string accountId, string key, CancellationToken ct) =>
        FriendWriter.HideLogEntryAsync(db, accountId, key, DateTimeOffset.UtcNow, ct);
}
