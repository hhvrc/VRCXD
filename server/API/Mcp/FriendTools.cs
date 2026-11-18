using System.ComponentModel;
using ModelContextProtocol.Server;
using VRCX.Common.Services.Charts;
using VRCX.Common.Services.Queries;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Mcp;

public sealed record McpOnlineFriend(
    string UserId, string DisplayName, string State, string Status, string? StatusDescription,
    McpLocation? Location, McpLocation? TravelingTo, DateTimeOffset? LocationSince, DateTimeOffset? StateSince,
    string? Platform, string? Nickname, bool PendingOffline);

public sealed record McpFriendLogRow(string Type, DateTimeOffset At, string UserId, string DisplayName, string? PreviousDisplayName, string? TrustLevel, string? PreviousTrustLevel);

public sealed record McpMutualFriend(string UserId, string DisplayName);

public sealed record McpMutualFriends(McpAccount Account, string UserId, string? DisplayName, bool IsFriend, DateTimeOffset? FetchedAt, bool UserOptedOut, bool AccountOptedOut, McpPage<McpMutualFriend> Mutuals);

[McpServerToolType]
public sealed class FriendTools(McpAccountResolver accounts, FriendQueries friends, MutualGraphService mutuals, VrcxContext db)
{
    [McpServerTool(Name = "get_online_friends", Title = "Online friends", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        Who of the account's friends is online right now and where: state (online = in the game,
        active = on the website or app only), the instance (location tag, world name, instance type),
        when they got there, status and status text. Online friends come first, then active ones,
        each by name. "private" as a location means VRChat hides where they are. This is the
        server's live presence picture, kept current by the VRChat connection; total says how many
        friends are not offline in all.
        """)]
    public async Task<McpPage<McpOnlineFriend>> GetOnlineFriends(
        [Description("usr_ id or display name of the logged-in account; optional when only one account exists.")] string? account = null,
        [Description("Also list friends who are only active (website or app), not in the game. Default true.")] bool includeActive = true,
        [Description("Rows to return, 1-200 (default 50).")] int limit = McpPaging.DefaultLimit,
        [Description("Rows to skip, from a previous result's nextOffset.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var who = await accounts.ResolveAsync(account, cancellationToken);
        var online = (await friends.ListFriendsAsync(who.Id, cancellationToken))
            .Where(f => f.State == UserState.Online || (includeActive && f.State == UserState.Active))
            .OrderBy(f => f.State == UserState.Online ? 0 : 1).ThenBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var name = await McpLocations.NamerAsync(db, online.SelectMany(f => new[] { f.Location, f.TravelingToLocation }), cancellationToken);
        return McpPaging.Page(who, online.Select(f => new McpOnlineFriend(
            f.UserId, f.DisplayName, McpLocations.State(f.State), f.Status, f.StatusDescription,
            name(f.Location), name(f.TravelingToLocation), f.LocationSince, f.OnlineFor, f.Platform, f.Nickname, f.PendingOffline)), offset, limit, online.Count);
    }

    [McpServerTool(Name = "get_friend_log", Title = "Friend log", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        The account's friend log, newest first: Friend (became friends), Unfriend, FriendRequest and
        CancelFriendRequest (sent through VRCXD), DisplayName (a friend renamed) and TrustLevel (a
        friend's trust rank changed). displayName is the name at that time. Only covers what the
        server observed while it was running and logged in: friends who were already friends at the
        first sync have no Friend row, and an unfriend is noticed at the next friend sync.
        """)]
    public async Task<McpPage<McpFriendLogRow>> GetFriendLog(
        [Description("usr_ id or display name of the logged-in account; optional when only one account exists.")] string? account = null,
        [Description("Only rows about this usr_ id.")] string? userId = null,
        [Description("Only these types, comma separated: Friend, Unfriend, FriendRequest, CancelFriendRequest, DisplayName, TrustLevel.")] string? types = null,
        [Description("Rows to return, 1-200 (default 50).")] int limit = McpPaging.DefaultLimit,
        [Description("Rows to skip, from a previous result's nextOffset.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var who = await accounts.ResolveAsync(account, cancellationToken);
        var wanted = (types ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Unfiltered, only the newest offset+limit+1 rows are needed, and the log pages those in SQL
        // (its page starts newest-first like the whole log, so the rows are the same).
        int? needed = userId is null && wanted.Count == 0 ? McpPaging.Offset(offset) + McpPaging.Limit(limit) + 1 : null;
        var rows = (await friends.FriendLogAsync(who.Id, cancellationToken, needed))
            .Where(e => userId is null || e.UserId == userId)
            .Where(e => wanted.Count == 0 || wanted.Contains(e.Type));
        return McpPaging.Page(who, rows.Select(e => new McpFriendLogRow(e.Type, e.CreatedAt, e.UserId, e.DisplayName, e.PreviousDisplayName, e.TrustLevel, e.PreviousTrustLevel)), offset, limit);
    }

    [McpServerTool(Name = "get_mutual_friends", Title = "Mutual friends", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        Which of the account's friends are also friends with one of its friends, from the stored
        mutual-friend graph. The graph is only as fresh as its last fetch (fetchedAt, started from the
        web UI's Charts page; this tool never fetches). fetchedAt null means this friend's mutuals
        were never fetched. userOptedOut: the friend hides their connections, so nothing is known.
        Only works for current friends.
        """)]
    public async Task<McpMutualFriends> GetMutualFriends(
        [Description("usr_ id of the friend. Use find_user to turn a name into an id.")] string userId,
        [Description("usr_ id or display name of the logged-in account; optional when only one account exists.")] string? account = null,
        [Description("Rows to return, 1-200 (default 50).")] int limit = McpPaging.DefaultLimit,
        [Description("Rows to skip, from a previous result's nextOffset.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var id = McpLocations.RequireId(userId, "usr_", "userId", "Use find_user to turn a name into an id.");
        var who = await accounts.ResolveAsync(account, cancellationToken);
        var graph = await mutuals.GetSnapshotAsync(who.Id, cancellationToken);
        var isFriend = graph.Friends.TryGetValue(id, out var shared);
        var meta = graph.Meta.GetValueOrDefault(id);
        var rows = (shared ?? [])
            .Select(m => new McpMutualFriend(m, graph.Names.GetValueOrDefault(m) ?? m))
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase);
        return new McpMutualFriends(who, id, graph.Names.GetValueOrDefault(id), isFriend, meta?.LastFetchedAt, meta?.OptedOut ?? false, graph.OptedOut,
            McpPaging.Page(null, rows, offset, limit));
    }
}
