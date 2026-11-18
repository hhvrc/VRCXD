using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using VRCX.Common.Features.Content;

namespace VRCX.API.Mcp;

public sealed record McpWorld(
    string Id, string Name, string? AuthorId, string? AuthorName, string? Description, int? Capacity, int? RecommendedCapacity,
    string? ReleaseStatus, IReadOnlyList<string> Tags, IReadOnlyList<string> Platforms, int? Visits, int? Favorites,
    DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt, bool IsStub, DateTimeOffset? FetchedAt, string? Memo);

public sealed record McpAvatar(
    string Id, string Name, string? AuthorId, string? AuthorName, string? Description, string? ReleaseStatus,
    IReadOnlyList<string> Tags, IReadOnlyList<string> Platforms, string? PerformancePc, string? PerformanceAndroid,
    DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt, bool IsStub, DateTimeOffset? FetchedAt, string? Memo);

public sealed record McpGroup(
    string Id, string Name, string? ShortCode, string? Discriminator, string? Description, string? OwnerUserId, string? OwnerName,
    string? Privacy, string? JoinState, int? MemberCount, bool IsVerified, IReadOnlyList<string> Languages, IReadOnlyList<string> Links,
    DateTimeOffset? CreatedAt, bool IsStub, DateTimeOffset? FetchedAt, int KnownMembers);

// Worlds, avatars and groups are shared by every account, so these take no account.
[McpServerToolType]
public sealed class ContentTools(ContentQueries content)
{
    private const string StoredOnly = "Stored copy only: never asks VRChat, so it can be stale (fetchedAt says when it was last fetched; isStub means only the id was ever seen).";

    [McpServerTool(Name = "get_world", Title = "Get a world", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("A world as stored here: name, author, description, capacity, tags, platforms, visit and favorite counts as VRChat last reported them, and the VRCXD memo on it. " + StoredOnly)]
    public async Task<McpWorld> GetWorld(
        [Description("wrld_ id of the world.")] string worldId,
        CancellationToken cancellationToken = default)
    {
        var id = McpLocations.RequireId(worldId, "wrld_", "worldId", "A location tag starts with the world id (the part before the colon).");
        var stored = await content.WorldAsync(id, cancellationToken) ?? throw new McpException($"This server has never seen {id}.");
        var (w, memo) = (stored.World, stored.Memo);
        return new McpWorld(w.Id, w.Name, w.AuthorId, w.Author is { IsStub: false } author ? author.DisplayName : w.AuthorName, w.Description, w.Capacity, w.RecommendedCapacity,
            w.ReleaseStatus, w.Tags, w.Platforms, w.Visits, w.Favorites, w.CreatedAt, w.UpdatedAt, w.IsStub, w.FetchedAt, string.IsNullOrEmpty(memo) ? null : memo);
    }

    [McpServerTool(Name = "get_avatar", Title = "Get an avatar", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("An avatar as stored here: name, author, description, release status, tags, platforms and performance ratings, and the VRCXD memo on it. " + StoredOnly)]
    public async Task<McpAvatar> GetAvatar(
        [Description("avtr_ id of the avatar.")] string avatarId,
        CancellationToken cancellationToken = default)
    {
        var id = McpLocations.RequireId(avatarId, "avtr_", "avatarId", "Avatar ids come from get_user_history (avatar changes) or get_favorites.");
        var stored = await content.AvatarAsync(id, cancellationToken) ?? throw new McpException($"This server has never seen {id}.");
        var (a, memo) = (stored.Avatar, stored.Memo);
        return new McpAvatar(a.Id, a.Name, a.AuthorId, a.Author is { IsStub: false } author ? author.DisplayName : a.AuthorName, a.Description, a.ReleaseStatus,
            a.Tags, a.Platforms, a.PerformancePc, a.PerformanceAndroid, a.CreatedAt, a.UpdatedAt, a.IsStub, a.FetchedAt, string.IsNullOrEmpty(memo) ? null : memo);
    }

    [McpServerTool(Name = "get_group", Title = "Get a group", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("A VRChat group as stored here: name, short code, description, owner, privacy, join state and member count as VRChat last reported it. knownMembers counts the memberships stored here (the logged-in accounts' own groups are synced; others are partial). " + StoredOnly)]
    public async Task<McpGroup> GetGroup(
        [Description("grp_ id of the group.")] string groupId,
        CancellationToken cancellationToken = default)
    {
        var id = McpLocations.RequireId(groupId, "grp_", "groupId", "Group instances carry the group id in their location tag (~group(grp_...)).");
        var stored = await content.GroupAsync(id, cancellationToken) ?? throw new McpException($"This server has never seen {id}.");
        var g = stored.Group;
        return new McpGroup(g.Id, g.Name, g.ShortCode, g.Discriminator, g.Description, g.OwnerUserId, g.Owner?.DisplayName,
            g.Privacy, g.JoinState, g.MemberCount, g.IsVerified, g.Languages, g.Links, g.CreatedAt, g.IsStub, g.FetchedAt, stored.KnownMembers);
    }
}
