namespace VRCX.Vrchat.Models;

/// <summary>GET groups/{id} (with ?includeRoles=true for Roles).</summary>
public sealed record Group
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string ShortCode { get; init; } = "";
    public string Discriminator { get; init; } = "";
    public string Description { get; init; } = "";
    public string? IconUrl { get; init; }
    public string? IconId { get; init; }
    public string? BannerUrl { get; init; }
    public string? BannerId { get; init; }
    public string? OwnerId { get; init; }
    public string Privacy { get; init; } = "default";
    public string JoinState { get; init; } = "open";
    public string? Rules { get; init; }
    public IReadOnlyList<string> Links { get; init; } = [];
    public IReadOnlyList<string> Languages { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> Badges { get; init; } = [];
    public int MemberCount { get; init; }
    public int? OnlineMemberCount { get; init; }
    public DateTimeOffset? MemberCountSyncedAt { get; init; }
    public bool IsVerified { get; init; }
    public string? MembershipStatus { get; init; }
    public GroupMyMember? MyMember { get; init; }
    public IReadOnlyList<GroupRole>? Roles { get; init; }
    public IReadOnlyList<GroupGallery>? Galleries { get; init; }
    /// <summary>The group's store, which the pop-up's Store tab lists.</summary>
    public string? StoreId { get; init; }
    /// <summary>Automatic Group Invites, which Edit Group sets.</summary>
    public bool? AllowGroupJoinPrompt { get; init; }
    public DateTimeOffset? LastPostCreatedAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed record GroupRole
{
    public required string Id { get; init; }
    public string? GroupId { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public bool IsSelfAssignable { get; init; }
    public bool IsManagementRole { get; init; }
    public bool IsAddedOnJoin { get; init; }
    public bool DefaultRole { get; init; }
    public bool RequiresTwoFactor { get; init; }
    public bool RequiresPurchase { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public int Order { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>The account's own membership, embedded in a Group as myMember.</summary>
public sealed record GroupMyMember
{
    public string? Id { get; init; }
    public string? GroupId { get; init; }
    public string? UserId { get; init; }
    public IReadOnlyList<string> RoleIds { get; init; } = [];
    public IReadOnlyList<string> MRoleIds { get; init; } = [];
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public string? MembershipStatus { get; init; }
    public string? Visibility { get; init; }
    public bool IsRepresenting { get; init; }
    public bool IsSubscribedToAnnouncements { get; init; }
    /// <summary>Missing on older memberships, which the original reads as subscribed.</summary>
    public bool? IsSubscribedToEventAnnouncements { get; init; }
    public string? ManagerNotes { get; init; }
    public DateTimeOffset? JoinedAt { get; init; }
    public DateTimeOffset? BannedAt { get; init; }
    public DateTimeOffset? LastPostReadAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>GET groups/{id}/members, groups/{id}/members/{userId}, and the bans, invites and requests lists, which are members too.</summary>
public sealed record GroupMember
{
    public required string Id { get; init; }
    public required string GroupId { get; init; }
    public required string UserId { get; init; }
    public IReadOnlyList<string> RoleIds { get; init; } = [];
    public IReadOnlyList<string> MRoleIds { get; init; } = [];
    public string? MembershipStatus { get; init; }
    public string? Visibility { get; init; }
    public bool IsRepresenting { get; init; }
    public bool IsSubscribedToAnnouncements { get; init; }
    public bool? IsSubscribedToEventAnnouncements { get; init; }
    public string? ManagerNotes { get; init; }
    public string? AcceptedById { get; init; }
    public string? AcceptedByDisplayName { get; init; }
    public bool? HasJoinedFromPurchase { get; init; }
    public DateTimeOffset? JoinedAt { get; init; }
    public DateTimeOffset? BannedAt { get; init; }
    public DateTimeOffset? LastPostReadAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public GroupMemberUser? User { get; init; }
}

public sealed record GroupMemberUser
{
    public required string Id { get; init; }
    public string DisplayName { get; init; } = "";
    public string? ThumbnailUrl { get; init; }
    public string? IconUrl { get; init; }
    public string? ProfilePicOverride { get; init; }
    public string? CurrentAvatarThumbnailImageUrl { get; init; }
    public IReadOnlyList<string>? CurrentAvatarTags { get; init; }
}

/// <summary>GET groups/{id}/members/search.</summary>
public sealed record GroupMemberSearchResult
{
    public IReadOnlyList<GroupMember> Results { get; init; } = [];
}

/// <summary>PUT groups/{id}/members/{userId}: only the fields given are changed.</summary>
public sealed record GroupMemberUpdate
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Visibility { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsSubscribedToAnnouncements { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsSubscribedToEventAnnouncements { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ManagerNotes { get; init; }
}

/// <summary>GET groups/{id}/auditLogs.</summary>
public sealed record GroupAuditLogPage
{
    public IReadOnlyList<GroupAuditLogEntry> Results { get; init; } = [];
    public int? TotalCount { get; init; }
    public bool HasNext { get; init; }
}

public sealed record GroupAuditLogEntry
{
    public string? Id { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; init; }
    public string? GroupId { get; init; }
    public string? ActorId { get; init; }
    public string? ActorDisplayName { get; init; }
    public string? TargetId { get; init; }
    public string? EventType { get; init; }
    public string? Description { get; init; }
    /// <summary>What changed, shaped per event type, kept as VRChat sends it.</summary>
    public System.Text.Json.JsonElement? Data { get; init; }
}

/// <summary>GET users/{id}/groups - the groups a user is in, as the group list endpoint shapes them.</summary>
public sealed record LimitedUserGroup
{
    /// <summary>The membership id (gmem_...), not the group id.</summary>
    public string? Id { get; init; }
    public required string GroupId { get; init; }
    public string Name { get; init; } = "";
    public string ShortCode { get; init; } = "";
    public string Discriminator { get; init; } = "";
    public string Description { get; init; } = "";
    public string? IconUrl { get; init; }
    public string? IconId { get; init; }
    public string? BannerUrl { get; init; }
    public string? BannerId { get; init; }
    public string? OwnerId { get; init; }
    public string? Privacy { get; init; }
    public int MemberCount { get; init; }
    public string? MemberVisibility { get; init; }
    public bool IsRepresenting { get; init; }
    public bool? MutualGroup { get; init; }
    public DateTimeOffset? LastPostCreatedAt { get; init; }
    public DateTimeOffset? LastPostReadAt { get; init; }
}

/// <summary>GET users/{id}/groups/represented - the group shown on a user's nameplate.</summary>
public sealed record RepresentedGroup
{
    public string? GroupId { get; init; }
    public string? Name { get; init; }
    public string? ShortCode { get; init; }
    public string? Discriminator { get; init; }
    public string? Description { get; init; }
    public string? IconUrl { get; init; }
    public string? BannerUrl { get; init; }
    public string? OwnerId { get; init; }
    public string? Privacy { get; init; }
    public int? MemberCount { get; init; }
    public string? MemberVisibility { get; init; }
    public bool IsRepresenting { get; init; }
}

public sealed record GroupGallery
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public bool MembersOnly { get; init; }
    public IReadOnlyList<string>? RoleIdsToView { get; init; }
    public IReadOnlyList<string>? RoleIdsToSubmit { get; init; }
    public IReadOnlyList<string>? RoleIdsToAutoApprove { get; init; }
    public IReadOnlyList<string>? RoleIdsToManage { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public static class GroupMemberStatusValues
{
    public const string Member = "member";
    public const string Invited = "invited";
    public const string Requested = "requested";
    public const string Banned = "banned";
    public const string Inactive = "inactive";
    public const string UserBlocked = "userblocked";
}
