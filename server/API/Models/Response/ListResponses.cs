using VRCX.Common.VrcxDb;

namespace VRCX.API.Models.Response;

public sealed class MemoResponse
{
    public required string UserId { get; init; }
    public required string Memo { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
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
