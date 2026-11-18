using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using VRCX.Common.Services.Queries;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Mcp;

public sealed record McpNotification(string Id, string Type, DateTimeOffset At, string? SenderUserId, string? SenderDisplayName, string? Title, string Message, bool Seen, bool Hidden);

public sealed record McpFavorite(string TargetId, string Name, string? Group, DateTimeOffset? FirstSeenAt, string? AuthorName, string? State, McpLocation? Location, bool Missing);

public sealed record McpFavorites(McpAccount Account, string Type, IReadOnlyList<string> Groups, McpPage<McpFavorite> Favorites);

public sealed record McpModeration(string Type, string TargetUserId, string TargetDisplayName, DateTimeOffset? CreatedAt);

[McpServerToolType]
public sealed class ListTools(McpAccountResolver accounts, AccountListQueries lists, VrcxContext db)
{
    [McpServerTool(Name = "get_notifications", Title = "Notifications", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        The account's VRChat notifications as last synced, newest first: friend requests, invites,
        invite requests, group notices and so on (type is VRChat's). Hidden ones (answered or
        dismissed) are left out unless includeHidden. Mirrors VRChat at the last sync, so a
        notification that arrived since may be missing.
        """)]
    public async Task<McpPage<McpNotification>> GetNotifications(
        [Description("usr_ id or display name of the logged-in account; optional when only one account exists.")] string? account = null,
        [Description("Only this VRChat notification type, e.g. friendRequest, invite, requestInvite.")] string? type = null,
        [Description("Include notifications that were answered or dismissed.")] bool includeHidden = false,
        [Description("Rows to return, 1-200 (default 50).")] int limit = McpPaging.DefaultLimit,
        [Description("Rows to skip, from a previous result's nextOffset.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var who = await accounts.ResolveAsync(account, cancellationToken);
        var query = lists.Notifications(who.Id, includeHidden);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(n => n.Type == type.Trim());
        var page = await McpPaging.PageAsync(who, query, offset, limit, cancellationToken);
        return new McpPage<McpNotification>(who,
            page.Items.Select(n => new McpNotification(n.Id, n.Type, n.CreatedAt, n.SenderUserId, n.SenderDisplayName, n.Title, n.Message, n.Seen, n.Hidden)).ToList(),
            page.Offset, page.Truncated, page.NextOffset);
    }

    [McpServerTool(Name = "get_favorites", Title = "Favorites", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        The account's VRChat favorites of one kind as last synced: friend, world or avatar, with the
        favorite group each is in. Favorite friends also carry their current state and location.
        missing means the favorite's target was never stored here, so only its id is known.
        """)]
    public async Task<McpFavorites> GetFavorites(
        [Description("friend, world or avatar.")] string type,
        [Description("usr_ id or display name of the logged-in account; optional when only one account exists.")] string? account = null,
        [Description("Rows to return, 1-200 (default 50).")] int limit = McpPaging.DefaultLimit,
        [Description("Rows to skip, from a previous result's nextOffset.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var kind = (type ?? "").Trim().ToLowerInvariant() switch
        {
            "friend" or "friends" => FavoriteType.Friend,
            "world" or "worlds" => FavoriteType.World,
            "avatar" or "avatars" => FavoriteType.Avatar,
            _ => throw new McpException("`type` must be friend, world or avatar."),
        };
        var who = await accounts.ResolveAsync(account, cancellationToken);
        var result = await lists.FavoritesAsync(who.Id, kind, cancellationToken);
        var name = await McpLocations.NamerAsync(db, result.Favorites.Select(f => f.Location), cancellationToken);
        var rows = result.Favorites.Select(f => new McpFavorite(f.TargetId, f.TargetName, f.GroupDisplayName ?? f.GroupName, f.CreatedAt, f.AuthorName, f.State,
            kind == FavoriteType.Friend ? name(f.Location) : null, f.Missing));
        return new McpFavorites(who, kind.ToString().ToLowerInvariant(), result.Groups.Select(g => g.DisplayName).ToList(), McpPaging.Page(null, rows, offset, limit, result.Favorites.Count));
    }

    [McpServerTool(Name = "get_moderations", Title = "Moderations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        The player moderations the account applied, as VRChat reported them at the last sync:
        block, mute, hideAvatar, showAvatar, interactOff and so on, by target name.
        """)]
    public async Task<McpPage<McpModeration>> GetModerations(
        [Description("usr_ id or display name of the logged-in account; optional when only one account exists.")] string? account = null,
        [Description("Only this moderation type, e.g. block or mute.")] string? type = null,
        [Description("Rows to return, 1-200 (default 50).")] int limit = McpPaging.DefaultLimit,
        [Description("Rows to skip, from a previous result's nextOffset.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var who = await accounts.ResolveAsync(account, cancellationToken);
        var rows = (await lists.ModerationsAsync(who.Id, cancellationToken))
            .Where(m => string.IsNullOrWhiteSpace(type) || string.Equals(m.Type, type.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(m => new McpModeration(m.Type, m.TargetUserId, m.TargetDisplayName, m.CreatedAt))
            .ToList();
        return McpPaging.Page(who, rows, offset, limit, rows.Count);
    }
}
