using Microsoft.EntityFrameworkCore;
using VRCX.Common.Live;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services.Live;

/// <summary>
/// The account settings the PC's agent acts on by itself (the app launcher, the UGC folder, the
/// cache sweep), sent as <see cref="AgentSettings"/> when the agent connects and whenever they are
/// saved. The agent keeps the last copy, so they hold while the server is away.
/// </summary>
public sealed class AgentSettingsPush(IDbContextFactory<VrcxContext> dbFactory, DesktopClients desktops)
{
    public static ClientAction For(AccountSettings s) => new()
    {
        ApplyAgentSettings = new AgentSettings
        {
            AppLauncherEnabled = s.EnableAppLauncher,
            AppLauncherKillOnExit = s.EnableAppLauncherAutoClose,
            AppLauncherRunOnce = s.EnableAppLauncherRunProcessOnce,
            RelaunchAfterCrash = s.RelaunchVrchatAfterCrash,
            AutoSweepCache = s.AutoSweepVrchatCache,
            UgcFolder = s.UgcFolderPath.Trim(),
            OpenVr = s.OpenVr,
            OpenVrAlways = s.OpenVrAlways,
            WristOverlay = s.OverlayWrist,
            OverlayMenuButton = s.OverlayButton,
            OverlayHand = s.OverlayHand,
        },
    };

    /// <summary>The push for a PC that just connected, when its agent takes settings and its account is known.</summary>
    public async Task<ClientAction?> OnConnectAsync(string clientId, string? accountId, CancellationToken ct)
    {
        if (accountId is null || desktops.Get(clientId) is not { } desktop || !desktop.Can(Capability.Settings)) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => a.Settings).FirstOrDefaultAsync(ct);
        return settings is null ? null : For(settings);
    }

    /// <summary>Sends saved settings to the account's PC, if it is connected and takes them.</summary>
    public Task SendAsync(DesktopClient? desktop, AccountSettings settings, CancellationToken ct) =>
        desktop is not null && desktop.Can(Capability.Settings) ? desktops.PostAsync(desktop.ClientId, For(settings), ct) : Task.CompletedTask;
}
