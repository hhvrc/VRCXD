using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VRCX.Common.Live;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services.Live;

/// <summary>
/// The original's VRChat registry backups (vrcx.js backupVrcRegistry, tryAutoBackupVrcRegistry and
/// checkAutoBackupRestoreVrcRegistry), kept in Postgres per account. The PC's agent reads and writes
/// the registry; this decides when.
/// </summary>
public sealed class RegistryBackups(
    IDbContextFactory<VrcxContext> dbFactory,
    DesktopClients desktops,
    TimeProvider time,
    ILogger<RegistryBackups> logger)
{
    private static readonly TimeSpan AutoEvery = TimeSpan.FromDays(3);
    private static readonly TimeSpan AutoKeep = TimeSpan.FromDays(14);

    /// <summary>What reading the registry on the PC gave.</summary>
    public sealed record Read(JsonDocument? Data, ActionStatus Status, string? Error)
    {
        /// <summary>VRChat has no registry key on the PC (HasVRChatRegistryFolder false).</summary>
        public bool Missing => Status == ActionStatus.NotFound;
    }

    public async Task<Read> ReadAsync(DesktopClient desktop, CancellationToken ct)
    {
        var result = await desktops.RequestAsync(desktop.ClientId, new ClientAction { ReadVrchatRegistry = new LogNoData() }, ct);
        if (result is null) return new Read(null, ActionStatus.Failed, "The PC did not answer");
        return result.Status == ActionStatus.Ok && !string.IsNullOrEmpty(result.Json)
            ? new Read(JsonDocument.Parse(result.Json), ActionStatus.Ok, null)
            : new Read(null, result.Status, result.Error);
    }

    /// <summary>backupVrcRegistry: reads the PC's registry and keeps it under <paramref name="name"/>; null when it could not be read.</summary>
    public async Task<VrchatRegistryBackup?> BackupAsync(string accountId, DesktopClient desktop, string name, CancellationToken ct)
    {
        var read = await ReadAsync(desktop, ct);
        if (read.Data is null) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var backup = new VrchatRegistryBackup { AccountId = accountId, Name = name, CreatedAt = time.GetUtcNow(), Data = read.Data };
        db.VrchatRegistryBackups.Add(backup);
        await db.SaveChangesAsync(ct);
        return backup;
    }

    /// <summary>tryAutoBackupVrcRegistry: an auto backup when the last is three days old, dropping auto backups older than two weeks.</summary>
    public async Task TryAutoBackupAsync(string accountId, DesktopClient desktop, CancellationToken ct)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var settings = await db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => a.Settings).FirstOrDefaultAsync(ct);
            if (settings is not { VrcRegistryAutoBackup: true }) return;
            var now = time.GetUtcNow();
            var last = await LastAutoBackupAsync(db, accountId, ct);
            if (last is not null && now - last.CreatedAt < AutoEvery) return;
            await db.VrchatRegistryBackups
                .Where(b => b.AccountId == accountId && b.Name == VrchatRegistryBackup.AutoName && b.CreatedAt < now - AutoKeep)
                .ExecuteDeleteAsync(ct);
        }
        await BackupAsync(accountId, desktop, VrchatRegistryBackup.AutoName, ct);
    }

    /// <summary>
    /// checkAutoBackupRestoreVrcRegistry, when the web client starts: with VRChat's registry gone, true
    /// once per auto backup (the client then offers the backups); otherwise an auto backup when due.
    /// </summary>
    public async Task<bool> CheckAsync(string accountId, DesktopClient desktop, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => a.Settings).FirstOrDefaultAsync(ct);
        if (settings is not { VrcRegistryAutoBackup: true, VrcRegistryAskRestore: true }) return false;

        var read = await ReadAsync(desktop, ct);
        if (read.Missing)
        {
            if (await LastAutoBackupAsync(db, accountId, ct) is not { RestoreOffered: false } last) return false;
            last.RestoreOffered = true;
            await db.SaveChangesAsync(ct);
            return true;
        }
        if (read.Data is not null) await TryAutoBackupAsync(accountId, desktop, ct);
        return false;
    }

    /// <summary>On the agent's hello: the auto backup the original made at start, without a page open.</summary>
    public async Task OnConnectAsync(string clientId, string? accountId, CancellationToken ct)
    {
        try
        {
            if (accountId is null || desktops.Get(clientId) is not { } desktop || !desktop.Can(Capability.Registry)) return;
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
            {
                var settings = await db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => a.Settings).FirstOrDefaultAsync(ct);
                // As the original: no backups while asking to restore is off either.
                if (settings is not { VrcRegistryAskRestore: true }) return;
            }
            await TryAutoBackupAsync(accountId, desktop, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Backing up VRChat's registry on {Client} failed", clientId);
        }
    }

    private static Task<VrchatRegistryBackup?> LastAutoBackupAsync(VrcxContext db, string accountId, CancellationToken ct) =>
        db.VrchatRegistryBackups.Where(b => b.AccountId == accountId && b.Name == VrchatRegistryBackup.AutoName)
            .OrderByDescending(b => b.CreatedAt).FirstOrDefaultAsync(ct);
}
