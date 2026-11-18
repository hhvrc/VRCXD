using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services.Presence;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Content;

public sealed class GroupResponse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ShortCode { get; init; }
    public string? Discriminator { get; init; }
    public string? Description { get; init; }
    public string? IconUrl { get; init; }
    public string? BannerUrl { get; init; }
    public string? OwnerUserId { get; init; }
    public string? Privacy { get; init; }
    public string? JoinState { get; init; }
    public string? Rules { get; init; }
    public required string[] Links { get; init; }
    public required string[] Languages { get; init; }
    public required string[] Tags { get; init; }
    public int? MemberCount { get; init; }
    public required bool IsVerified { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public required bool IsStub { get; init; }
    public DateTimeOffset? FetchedAt { get; init; }
    public required IReadOnlyList<GroupRoleResponse> Roles { get; init; }
    public required IReadOnlyList<GroupMemberResponse> Members { get; init; }
}

public sealed class GroupRoleResponse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required bool IsManagementRole { get; init; }
    public required bool IsSelfAssignable { get; init; }
    public required string[] Permissions { get; init; }
    public required int Order { get; init; }
}

public sealed class GroupMemberResponse
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string MembershipStatus { get; init; }
    public required bool IsRepresenting { get; init; }
    public DateTimeOffset? JoinedAt { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
}

public sealed class InstanceResponse
{
    public required string Location { get; init; }
    public required string WorldId { get; init; }
    public required string WorldName { get; init; }
    public required string InstanceName { get; init; }
    public required InstanceAccessType Type { get; init; }
    public string? Region { get; init; }
    public string? OwnerUserId { get; init; }
    public string? GroupId { get; init; }
    public string? GroupAccessType { get; init; }
    public required bool CanRequestInvite { get; init; }
    public required bool AgeGate { get; init; }
    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public int? Capacity { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public required DateTimeOffset FirstSeenAt { get; init; }
    public DateTimeOffset? FetchedAt { get; init; }
    public required IReadOnlyList<InstancePresentResponse> Present { get; init; }
}

public sealed class InstancePresentResponse
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required DateTimeOffset JoinedAt { get; init; }
    public required VisitSource Source { get; init; }
}

/// <summary>A world as stored, with its author row when there is one, and the memo on it.</summary>
public sealed record StoredWorld(World World, string? Memo);

/// <summary>An avatar as stored, with its author row when there is one, and the memo on it.</summary>
public sealed record StoredAvatar(Avatar Avatar, string? Memo);

/// <summary>A group as stored, with its owner row when there is one, and how many of its memberships are stored here.</summary>
public sealed record StoredGroup(Group Group, int KnownMembers);

