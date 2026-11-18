using VRCX.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Services.Sessions;
using VRCX.Vrchat;

namespace VRCX.Common.Features.Accounts;

/// <summary>
/// Runs what a feature does on VRChat with an account's own session, and turns the ways that can
/// fail into the problem to answer with, so a feature's actions class holds no session code of its
/// own. The accounts controller's RunAsync does the same for what has not moved to a feature yet.
/// </summary>
public sealed class AccountSessionRunner(IAccountSessionManager sessions)
{
    /// <summary>
    /// Picks the VRChat session a read-through should use: the requested account when it has a
    /// live session, otherwise any live account, otherwise null (the caller answers with
    /// <see cref="AccountError.NoAccounts"/>).
    /// </summary>
    public string? PickAccount(string? requested) =>
        requested is not null && sessions.Get(requested) is not null ? requested : sessions.ActiveAccountIds.FirstOrDefault();

    /// <summary>Whether this server holds a live VRChat session for the account right now.</summary>
    public bool HasSession(string accountId) => sessions.Get(accountId) is not null;

    /// <summary>
    /// Null when the action ran. Otherwise the problem: no live session, a session VRChat no longer
    /// accepts (which is then marked invalid, and not retried), or VRChat's own refusal.
    /// </summary>
    public async Task<VrcxProblem?> RunAsync(string accountId, Func<VrchatApiClient, Task> action, CancellationToken ct)
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

    /// <summary>
    /// The same for something read live from VRChat: the value, or the problem it failed with. A
    /// read that may answer nothing (VRChat has no such thing) is one of a nullable type, and
    /// nothing is then its value, not a problem.
    /// </summary>
    public async Task<Result<T>> ReadAsync<T>(string accountId, Func<VrchatApiClient, Task<T>> read, CancellationToken ct)
    {
        var client = sessions.Get(accountId);
        if (client is null) return AccountError.NoLiveSession;
        try
        {
            return await read(client);
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
    }
}
