using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Live;
using VRCX.Common.Services.Live;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Desktop;

/// <summary>What the agent on an account's PC answered: the problem, or its JSON (empty when it answered with nothing).</summary>
public readonly record struct DesktopAnswer(OpenShockProblem? Problem, string? Json);

/// <summary>
/// The agent on the PC an account plays on: which connected agent that is, and asking it to do
/// something. The agent and desktop endpoints have not moved into this folder yet; this is the
/// part other features need.
/// </summary>
public sealed class AccountDesktop(VrcxContext db, DesktopClients desktops)
{
    /// <summary>The PC this account last played on, if connected; else a connected app whose log has not named an account yet.</summary>
    public async Task<DesktopClient?> FindAsync(string accountId, CancellationToken ct)
    {
        var connected = desktops.ConnectedIds;
        if (connected.Count == 0) return null;
        var known = await db.GameLogClients.AsNoTracking()
            .Where(c => connected.Contains(c.ClientId))
            .OrderByDescending(c => c.LastSeenAt)
            .Select(c => new { c.ClientId, c.AccountId })
            .ToListAsync(ct);
        var clientId = known.FirstOrDefault(c => c.AccountId == accountId)?.ClientId
            ?? known.FirstOrDefault(c => c.AccountId is null)?.ClientId
            ?? connected.FirstOrDefault(id => known.All(c => c.ClientId != id));
        return clientId is null ? null : desktops.Get(clientId);
    }

    /// <summary>Relays one action to the account's PC and answers what it said, or the problem matching its status.</summary>
    public async Task<DesktopAnswer> RequestAsync(string accountId, Capability capability, ClientAction action, CancellationToken ct, TimeSpan? timeout = null)
    {
        if (await FindAsync(accountId, ct) is not { } desktop) return new DesktopAnswer(DesktopError.NotConnected, null);
        if (!desktop.Can(capability)) return new DesktopAnswer(AgentError.Unsupported, null);

        var result = await desktops.RequestAsync(desktop.ClientId, action, ct, timeout);
        return result?.Status switch
        {
            null => new DesktopAnswer(DesktopError.NoAnswer, null),
            ActionStatus.Ok => new DesktopAnswer(null, result.Json),
            ActionStatus.Unsupported => new DesktopAnswer(AgentError.Unsupported, null),
            ActionStatus.NotConfirmed => new DesktopAnswer(AgentError.NotConfirmed, null),
            ActionStatus.NotFound => new DesktopAnswer(AgentError.NotFound(result.Error), null),
            ActionStatus.GameRunning => new DesktopAnswer(DesktopError.GameRunning, null),
            _ => new DesktopAnswer(DesktopError.ActionFailed(result.Error), null),
        };
    }
}
