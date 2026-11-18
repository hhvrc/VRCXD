using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Accounts;

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
}
