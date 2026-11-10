namespace VRCX.Common.VrcxDb;

public sealed class World
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? AuthorId { get; set; }
    public User? Author { get; set; }
    public string? AuthorName { get; set; }
    public int? Capacity { get; set; }
    public int? RecommendedCapacity { get; set; }
    public string? ImageUrl { get; set; }
    public string? ThumbnailImageUrl { get; set; }
    public string? ReleaseStatus { get; set; }
    public string? Organization { get; set; }
    public string[] Tags { get; set; } = [];
    public int? Version { get; set; }
    /// <summary>Cumulative, as VRChat reports them; momentary counts (occupants, heat, popularity) are deliberately not stored.</summary>
    public int? Visits { get; set; }
    public int? Favorites { get; set; }
    public bool Featured { get; set; }
    public string? PreviewYoutubeId { get; set; }
    public string? PublicationDate { get; set; }
    public string? LabsPublicationDate { get; set; }
    /// <summary>Distinct unityPackages[].platform values: standalonewindows, android, ios.</summary>
    public string[] Platforms { get; set; } = [];
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>True while all we know is the id (from a location tag) and maybe a name (from a log line).</summary>
    public bool IsStub { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset? FetchedAt { get; set; }

    public ICollection<Instance> Instances { get; set; } = [];
}

public sealed class Avatar
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? AuthorId { get; set; }
    public User? Author { get; set; }
    public string? AuthorName { get; set; }
    public string? ImageUrl { get; set; }
    public string? ThumbnailImageUrl { get; set; }
    public string? ReleaseStatus { get; set; }
    public string[] Tags { get; set; } = [];
    public int? Version { get; set; }
    public bool Featured { get; set; }
    public string? PerformancePc { get; set; }
    public string? PerformanceAndroid { get; set; }
    public string? PerformanceIos { get; set; }
    public string? StylePrimary { get; set; }
    public string? StyleSecondary { get; set; }
    public string[] Platforms { get; set; } = [];
    /// <summary>The impostorizer version of the avatar's last impostor package, if it has one (My Avatars' Impostor column).</summary>
    public string? ImpostorVersion { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public bool IsStub { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset? FetchedAt { get; set; }
}

/// <summary>
/// One instance of a world, keyed by its full location ("wrld_x:123~hidden(usr_y)~region(eu)").
/// Created the moment any location tag mentions it; filled in by GET instances/… when fetched.
/// </summary>
public sealed class Instance
{
    public required string Location { get; set; }
    public required string WorldId { get; set; }
    public World World { get; set; } = null!;
    /// <summary>Everything after the colon.</summary>
    public required string InstanceId { get; set; }
    /// <summary>The part before the first "~" - usually the 5-digit number.</summary>
    public required string InstanceName { get; set; }
    /// <summary>From the tag (hidden = friends+).</summary>
    public required InstanceAccessType Type { get; set; }
    public string? Region { get; set; }
    public string? OwnerUserId { get; set; }
    public User? OwnerUser { get; set; }
    public string? GroupId { get; set; }
    public Group? Group { get; set; }
    public string? GroupAccessType { get; set; }
    public bool CanRequestInvite { get; set; }
    public bool AgeGate { get; set; }

    // From GET instances/… only.
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public int? Capacity { get; set; }
    public bool? QueueEnabled { get; set; }
    public string[] Tags { get; set; } = [];
    public bool? Permanent { get; set; }
    public string? ClientNumber { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset? FetchedAt { get; set; }

    public ICollection<InstanceVisit> Visits { get; set; } = [];
}

/// <summary>Which file on disk holds a VRChat image. The bytes are never in the database.</summary>
public sealed class CachedImage
{
    public required string Url { get; set; }
    /// <summary>The file's name on disk, so identical images share one file.</summary>
    public required string Sha256 { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

public sealed class WorldMemo
{
    public required string WorldId { get; set; }
    public World World { get; set; } = null!;
    public required string Memo { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AvatarMemo
{
    public required string AvatarId { get; set; }
    public Avatar Avatar { get; set; } = null!;
    public required string Memo { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AvatarTag
{
    public required string AvatarId { get; set; }
    public Avatar Avatar { get; set; } = null!;
    public required string Tag { get; set; }
    public string? Color { get; set; }
}

/// <summary>Which avatars an account has worn, and for how long (was avatar_history).</summary>
public sealed class AvatarUsage
{
    public required string OwnerUserId { get; set; }
    public Account Owner { get; set; } = null!;
    public required string AvatarId { get; set; }
    public Avatar Avatar { get; set; } = null!;
    public DateTimeOffset LastUsedAt { get; set; }
    public TimeSpan TimeSpent { get; set; }
}
