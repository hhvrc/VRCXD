using OpenShock.Internal.Common.Problems;
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
    /// Null when the action ran. Otherwise the problem: no live session, a session VRChat no longer
    /// accepts (which is then marked invalid, and not retried), or VRChat's own refusal.
    /// </summary>
    public async Task<OpenShockProblem?> RunAsync(string accountId, Func<VrchatApiClient, Task> action, CancellationToken ct)
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
    /// read may answer nothing (VRChat has no such thing), which is a value of null and no problem.
    /// </summary>
    public async Task<LiveRead<T>> ReadAsync<T>(string accountId, Func<VrchatApiClient, Task<T>> read, CancellationToken ct)
    {
        var client = sessions.Get(accountId);
        if (client is null) return new LiveRead<T>(default, AccountError.NoLiveSession);
        try
        {
            return new LiveRead<T>(await read(client), null);
        }
        catch (VrchatUnauthorizedException)
        {
            await sessions.InvalidateAsync(accountId, ct);
            return new LiveRead<T>(default, VrchatError.SessionExpired);
        }
        catch (VrchatApiException ex)
        {
            return new LiveRead<T>(default, VrchatError.Upstream(ex.Message));
        }
    }
}

/// <summary>What <see cref="AccountSessionRunner.ReadAsync{T}"/> came back with: the value when <see cref="Problem"/> is null.</summary>
public readonly record struct LiveRead<T>(T? Value, OpenShockProblem? Problem);