/// <summary>
/// Worlds, avatars and groups as this server has them stored. They are shared by every account, so
/// nothing here takes one. Read only and never asks VRChat; shared by the get-by-id endpoints (after
/// <see cref="ContentRefresh"/> has fetched, when asked to) and the MCP tools.
/// </summary>
public sealed class ContentQueries(VrcxContext db, IPresenceCache presence)
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

    /// <summary>The stored world with its author row, or null when the server has never seen it: GET worlds/{id}.</summary>
    public Task<World?> FindWorldAsync(string worldId, CancellationToken cancellationToken) =>
        db.Worlds.AsNoTracking().Include(w => w.Author).FirstOrDefaultAsync(w => w.Id == worldId, cancellationToken);

    /// <summary>The stored avatar with its author row, or null when the server has never seen it: GET avatars/{id}.</summary>
    public Task<Avatar?> FindAvatarAsync(string avatarId, CancellationToken cancellationToken) =>
        db.Avatars.AsNoTracking().Include(a => a.Author).FirstOrDefaultAsync(a => a.Id == avatarId, cancellationToken);

    /// <summary>A group with its roles and current members, or null when the server has never seen it: GET groups/{id}.</summary>
    public async Task<GroupResponse?> GroupDetailsAsync(string groupId, CancellationToken cancellationToken)
    {
        var group = await db.Groups.AsNoTracking().Include(g => g.Roles).FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (group is null) return null;

        var members = await db.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.LeftAt == null)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new GroupMemberResponse
            {
                UserId = m.UserId,
                DisplayName = m.User.DisplayName,
                MembershipStatus = m.MembershipStatus,
                IsRepresenting = m.IsRepresenting,
                JoinedAt = m.JoinedAt,
                Roles = m.Roles.Select(r => r.Role.Name).ToList(),
            })
            .ToListAsync(cancellationToken);

        return new GroupResponse
        {
            Id = group.Id, Name = group.Name, ShortCode = group.ShortCode, Discriminator = group.Discriminator, Description = group.Description,
            IconUrl = group.IconUrl, BannerUrl = group.BannerUrl, OwnerUserId = group.OwnerUserId, Privacy = group.Privacy, JoinState = group.JoinState,
            Rules = group.Rules, Links = group.Links, Languages = group.Languages, Tags = group.Tags, MemberCount = group.MemberCount,
            IsVerified = group.IsVerified, CreatedAt = group.CreatedAt, IsStub = group.IsStub, FetchedAt = group.FetchedAt,
            Roles = group.Roles.OrderBy(r => r.Order).Select(r => new GroupRoleResponse
            {
                Id = r.Id, Name = r.Name, Description = r.Description, IsManagementRole = r.IsManagementRole, IsSelfAssignable = r.IsSelfAssignable,
                Permissions = r.Permissions, Order = r.Order,
            }).ToList(),
            Members = members,
        };
    }

    /// <summary>
    /// An instance by its own location tag, with everyone believed to be there now, or null when the
    /// server has never seen it: GET instances/{location}.
    /// </summary>
    public async Task<InstanceResponse?> InstanceDetailsAsync(string location, CancellationToken cancellationToken)
    {
        var instance = await db.Instances.AsNoTracking().Include(i => i.World).FirstOrDefaultAsync(i => i.Location == location, cancellationToken);
        if (instance is null) return null;

        // The game log's own record of who it saw, plus who VRChat's pipeline places here right now.
        var seen = await db.InstanceVisits.AsNoTracking()
            .Where(v => v.InstanceLocation == location && v.Source == VisitSource.GameLog && v.LeftAt == null)
            .Select(v => new InstancePresentResponse { UserId = v.UserId, DisplayName = v.User.DisplayName, JoinedAt = v.JoinedAt, Source = v.Source })
            .ToListAsync(cancellationToken);
        var occupants = await presence.GetOccupantsAsync(location, cancellationToken);
        var occupantIds = occupants.Select(o => o.UserId).ToArray();
        var names = await db.Users.AsNoTracking().Where(u => occupantIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var present = seen
            .Concat(occupants.Where(o => names.ContainsKey(o.UserId)).Select(o => new InstancePresentResponse
            {
                UserId = o.UserId, DisplayName = names[o.UserId], JoinedAt = o.Since ?? DateTimeOffset.UtcNow, Source = VisitSource.Pipeline,
            }))
            .OrderBy(p => p.JoinedAt)
            .ToList();

        return new InstanceResponse
        {
            Location = instance.Location, WorldId = instance.WorldId, WorldName = instance.World.Name, InstanceName = instance.InstanceName,
            Type = instance.Type, Region = instance.Region, OwnerUserId = instance.OwnerUserId, GroupId = instance.GroupId,
            GroupAccessType = instance.GroupAccessType, CanRequestInvite = instance.CanRequestInvite, AgeGate = instance.AgeGate,
            Name = instance.Name, DisplayName = instance.DisplayName, Capacity = instance.Capacity, ClosedAt = instance.ClosedAt,
            FirstSeenAt = instance.FirstSeenAt, FetchedAt = instance.FetchedAt, Present = present,
        };
    }

    /// <summary>The avatars by a user that this server has seen, by name. VRChat only lists an account's own.</summary>
    public Task<List<Avatar>> AvatarsByAuthorAsync(string userId, CancellationToken cancellationToken) =>
        db.Avatars.AsNoTracking().Where(a => a.AuthorId == userId).OrderBy(a => a.Name).ToListAsync(cancellationToken);
}
