using Microsoft.EntityFrameworkCore;
using VRCX.Common.Problems;
using VRCX.Common.Errors;
using VRCX.Common.Live;
using VRCX.Common.Services.Live;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Desktop;

/// <summary>A tag another program on the account's PC gave a user over the VRCX named pipe.</summary>
public sealed record CustomTagResponse(string UserId, string Tag, string? Colour);

/// <summary>The game session times of the PC an account played on last.</summary>
public sealed record DesktopSessionTimes(DateTimeOffset? GameStartedAt, DateTimeOffset? LastGameStartedAt, DateTimeOffset? LastGameStoppedAt, bool GameNoVr);

/// <summary>Who the game log says is in the game's instance besides the account, and when the account joined it.</summary>
public sealed record GameInstancePlayers(List<string> PlayerIds, DateTimeOffset? JoinedAt);

/// <summary>
/// The agent on the PC an account plays on: which connected agent that is, asking it to do
/// something, and what this server keeps for it (the registry backups, the custom tags programs
/// on the PC set, the game's instance by its log). The endpoints are in API/Controller/Desktop.
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

    /// <summary>Relays one action to the account's PC and answers the JSON it said (nothing for an action with no answer), or the problem matching its status.</summary>
    public async Task<Result<string?>> RequestAsync(string accountId, Capability capability, ClientAction action, CancellationToken ct, TimeSpan? timeout = null)
    {
        if (await FindAsync(accountId, ct) is not { } desktop) return DesktopError.NotConnected;
        if (!desktop.Can(capability)) return AgentError.Unsupported;

        var result = await desktops.RequestAsync(desktop.ClientId, action, ct, timeout);
        return result?.Status switch
        {
            null => DesktopError.NoAnswer,
            ActionStatus.Ok => result.Json,
            ActionStatus.Unsupported => AgentError.Unsupported,
            ActionStatus.NotConfirmed => AgentError.NotConfirmed,
            ActionStatus.NotFound => AgentError.NotFound(result.Error),
            ActionStatus.GameRunning => DesktopError.GameRunning,
            _ => DesktopError.ActionFailed(result.Error),
        };
    }

    /// <summary>The session times of the PC this account played on last, connected or not; <paramref name="connectedClientId"/> wins when given.</summary>
    public Task<DesktopSessionTimes?> SessionTimesAsync(string accountId, string? connectedClientId, CancellationToken ct) =>
        db.GameLogClients.AsNoTracking()
            .Where(c => c.AccountId == accountId || (connectedClientId != null && c.ClientId == connectedClientId))
            .OrderByDescending(c => connectedClientId != null && c.ClientId == connectedClientId).ThenByDescending(c => c.LastSeenAt)
            .Select(c => new DesktopSessionTimes(c.GameStartedAt, c.LastGameStartedAt, c.LastGameStoppedAt, c.GameNoVr))
            .FirstOrDefaultAsync(ct);

    /// <summary>Everyone the game log has in an instance right now besides the account, and when the account joined (the original's lastLocation.date).</summary>
    public async Task<GameInstancePlayers> PlayersInAsync(string accountId, string location, CancellationToken ct)
    {
        var ids = await db.InstanceVisits.AsNoTracking()
            .Where(v => v.InstanceLocation == location && v.LeftAt == null && v.Source == VisitSource.GameLog && v.UserId != accountId)
            .Select(v => v.UserId).Distinct().ToListAsync(ct);
        var joinedAt = await db.InstanceVisits.AsNoTracking()
            .Where(v => v.InstanceLocation == location && v.LeftAt == null && v.Source == VisitSource.GameLog && v.UserId == accountId)
            .OrderByDescending(v => v.JoinedAt).Select(v => (DateTimeOffset?)v.JoinedAt).FirstOrDefaultAsync(ct);
        return new GameInstancePlayers(ids, joinedAt);
    }

    /// <summary>The account's custom user tags (the original's customUserTags), by user.</summary>
    public Task<CustomTagResponse[]> CustomTagsAsync(string accountId, CancellationToken ct) =>
        db.CustomUserTags.AsNoTracking().Where(t => t.OwnerUserId == accountId)
            .OrderBy(t => t.UserId)
            .Select(t => new CustomTagResponse(t.UserId, t.Tag, t.Colour))
            .ToArrayAsync(ct);

    /// <summary>The account's registry backups, newest first.</summary>
    public Task<List<VrchatRegistryBackup>> RegistryBackupsAsync(string accountId, CancellationToken ct) =>
        db.VrchatRegistryBackups.AsNoTracking().Where(b => b.AccountId == accountId).OrderByDescending(b => b.CreatedAt).ToListAsync(ct);

    public Task<VrchatRegistryBackup?> RegistryBackupAsync(string accountId, long id, CancellationToken ct) =>
        db.VrchatRegistryBackups.AsNoTracking().FirstOrDefaultAsync(b => b.AccountId == accountId && b.Id == id, ct);

    /// <summary>Keeps what the PC's registry held under a name.</summary>
    public async Task<VrchatRegistryBackup> KeepRegistryBackupAsync(string accountId, string name, System.Text.Json.JsonDocument data, CancellationToken ct)
    {
        var backup = new VrchatRegistryBackup { AccountId = accountId, Name = name.Trim(), CreatedAt = DateTimeOffset.UtcNow, Data = data };
        db.VrchatRegistryBackups.Add(backup);
        await db.SaveChangesAsync(ct);
        return backup;
    }

    public Task ForgetRegistryBackupAsync(string accountId, long id, CancellationToken ct) =>
        db.VrchatRegistryBackups.Where(b => b.AccountId == accountId && b.Id == id).ExecuteDeleteAsync(ct);
}
