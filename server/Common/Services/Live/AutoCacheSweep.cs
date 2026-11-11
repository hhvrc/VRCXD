using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VRCX.Common.Live;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services.Live;

/// <summary>
/// The original's runAutoVRChatCacheManagementFlow: when VRChat stops, and the account has auto
/// cache management on, the PC's agent sweeps old versions out of VRChat's cache.
/// </summary>
public sealed class AutoCacheSweep(IDbContextFactory<VrcxContext> dbFactory, DesktopClients desktops, ILogger<AutoCacheSweep> logger)
{
    public async Task GameStoppedAsync(string clientId, string? accountId, CancellationToken ct)
    {
        try
        {
            if (accountId is null || desktops.Get(clientId) is not { } desktop || !desktop.Can(Capability.Cache)) return;
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
            {
                var settings = await db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => a.Settings).FirstOrDefaultAsync(ct);
                if (settings is not { AutoSweepVrchatCache: true }) return;
            }
            await desktops.PostAsync(clientId, new ClientAction { SweepCache = new LogNoData() }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Sweeping VRChat's cache on {Client} failed", clientId);
        }
    }
}
