using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Content;

/// <summary>A world as stored, with its author row when there is one, and the memo on it.</summary>
public sealed record StoredWorld(World World, string? Memo);

/// <summary>An avatar as stored, with its author row when there is one, and the memo on it.</summary>
public sealed record StoredAvatar(Avatar Avatar, string? Memo);

/// <summary>A group as stored, with its owner row when there is one, and how many of its memberships are stored here.</summary>
public sealed record StoredGroup(Group Group, int KnownMembers);

/// <summary>
/// Worlds, avatars and groups as this server has them stored. They are shared by every account, so
/// nothing here takes one. Read only; the MCP tools use it and never ask VRChat.
/// </summary>
public sealed class ContentQueries(VrcxContext db)
{
    /// <summary>The stored world, or null when the server has never seen it.</summary>
    public async Task<StoredWorld?> WorldAsync(string worldId, CancellationToken cancellationToken)
    {
        var world = await db.Worlds.AsNoTracking().Include(w => w.Author).FirstOrDefaultAsync(w => w.Id == worldId, cancellationToken);
        if (world is null) return null;
        var memo = await db.WorldMemos.AsNoTracking().Where(m => m.WorldId == worldId).Select(m => m.Memo).FirstOrDefaultAsync(cancellationToken);
        return new StoredWorld(world, memo);
    }

    /// <summary>The stored avatar, or null when the server has never seen it.</summary>
    public async Task<StoredAvatar?> AvatarAsync(string avatarId, CancellationToken cancellationToken)
    {
        var avatar = await db.Avatars.AsNoTracking().Include(a => a.Author).FirstOrDefaultAsync(a => a.Id == avatarId, cancellationToken);
        if (avatar is null) return null;
        var memo = await db.AvatarMemos.AsNoTracking().Where(m => m.AvatarId == avatarId).Select(m => m.Memo).FirstOrDefaultAsync(cancellationToken);
        return new StoredAvatar(avatar, memo);
    }

    /// <summary>The stored group, or null when the server has never seen it.</summary>
    public async Task<StoredGroup?> GroupAsync(string groupId, CancellationToken cancellationToken)
    {
        var group = await db.Groups.AsNoTracking().Include(g => g.Owner).FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (group is null) return null;
        var known = await db.GroupMembers.AsNoTracking().CountAsync(m => m.GroupId == groupId && m.MembershipStatus == "member", cancellationToken);
        return new StoredGroup(group, known);
    }
}
