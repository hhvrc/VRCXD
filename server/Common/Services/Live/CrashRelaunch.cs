using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VRCX.Common.Live;
using VRCX.Common.VrcxDb;

using VRCX.Vrchat;

namespace VRCX.Common.Services.Live;

/// <summary>
/// The original's runCheckIfGameCrashedFlow and runRestartCrashedGameFlow: when VRChat closes without
/// logging its quit, and the account has relaunch after crash on, the PC starts it again in the
/// instance it was in. Not twice within two minutes (a crash loop), and in VR only while SteamVR still
/// runs. Decided here rather than in a page, so it works with no browser open.
/// </summary>
public sealed class CrashRelaunch(
    IDbContextFactory<VrcxContext> dbFactory,
    DesktopClients desktops,
    ILiveEventBus bus,
    ILogger<CrashRelaunch> logger)
{
    public const string Message = "VRChat crashed, attempting to rejoin last instance";
    private static readonly TimeSpan CrashLoop = TimeSpan.FromMinutes(2);

    /// <summary>How long to wait before deciding, in VR: SteamVR closing too means the user quit.</summary>
    public TimeSpan VrDelay { get; set; } = TimeSpan.FromSeconds(8);
    /// <summary>In desktop mode, only long enough for the game to finish closing.</summary>
    public TimeSpan DesktopDelay { get; set; } = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastCrash = new();

    /// <summary>VRChat stopped on this PC without quitting; <paramref name="location"/> is where it was.</summary>
    public async Task GameCrashedAsync(string clientId, string? accountId, string? location, CancellationToken ct)
    {
        try
        {
            await RunAsync(clientId, accountId, location, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Relaunching VRChat on {Client} after a crash failed", clientId);
        }
    }

    private async Task RunAsync(string clientId, string? accountId, string? location, CancellationToken ct)
    {
        if (location is null || !VrchatLocation.Parse(location).IsRealInstance) return;
        bool noVr;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var client = await db.GameLogClients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == clientId, ct);
            accountId ??= client?.AccountId;
            if (accountId is null) return;
            var settings = await db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => a.Settings).FirstOrDefaultAsync(ct);
            if (settings is not { RelaunchVrchatAfterCrash: true }) return;
            noVr = client?.GameNoVr ?? false;
        }

        var now = DateTimeOffset.UtcNow;
        if (_lastCrash.TryGetValue(clientId, out var last) && now - last < CrashLoop)
        {
            logger.LogInformation("VRChat on {Client} crashed again within two minutes; not relaunching", clientId);
            return;
        }
        _lastCrash[clientId] = now;

        await Task.Delay(noVr ? DesktopDelay : VrDelay, ct);
        var desktop = desktops.Get(clientId);
        if (desktop is null || desktop.GameRunning || !desktop.Can(Capability.Launch)) return;
        if (!noVr && !desktop.SteamVrRunning)
        {
            logger.LogInformation("SteamVR isn't running on {Client}, not relaunching VRChat", clientId);
            return;
        }

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            db.GameLogEvents.Add(new GameLogEvent { At = DateTimeOffset.UtcNow, InstanceLocation = location, Kind = "Event", Data = Message });
            await db.SaveChangesAsync(ct);
        }
        bus.Publish(LiveEvents.GameLog(accountId, location, ["event"]));
        await desktops.PostAsync(clientId, new ClientAction
        {
            LaunchVrchat = new LaunchVrchat { LaunchUrl = $"vrchat://launch?ref=vrcx.app&id={location}", DesktopMode = noVr },
        }, ct);
    }
}
