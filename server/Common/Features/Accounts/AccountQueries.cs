using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Accounts;

/// <summary>
/// What the feature controllers ask about the accounts logged into this server. The accounts
/// endpoints themselves have not moved here yet.
/// </summary>
public sealed class AccountQueries(VrcxContext db)
{
    /// <summary>Whether the account is logged into this server: what an account-scoped endpoint answers 404 on.</summary>
    public Task<bool> ExistsAsync(string accountId, CancellationToken cancellationToken) =>
        db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken);
}
