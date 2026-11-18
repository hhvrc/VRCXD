using System.ComponentModel;
using ModelContextProtocol.Server;
using VRCX.Common.Features.Accounts;

namespace VRCX.API.Mcp;

public sealed record McpAccountRow(string Id, string DisplayName, bool SessionValid, bool Connected, DateTimeOffset LoggedInAt, DateTimeOffset? LastFriendSyncAt, DateTimeOffset? LastFullSyncAt);

public sealed record McpAccountList(IReadOnlyList<McpAccountRow> Accounts);

[McpServerToolType]
public sealed class AccountTools(AccountQueries accounts, AccountSessionRunner sessions)
{
    [McpServerTool(Name = "list_accounts", Title = "List accounts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        The VRChat accounts logged into this VRCXD server. Every other account-scoped tool takes an
        optional `account` (a usr_ id or display name from this list); it may be left out only when
        exactly one account exists. sessionValid=false means VRChat ended the session: its stored
        history is still readable, but nothing new has been recorded for it since. connected says
        whether the server holds a live VRChat session for it right now. lastFriendSyncAt and
        lastFullSyncAt say how fresh the stored friends and lists are.
        """)]
    public async Task<McpAccountList> ListAccounts(CancellationToken cancellationToken) =>
        new((await accounts.ListAsync(cancellationToken))
            .Select(a => new McpAccountRow(a.UserId, a.DisplayName, a.SessionValid, sessions.HasSession(a.UserId), a.LoggedInAt, a.LastFriendSyncAt, a.LastFullSyncAt))
            .ToList());
}
