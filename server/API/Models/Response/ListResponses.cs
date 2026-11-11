using VRCX.Common.VrcxDb;

namespace VRCX.API.Models.Response;

public sealed class NotificationResponse
{
    public required string Id { get; init; }
    public required int Version { get; init; }
    public required string Type { get; init; }
    public string? SenderUserId { get; init; }
    public string? SenderDisplayName { get; init; }
    public string? Title { get; init; }
    public required string Message { get; init; }
    public string? ImageUrl { get; init; }
    public string? Link { get; init; }
    public string? LinkText { get; init; }
    /// <summary>v1 details or v2 data, as VRChat sent it.</summary>
    public System.Text.Json.JsonDocument? Details { get; init; }
    /// <summary>The answers VRChat offers on a v2 notification.</summary>
    public System.Text.Json.JsonDocument? Responses { get; init; }
    public required bool Seen { get; init; }
    public required bool Hidden { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>One game log row. Which fields are set depends on Type.</summary>
public sealed class GameLogRowResponse
{
    /// <summary>Stable row key: what a delete takes.</summary>
    public required string Key { get; init; }
    public required long Id { get; init; }
    /// <summary>Location, OnPlayerJoined, OnPlayerLeft, PortalSpawn, VideoPlay, Event, External, StringLoad or ImageLoad.</summary>
    public required string Type { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Location { get; init; }
    public string? WorldId { get; init; }
    public string? WorldName { get; init; }
    public string? GroupName { get; init; }
    public string? UserId { get; init; }
    public string? DisplayName { get; init; }
    /// <summary>How long a stay lasted, for Location and OnPlayerLeft.</summary>
    public long? TimeMs { get; init; }
    public string? VideoUrl { get; init; }
    public string? VideoName { get; init; }
    public string? VideoId { get; init; }
    /// <summary>A VideoPlay's position when logged and its length, in seconds.</summary>
    public double? VideoPos { get; init; }
    public double? VideoLength { get; init; }
    public string? ResourceUrl { get; init; }
    public string? Data { get; init; }
    public string? Message { get; init; }
    /// <summary>Where a portal leads.</summary>
    public string? InstanceId { get; init; }
    public bool IsFriend { get; init; }
    public bool IsFavorite { get; init; }

    public GameLogRowResponse WithFriend(HashSet<string> friends, HashSet<string> favorites) => new()
    {
        Key = Key, Id = Id, Type = Type, CreatedAt = CreatedAt, Location = Location, WorldId = WorldId, WorldName = WorldName, GroupName = GroupName,
        UserId = UserId, DisplayName = DisplayName, TimeMs = TimeMs, VideoUrl = VideoUrl, VideoName = VideoName, VideoId = VideoId, VideoPos = VideoPos, VideoLength = VideoLength, ResourceUrl = ResourceUrl,
        Data = Data, Message = Message, InstanceId = InstanceId,
        IsFriend = UserId is not null && friends.Contains(UserId),
        IsFavorite = UserId is not null && favorites.Contains(UserId),
    };
}

public sealed class GameLogSessionsResponse
{
    public required IReadOnlyList<GameLogRowResponse> Locations { get; init; }
    public required IReadOnlyList<GameLogRowResponse> Events { get; init; }
    public required bool HasMore { get; init; }
}

/// <summary>The account's current instance and who is in it.</summary>
public sealed class PlayerListResponse
{
    public string? Location { get; init; }
    public DateTimeOffset? JoinedAt { get; init; }
    public PlayerListWorld? World { get; init; }
    public required IReadOnlyList<PlayerListPlayer> Players { get; init; }
}

public sealed class PlayerListWorld
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? AuthorId { get; init; }
    public string? AuthorName { get; init; }
    public string? ImageUrl { get; init; }
    public string? ThumbnailImageUrl { get; init; }
    public string? ReleaseStatus { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required IReadOnlyList<string> Platforms { get; init; }
    public int? Capacity { get; init; }
    public int? RecommendedCapacity { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public required bool IsHome { get; init; }
}

public sealed class PlayerListPlayer
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required DateTimeOffset JoinedAt { get; init; }
    public required string TrustLevel { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required string DeveloperType { get; init; }
    public required string Status { get; init; }
    public string? StatusDescription { get; init; }
    public string? ThumbnailImageUrl { get; init; }
    public required IReadOnlyList<string> BioLinks { get; init; }
    public string? Note { get; init; }
    public string? Memo { get; init; }
    public string? Platform { get; init; }
    public required bool IsFriend { get; init; }
    public required bool IsBlocked { get; init; }
    public required bool IsMuted { get; init; }
    public required bool IsAvatarInteractionDisabled { get; init; }
    public required bool AgeVerified { get; init; }
}

public sealed class PlayerModerationResponse
{
    public required string Id { get; init; }
    public required string SourceUserId { get; init; }
    public required string SourceDisplayName { get; init; }
    public required string TargetUserId { get; init; }
    public required string TargetDisplayName { get; init; }
    /// <summary>block, mute, hideAvatar, interactOff, ...</summary>
    public required string Type { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}

public sealed class FavoriteResponse
{
    public required string Id { get; init; }
    public required FavoriteType Type { get; init; }
    public required string TargetId { get; init; }
    /// <summary>The user's display name, the world's name or the avatar's name - whatever we have.</summary>
    public required string TargetName { get; init; }
    public string? ThumbnailImageUrl { get; init; }
    public string? GroupName { get; init; }
    public string? GroupDisplayName { get; init; }
    /// <summary>When this server first saw the favorite: the original's sort by date.</summary>
    public DateTimeOffset? CreatedAt { get; init; }
    public string? AuthorId { get; init; }
    public string? AuthorName { get; init; }
    public string? ReleaseStatus { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public string? Status { get; init; }
    public string? StatusDescription { get; init; }
    /// <summary>A friend's presence: online, active or offline.</summary>
    public string? State { get; init; }
    public string? Location { get; init; }
    public string? TravelingToLocation { get; init; }
    public bool Missing { get; init; }
}

public sealed class FavoriteGroupResponse
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Visibility { get; init; }
}

public sealed class FavoritesResponse
{
    public required IReadOnlyList<FavoriteGroupResponse> Groups { get; init; }
    public required IReadOnlyList<FavoriteResponse> Favorites { get; init; }
}

/// <summary>One stay of a user in an instance, with what the instance was.</summary>
public sealed class InstanceVisitResponse
{
    public required long Id { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string InstanceLocation { get; init; }
    public required string WorldId { get; init; }
    public required string WorldName { get; init; }
    public required InstanceAccessType AccessType { get; init; }
    public required DateTimeOffset JoinedAt { get; init; }
    public DateTimeOffset? LeftAt { get; init; }
    public required VisitSource Source { get; init; }
}

public sealed class MemoResponse
{
    public required string UserId { get; init; }
    public required string Memo { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed class UserSearchResultResponse
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Status { get; init; }
    public string? StatusDescription { get; init; }
    public required string TrustLevel { get; init; }
    public string? ThumbnailImageUrl { get; init; }
    public required bool IsFriend { get; init; }
}

public sealed class UserRelationshipResponse
{
    public required bool IsFriend { get; init; }
    public required bool IncomingRequest { get; init; }
    public required bool OutgoingRequest { get; init; }
    public required string Note { get; init; }
}

public sealed class UserFavoriteWorldsResponse
{
    public required string Name { get; init; }
    public required string Visibility { get; init; }
    public required IReadOnlyList<VRCX.Vrchat.Models.World> Worlds { get; init; }
}

public sealed class WorldStatsResponse
{
    public required int VisitCount { get; init; }
    public DateTimeOffset? LastVisit { get; init; }
    public required long TimeSpentMs { get; init; }
}

/// <summary>The image an avatar or world now has, after Change Image.</summary>
public sealed class ContentImageResponse
{
    public required string ImageUrl { get; init; }
}

public sealed class WorldMemoResponse
{
    public required string WorldId { get; init; }
    public required string Memo { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed class AvatarDetailsResponse
{
    public VRCX.Vrchat.Models.Avatar? Avatar { get; init; }
    public required bool IsBlocked { get; init; }
    public required bool IsCurrent { get; init; }
    public required long TimeSpentMs { get; init; }
}

public sealed class AvatarMemoResponse
{
    public required string AvatarId { get; init; }
    public required string Memo { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed class GroupDetailsResponse
{
    public required VRCX.Vrchat.Models.Group Group { get; init; }
    public System.Text.Json.JsonElement? Instances { get; init; }
    public required int JoinCount { get; init; }
    public DateTimeOffset? LastVisit { get; init; }
}

public sealed class AvatarLookupResponse
{
    public string? AvatarId { get; init; }
    public string? OwnerId { get; init; }
    public string? AvatarName { get; init; }
}

public sealed class FavoriteSlotResponse
{
    public required string Type { get; init; }
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required int Capacity { get; init; }
    public required int Count { get; init; }
    public string Visibility { get; init; } = "private";
}

public sealed class LocalFavoriteGroupResponse
{
    public required string Name { get; init; }
    public required int Count { get; init; }
    public required bool Contains { get; init; }
}

public sealed class FavoritePickerResponse
{
    public required IReadOnlyList<FavoriteSlotResponse> Groups { get; init; }
    public FavoriteSlotResponse? Current { get; init; }
    public required IReadOnlyList<LocalFavoriteGroupResponse> Local { get; init; }
    public required bool IsSupporter { get; init; }
}

public sealed class LocalFavoriteResponse
{
    public required string GroupName { get; init; }
    public required string TargetId { get; init; }
    public required string TargetName { get; init; }
    public string? ThumbnailImageUrl { get; init; }
    /// <summary>For the favorites search, which matches author names and author tags (upstream issue 735).</summary>
    public string? AuthorName { get; init; }
    /// <summary>For the export's Author ID column (upstream issue 984).</summary>
    public string? AuthorId { get; init; }
    /// <summary>So local cards can mark private ones and hide Select Avatar on them, as the original.</summary>
    public string? ReleaseStatus { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
}

public sealed class PreviousInstanceResponse
{
    public required string Location { get; init; }
    public required string WorldId { get; init; }
    public required string WorldName { get; init; }
    public string? GroupName { get; init; }
    public string? OwnerUserId { get; init; }
    public string? OwnerName { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required long TimeMs { get; init; }
}

public sealed class PreviousInstancePlayerResponse
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required DateTimeOffset FirstJoinedAt { get; init; }
    public required long TimeMs { get; init; }
    public required int Count { get; init; }
    /// <summary>The original's gameLogIsFriend and gameLogIsFavorite, for the heart and star (upstream PR 777).</summary>
    public bool IsFriend { get; init; }
    public bool IsFavorite { get; init; }
}

public sealed class PreviousInstanceStayResponse
{
    public required long Id { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required DateTimeOffset JoinedAt { get; init; }
    public DateTimeOffset? LeftAt { get; init; }
    public bool IsFriend { get; set; }
    public bool IsFavorite { get; set; }
}

public sealed class PreviousInstancePlayersResponse
{
    public required IReadOnlyList<PreviousInstancePlayerResponse> Players { get; init; }
    public required IReadOnlyList<PreviousInstanceStayResponse> Stays { get; init; }
}

public sealed class InstanceOptionGroup
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? IconUrl { get; init; }
    public string? Privacy { get; init; }
    public required IReadOnlyList<string> Permissions { get; init; }
    public required bool CanCreateInstance { get; init; }
}

public sealed class DesktopStatusResponse
{
    public required bool Connected { get; init; }
    public string? ClientId { get; init; }
    public string? ClientName { get; init; }
    public required bool GameRunning { get; init; }
    /// <summary>When VRChat started on the account's PC, while it runs.</summary>
    public DateTimeOffset? GameStartedAt { get; init; }
    /// <summary>The account's last finished game session.</summary>
    public DateTimeOffset? LastGameStartedAt { get; init; }
    public DateTimeOffset? LastGameStoppedAt { get; init; }
    /// <summary>VRChat last started in desktop mode on that PC (the original's isGameNoVR).</summary>
    public bool GameNoVr { get; init; }
    /// <summary>Whether SteamVR runs on the connected PC (the original's isSteamVRRunning).</summary>
    public bool SteamVrRunning { get; init; }
    /// <summary>Nobody wears the headset (its proximity sensor), while the agent's OpenVR is active.</summary>
    public bool HmdAfk { get; init; }
    /// <summary>What the connected PC's agent does: launch, open_instance, folders, cache and so on.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    /// <summary>windows or linux.</summary>
    public string? Os { get; init; }
    /// <summary>Where VRChat keeps its files on the connected PC.</summary>
    public DesktopFolders? Folders { get; init; }
}

public sealed record DesktopFolders(string AppData, string Photos, string Screenshots, string Cache, string UgcPhotos, string CrashDumps);

public sealed class InstanceOptionsResponse
{
    public required string DisplayName { get; init; }
    public required bool IsSupporter { get; init; }
    /// <summary>18+ verified: any instance the account creates can be age gated.</summary>
    public bool AgeVerified { get; init; }
    public string? CurrentLocation { get; init; }
    public required IReadOnlyList<InstanceOptionGroup> Groups { get; init; }
}
