using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using VRCX.Common.Services;

namespace VRCX.API.Mcp;

/// <summary>A memo as stored after a write. Removed: the memo was empty, so there is none now.</summary>
public sealed record McpMemo(string Id, string Memo, bool Removed, DateTimeOffset? UpdatedAt);

// The only writes MCP offers in v1: VRCXD's own memos. They are local data, VRChat never sees
// them, so these change nothing on VRChat and make no VRChat call.
[McpServerToolType]
public sealed class MemoTools(MemoStore memos)
{
    private const string Replaces = "The memo is replaced as a whole: read the current one first (get_user, get_world, get_avatar) to add to it. An empty memo removes it. Memos are VRCXD's own notes, shared by all accounts on this server, and never sent to VRChat.";

    [McpServerTool(Name = "set_user_memo", Title = "Set a user memo", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Write the VRCXD memo on a user (the first line shows as their nickname in the sidebar). " + Replaces)]
    public async Task<McpMemo> SetUserMemo(
        [Description("usr_ id of the user. Use find_user to turn a name into an id.")] string userId,
        [Description("The whole new memo text; empty to remove it.")] string memo,
        CancellationToken cancellationToken = default)
    {
        var id = McpLocations.RequireId(userId, "usr_", "userId", "Use find_user to turn a name into an id.");
        if (string.IsNullOrWhiteSpace(memo))
        {
            await memos.DeleteUserMemoAsync(id, cancellationToken);
            return new McpMemo(id, "", true, null);
        }
        var stored = await memos.SetUserMemoAsync(id, memo, cancellationToken);
        if (!stored.Found) throw new McpException($"This server has never seen {id}, so it cannot hold a memo on it.");
        return new McpMemo(id, stored.Memo, false, stored.UpdatedAt);
    }

    [McpServerTool(Name = "set_world_memo", Title = "Set a world memo", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Write the VRCXD memo on a world. " + Replaces)]
    public async Task<McpMemo> SetWorldMemo(
        [Description("wrld_ id of the world.")] string worldId,
        [Description("The whole new memo text; empty to remove it.")] string memo,
        CancellationToken cancellationToken = default)
    {
        var id = McpLocations.RequireId(worldId, "wrld_", "worldId", "A location tag starts with the world id (the part before the colon).");
        var stored = await memos.SetWorldMemoAsync(id, memo ?? "", cancellationToken);
        if (!stored.Found) throw new McpException($"This server has never seen {id}, so it cannot hold a memo on it.");
        return new McpMemo(id, stored.Memo, stored.UpdatedAt is null, stored.UpdatedAt);
    }

    [McpServerTool(Name = "set_avatar_memo", Title = "Set an avatar memo", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Write the VRCXD memo on an avatar. " + Replaces)]
    public async Task<McpMemo> SetAvatarMemo(
        [Description("avtr_ id of the avatar.")] string avatarId,
        [Description("The whole new memo text; empty to remove it.")] string memo,
        CancellationToken cancellationToken = default)
    {
        var id = McpLocations.RequireId(avatarId, "avtr_", "avatarId", "Avatar ids come from get_user_history (avatar changes) or get_favorites.");
        var stored = await memos.SetAvatarMemoAsync(id, memo ?? "", cancellationToken);
        if (!stored.Found) throw new McpException($"This server has never seen {id}, so it cannot hold a memo on it.");
        return new McpMemo(id, stored.Memo, stored.UpdatedAt is null, stored.UpdatedAt);
    }
}
