using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Mcp;

/// <summary>The account a result is about.</summary>
public sealed record McpAccount(string Id, string DisplayName);

/// <summary>
/// Picks the account an account-scoped tool works on. The server can hold several VRChat accounts
/// and an AI caller must never be answered from the wrong one: with no <c>account</c> argument the
/// only account is used, and with several the call fails with the list so the caller can choose.
/// </summary>
public sealed class McpAccountResolver(VrcxContext db)
{
    public async Task<McpAccount> ResolveAsync(string? account, CancellationToken cancellationToken)
    {
        var all = await db.Accounts.AsNoTracking()
            .OrderBy(a => a.User.DisplayName).ThenBy(a => a.UserId)
            .Select(a => new McpAccount(a.UserId, a.User.DisplayName))
            .ToListAsync(cancellationToken);
        if (all.Count == 0)
            throw new McpException("No VRChat account has logged into this server yet, so there is nothing stored to answer from.");

        if (string.IsNullOrWhiteSpace(account))
        {
            if (all.Count == 1) return all[0];
            throw new McpException($"Several VRChat accounts are logged into this server; call again with `account` set to one of: {Describe(all)}.");
        }

        var wanted = account.Trim();
        var matches = all.Where(a => a.Id == wanted).ToList();
        if (matches.Count == 0) matches = all.Where(a => string.Equals(a.DisplayName, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new McpException($"No logged-in account is \"{wanted}\". The accounts are: {Describe(all)}."),
            _ => throw new McpException($"\"{wanted}\" is the name of several accounts; pass the usr_ id of one of: {Describe(matches)}."),
        };
    }

    private static string Describe(IEnumerable<McpAccount> accounts) =>
        string.Join(", ", accounts.Select(a => $"{a.DisplayName} ({a.Id})"));
}
