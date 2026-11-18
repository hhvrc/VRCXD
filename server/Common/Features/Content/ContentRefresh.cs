using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Features.Accounts;
using VRCX.Common.Services;
using VRCX.Common.Services.Sync;
using VRCX.Vrchat;

namespace VRCX.Common.Features.Content;

/// <summary>
/// The refresh=true half of the get-by-id endpoints: fetch a user, world, avatar, group or
/// instance from VRChat and store it, before the stored row is read. The fetch goes through the
/// asked-for account's session when it has one, else any live one. Users, worlds, groups and
/// instances are fetched at most once per <see cref="VrchatReadCache.RefreshMinAge"/> per account
/// and target; a refresh asked for sooner does nothing and still succeeds. A method returns null
/// when it worked (or was not due), else the problem to answer with.
/// </summary>
public sealed class ContentRefresh(AccountSessionRunner runner, IAccountSyncService sync, VrchatReadCache cache)
{
    public Task<OpenShockProblem?> UserAsync(string? account, string userId, CancellationToken ct) =>
        RefreshAsync(account, "refresh-user", userId, via => sync.RefreshUserAsync(via, userId, ct),
            // The dialog's refresh reads its tabs again too.
            via => cache.Forget([.. new[] { "mutual-counts", "public-profile", "user-groups" }.Select(tab => VrchatReadCache.Key(via, tab, userId))]));

    public Task<OpenShockProblem?> WorldAsync(string? account, string worldId, CancellationToken ct) =>
        RefreshAsync(account, "refresh-world", worldId, via => sync.RefreshWorldAsync(via, worldId, ct));

    /// <summary>An avatar is fetched every time it is asked for: its endpoint has no minimum gap.</summary>
    public Task<OpenShockProblem?> AvatarAsync(string? account, string avatarId, CancellationToken ct) =>
        RefreshAsync(account, null, avatarId, via => sync.RefreshAvatarAsync(via, avatarId, ct));

    public Task<OpenShockProblem?> GroupAsync(string? account, string groupId, CancellationToken ct) =>
        RefreshAsync(account, "refresh-group", groupId, via => sync.RefreshGroupAsync(via, groupId, ct));

    /// <param name="account">The account to fetch with, when it has a session.</param>
    /// <param name="location">The instance's own location tag (VrchatLocation.InstanceLocation).</param>
    /// <param name="ct"></param>
    public Task<OpenShockProblem?> InstanceAsync(string? account, string location, CancellationToken ct) =>
        RefreshAsync(account, "refresh-instance", location, via => sync.RefreshInstanceAsync(via, location, ct));

    private async Task<OpenShockProblem?> RefreshAsync(string? account, string? throttle, string target, Func<string, Task> refresh, Action<string>? ran = null)
    {
        var via = runner.PickAccount(account);
        if (via is null) return AccountError.NoAccounts;
        try
        {
            if (throttle is null) await refresh(via);
            else if (await cache.ThrottleAsync(VrchatReadCache.Key(via, throttle, target), VrchatReadCache.RefreshMinAge, () => refresh(via))) ran?.Invoke(via);
        }
        catch (VrchatApiException ex)
        {
            return ex is VrchatUnauthorizedException ? VrchatError.SessionExpired : VrchatError.Upstream(ex.Message);
        }
        return null;
    }
}
