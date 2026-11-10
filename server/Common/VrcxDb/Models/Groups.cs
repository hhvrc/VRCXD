namespace VRCX.Common.VrcxDb;

public sealed class Group
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? ShortCode { get; set; }
    public string? Discriminator { get; set; }
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? OwnerUserId { get; set; }
    public User? Owner { get; set; }
    public string? Privacy { get; set; }
    public string? JoinState { get; set; }
    public string? Rules { get; set; }
    public string[] Links { get; set; } = [];
    public string[] Languages { get; set; } = [];
    public string[] Tags { get; set; } = [];
    public string[] Badges { get; set; } = [];
    public int? MemberCount { get; set; }
    public bool IsVerified { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public bool IsStub { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset? FetchedAt { get; set; }

    public ICollection<GroupRole> Roles { get; set; } = [];
    public ICollection<GroupMember> Members { get; set; } = [];
}

public sealed class GroupRole
{
    public required string Id { get; set; }
    public required string GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsSelfAssignable { get; set; }
    public bool IsManagementRole { get; set; }
    public bool IsAddedOnJoin { get; set; }
    public bool DefaultRole { get; set; }
    public bool RequiresTwoFactor { get; set; }
    public bool RequiresPurchase { get; set; }
    public string[] Permissions { get; set; } = [];
    public int Order { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public bool IsStub { get; set; }
    public DateTimeOffset? FetchedAt { get; set; }
}

/// <summary>A user's membership of a group - the connecting table, with the membership's own attributes.</summary>
public sealed class GroupMember
{
    public required string GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public required string UserId { get; set; }
    public User User { get; set; } = null!;
    /// <summary>VRChat's gmem_ id, when the endpoint gave one.</summary>
    public string? MembershipId { get; set; }
    /// <summary>member, invited, requested, banned, inactive, userblocked.</summary>
    public string MembershipStatus { get; set; } = "member";
    /// <summary>visible, friends, hidden - whether they show this group on their profile.</summary>
    public string? Visibility { get; set; }
    public bool IsRepresenting { get; set; }
    public string? ManagerNotes { get; set; }
    public DateTimeOffset? JoinedAt { get; set; }
    public DateTimeOffset? BannedAt { get; set; }
    /// <summary>Set when a fetch of their groups no longer listed this one.</summary>
    public DateTimeOffset? LeftAt { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset FetchedAt { get; set; }

    public ICollection<GroupMemberRole> Roles { get; set; } = [];
}

public sealed class GroupMemberRole
{
    public required string GroupId { get; set; }
    public required string UserId { get; set; }
    public GroupMember Member { get; set; } = null!;
    public required string RoleId { get; set; }
    public GroupRole Role { get; set; } = null!;
}
