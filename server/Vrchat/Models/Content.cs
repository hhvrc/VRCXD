using System.Text.Json;
using System.Text.Json.Serialization;

namespace VRCX.Vrchat.Models;

/// <summary>GET worlds/{id}.</summary>
public sealed record World
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public required string AuthorId { get; init; }
    public string AuthorName { get; init; } = "";
    public int Capacity { get; init; }
    public int RecommendedCapacity { get; init; }
    public string ImageUrl { get; init; } = "";
    public string ThumbnailImageUrl { get; init; } = "";
    public string ReleaseStatus { get; init; } = "public";
    public string Organization { get; init; } = "vrchat";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public int Version { get; init; }
    public int Visits { get; init; }
    public int Favorites { get; init; }
    public int Heat { get; init; }
    public int Popularity { get; init; }
    public int Occupants { get; init; }
    public int PublicOccupants { get; init; }
    public int PrivateOccupants { get; init; }
    public bool Featured { get; init; }
    public string? PreviewYoutubeId { get; init; }
    public string? LabsPublicationDate { get; init; }
    public string? PublicationDate { get; init; }
    [JsonConverter(typeof(LenientListConverter<UnityPackage>))]
    public IReadOnlyList<UnityPackage> UnityPackages { get; init; } = [];
    public IReadOnlyList<string>? UdonProducts { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; init; }
    /// <summary>[[instanceId, userCount], ...] - only on the full world object.</summary>
    public IReadOnlyList<JsonElement>? Instances { get; init; }
    /// <summary>Allowed video player domains.</summary>
    public IReadOnlyList<string>? UrlList { get; init; }
    public IReadOnlyList<string>? DisabledPropAbilities { get; init; }
    /// <summary>The world's store, when it sells something (the Store tab).</summary>
    public string? StoreId { get; init; }
    /// <summary>Only for the world's author: the source package VRChat keeps ("Future Proofing").</summary>
    public string? UnityPackageUrl { get; init; }
}

/// <summary>GET, PUT or DELETE worlds/{id}/rating: like, dislike or null.</summary>
public sealed record WorldRating
{
    // Sent as null when there is none, as VRChat does.
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Value { get; init; }
}

/// <summary>GET avatars/{id}.</summary>
public sealed record Avatar
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public required string AuthorId { get; init; }
    public string AuthorName { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public string ThumbnailImageUrl { get; init; } = "";
    public string ReleaseStatus { get; init; } = "public";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public int Version { get; init; }
    public bool Featured { get; init; }
    public bool? Searchable { get; init; }
    public AvatarPerformance? Performance { get; init; }
    public AvatarStyles? Styles { get; init; }
    [JsonConverter(typeof(LenientListConverter<UnityPackage>))]
    public IReadOnlyList<UnityPackage> UnityPackages { get; init; } = [];
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; init; }
    /// <summary>Only for the avatar's author: the source package VRChat keeps ("Future Proofing").</summary>
    public string? UnityPackageUrl { get; init; }
    /// <summary>The marketplace product this avatar is, when it is sold.</summary>
    public string? ProductId { get; init; }
    /// <summary>Its marketplace listings, as VRChat sends them.</summary>
    public JsonElement? PublishedListings { get; init; }
}

/// <summary>GET auth/user/avatarmoderations - one block on an avatar.</summary>
public sealed record AvatarModeration
{
    public string AvatarModerationType { get; init; } = "";
    public string TargetAvatarId { get; init; } = "";
}

public sealed record AvatarPerformance
{
    public string? Android { get; init; }
    public string? Ios { get; init; }
    public string? Standalonewindows { get; init; }
}

public sealed record AvatarStyles
{
    public string? Primary { get; init; }
    public string? Secondary { get; init; }
    public IReadOnlyList<string>? Supplementary { get; init; }
}

public sealed record UnityPackage
{
    public required string Id { get; init; }
    public required string Platform { get; init; }
    public string UnityVersion { get; init; } = "";
    public int AssetVersion { get; init; }
    public string? AssetUrl { get; init; }
    public string? PerformanceRating { get; init; }
    public string? ImpostorUrl { get; init; }
    public string? Variant { get; init; }
    /// <summary>On the "impostor" variant: the impostorizer version that made it.</summary>
    public string? ImpostorizerVersion { get; init; }
    public string? ScanStatus { get; init; }
    public long? UnitySortNumber { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>PUT file/{id}/{version}/{part}/start - where to upload that part.</summary>
public sealed record FileUploadUrl
{
    public string Url { get; init; } = "";
}

/// <summary>GET instances/{worldId}:{instanceId}.</summary>
public sealed record Instance
{
    /// <summary>Same as Location ("wrld_...:12345~region(eu)").</summary>
    public required string Id { get; init; }
    public required string Location { get; init; }
    public required string WorldId { get; init; }
    public required string InstanceId { get; init; }
    public string Name { get; init; } = "";
    public string? DisplayName { get; init; }
    public string Type { get; init; } = "public";
    public string Region { get; init; } = "us";
    public string? PhotonRegion { get; init; }
    public JsonElement? OwnerId { get; init; }
    public string? GroupAccessType { get; init; }
    public int Capacity { get; init; }
    public int RecommendedCapacity { get; init; }
    [JsonPropertyName("n_users")] public int UserCount { get; init; }
    public bool Full { get; init; }
    public bool Active { get; init; }
    public bool Permanent { get; init; }
    public bool? AgeGate { get; init; }
    public bool QueueEnabled { get; init; }
    public int QueueSize { get; init; }
    public bool? HardClose { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public string? ClientNumber { get; init; }
    public string? SecureName { get; init; }
    public string? ShortName { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public InstancePlatforms? Platforms { get; init; }
    public World? World { get; init; }
    public IReadOnlyList<LimitedUserInstance>? Users { get; init; }

    /// <summary>ownerId is a user id, a group id, or null - as a string.</summary>
    public string? OwnerIdString =>
        OwnerId is { ValueKind: JsonValueKind.String } element ? element.GetString() : null;
}

public sealed record InstancePlatforms
{
    public int Android { get; init; }
    public int Ios { get; init; }
    public int Standalonewindows { get; init; }
}

public sealed record LimitedUserInstance
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string Status { get; init; } = UserStatusValues.Offline;
    public string StatusDescription { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string DeveloperType { get; init; } = "none";
    public string? CurrentAvatarImageUrl { get; init; }
    public string? CurrentAvatarThumbnailImageUrl { get; init; }
    public string? ProfilePicOverride { get; init; }
    public string? UserIcon { get; init; }
    public string? IconUrl { get; init; }
    public bool IsFriend { get; init; }
}

public static class ReleaseStatusValues
{
    public const string Public = "public";
    public const string Private = "private";
    public const string Hidden = "hidden";
    public const string All = "all";
}

public static class InstanceTypeValues
{
    public const string Public = "public";
    /// <summary>Friends+.</summary>
    public const string Hidden = "hidden";
    public const string Friends = "friends";
    public const string Private = "private";
    public const string Group = "group";
}
