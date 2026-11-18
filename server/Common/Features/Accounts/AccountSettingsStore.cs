using Microsoft.EntityFrameworkCore;
using VRCX.Common.Features.Feed;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Accounts;

/// <summary>
/// The per-account app settings the original kept in its config: what the server should ask the
/// desktop app to do with screenshots and a quitting game, and what to log.
/// </summary>
public sealed class AccountSettingsStore(VrcxContext db)
{
    /// <summary>The account's app settings, or null when there is no such account.</summary>
    public async Task<AccountSettings?> GetAsync(string accountId, CancellationToken cancellationToken) =>
        (await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == accountId, cancellationToken))?.Settings;

    /// <summary>
    /// Replaces the account's app settings with <paramref name="body"/>, brought within what the
    /// server accepts, and answers them as stored. Null when there is no such account.
    /// </summary>
    public async Task<AccountSettings?> SaveAsync(string accountId, AccountSettings body, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.UserId == accountId, cancellationToken);
        if (account is null) return null;
        account.Settings = body with
        {
            YoutubeApiKey = body.YoutubeApiKey?.Trim() ?? "",
            // VRChat's limit, which the original's inputs enforce too.
            AutoStateChangeAloneDesc = Clip(body.AutoStateChangeAloneDesc),
            AutoStateChangeCompanyDesc = Clip(body.AutoStateChangeCompanyDesc),
            AvatarAutoCleanup = AvatarFeedCleanup.Normalize(body.AvatarAutoCleanup),
            // The server's own record of the last run; the client cannot move it.
            LastAvatarCleanupAt = account.Settings.LastAvatarCleanupAt,
            // The original's setAutoLoginDelaySeconds keeps it within 0 to 10.
            AutoLoginDelaySeconds = Math.Clamp(body.AutoLoginDelaySeconds, 0, 10),
            UgcFolderPath = body.UgcFolderPath?.Trim() ?? "",
        };
        await db.SaveChangesAsync(cancellationToken);
        return account.Settings;

        static string Clip(string? text) => text is { Length: > 32 } ? text[..32] : text ?? "";
    }
}
