using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Accounts;

/// <summary>An account as the account list shows it: its session and sync state, its picture, and whether a password is saved for it.</summary>
public sealed record AccountDetails(
    string UserId, string DisplayName, string? Username, bool SessionValid, string? ApiEndpoint, string? PipelineEndpoint,
    DateTimeOffset LoggedInAt, DateTimeOffset? LastFriendSyncAt, DateTimeOffset? LastFullSyncAt, string? HomeLocation,
    string? ThumbnailImageUrl, string? IconFrame, bool HasSavedCredentials, bool SavedCredentialsNeedPrimaryPassword);

/// <summary>An account logged into this server, with how fresh its stored friends and lists are.</summary>
public sealed record AccountSummary(string UserId, string DisplayName, bool SessionValid, DateTimeOffset LoggedInAt, DateTimeOffset? LastFriendSyncAt, DateTimeOffset? LastFullSyncAt);

/// <summary>
/// What the feature controllers and the MCP tools ask about the accounts logged into this server.
/// The accounts endpoints themselves have not moved here yet.
/// </summary>
public sealed class AccountQueries(VrcxContext db)
{
    /// <summary>Whether the account is logged into this server: what an account-scoped endpoint answers 404 on.</summary>
    public Task<bool> ExistsAsync(string accountId, CancellationToken cancellationToken) =>
        db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken);

    /// <summary>The accounts logged into this server, by display name and then id.</summary>
    public Task<List<AccountSummary>> ListAsync(CancellationToken cancellationToken) =>
        db.Accounts.AsNoTracking()
            .OrderBy(a => a.User.DisplayName).ThenBy(a => a.UserId)
            .Select(a => new AccountSummary(a.UserId, a.User.DisplayName, a.SessionValid, a.LoggedInAt, a.LastFriendSyncAt, a.LastFullSyncAt))
            .ToListAsync(cancellationToken);

    /// <summary>Every VRChat account that has logged into this server, with its session and sync state.</summary>
    public async Task<List<AccountDetails>> ListDetailsAsync(CancellationToken cancellationToken)
    {
        var accounts = await db.Accounts.AsNoTracking()
            .Select(a => new
            {
                a.UserId, a.User.DisplayName, a.Username, a.SessionValid, a.ApiEndpoint, a.PipelineEndpoint, a.LoggedInAt, a.LastFriendSyncAt, a.LastFullSyncAt, a.HomeLocation,
                a.User.IconUrl, a.User.CurrentAvatarThumbnailImageUrl, a.User.CurrentAvatarImageUrl, a.User.IconFrame,
            })
            .ToListAsync(cancellationToken);
        var saved = await db.SavedCredentials.AsNoTracking().Select(c => new { c.AccountId, c.Protection }).ToDictionaryAsync(c => c.AccountId, c => c.Protection, cancellationToken);
        return accounts.Select(a => new AccountDetails(
            a.UserId, a.DisplayName, a.Username, a.SessionValid, a.ApiEndpoint, a.PipelineEndpoint, a.LoggedInAt, a.LastFriendSyncAt, a.LastFullSyncAt, a.HomeLocation,
            UserImage.Pick(a.IconUrl, a.CurrentAvatarThumbnailImageUrl, a.CurrentAvatarImageUrl), a.IconFrame,
            saved.ContainsKey(a.UserId), saved.GetValueOrDefault(a.UserId) == CredentialProtection.PrimaryPassword)).ToList();
    }
}
