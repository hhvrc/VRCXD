using VRCX.Common.VrcxDb;

namespace VRCX.API.Models.Response;

public sealed class LoginResponse
{
    /// <summary>loggedIn, needsTwoFactor, or pending while a QR login waits for approval.</summary>
    public required string Status { get; init; }
    public string? UserId { get; init; }
    public string? DisplayName { get; init; }
    /// <summary>Set for needsTwoFactor: pass it back to /accounts/login/2fa with the code.</summary>
    public string? PendingId { get; init; }
    /// <summary>Methods VRChat accepts for this account: totp, otp, emailOtp.</summary>
    public IReadOnlyList<string>? Methods { get; init; }
}

/// <summary>A QR login waiting for approval: show <see cref="VerificationUrl"/> as a QR code and poll with the pendingId.</summary>
public sealed class DeviceLoginResponse
{
    public required string PendingId { get; init; }
    public required string VerificationUrl { get; init; }
    /// <summary>Seconds VRChat wants between polls.</summary>
    public int Interval { get; init; }
    public int ExpiresIn { get; init; }
}

public sealed class AccountResponse
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public string? Username { get; init; }
    public required bool SessionValid { get; init; }
    /// <summary>The custom development endpoint the account logged in against, when not VRChat's.</summary>
    public string? ApiEndpoint { get; init; }
    public string? PipelineEndpoint { get; init; }
    /// <summary>Whether this API process currently holds a live VRChat client for the account.</summary>
    public required bool Connected { get; init; }
    public required DateTimeOffset LoggedInAt { get; init; }
    public DateTimeOffset? LastFriendSyncAt { get; init; }
    public DateTimeOffset? LastFullSyncAt { get; init; }
    public string? HomeLocation { get; init; }
    /// <summary>The account's picture, as friends' are picked, for the login page's saved accounts.</summary>
    public string? ThumbnailImageUrl { get; init; }
    /// <summary>The icon frame cosmetic drawn around that picture.</summary>
    public string? IconFrame { get; init; }
    /// <summary>Whether a password is saved for logging in again. The password itself is never returned.</summary>
    public bool HasSavedCredentials { get; init; }
    /// <summary>Whether that saved password is encrypted with the primary password, so logging in with it asks for it.</summary>
    public bool SavedCredentialsNeedPrimaryPassword { get; init; }
}

/// <summary>Whether new saved passwords are encrypted with the primary password.</summary>
public sealed class PrimaryPasswordResponse
{
    public required bool Enabled { get; init; }
}

/// <summary>The picture to show for a user, in the original's userImage order. VRChat often sends no thumbnail for friends.</summary>
public static class UserImage
{
    public static string? Pick(string? iconUrl, string? thumbnailUrl, string? imageUrl) =>
        !string.IsNullOrEmpty(iconUrl) ? iconUrl
        : !string.IsNullOrEmpty(thumbnailUrl) ? thumbnailUrl
        : string.IsNullOrEmpty(imageUrl) ? null : imageUrl;
}

public sealed class FriendResponse
{
    public required int FriendNumber { get; init; }
    public required DateTimeOffset FriendsSince { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string TrustLevel { get; init; }
    public required UserState State { get; init; }
    public required string Status { get; init; }
    public string? StatusDescription { get; init; }
    public string? Location { get; init; }
    public string? TravelingToLocation { get; init; }
    /// <summary>When they arrived at Location.</summary>
    public DateTimeOffset? LocationSince { get; init; }
    /// <summary>When they last came online, went active or went offline (the original's $online_for and $offline_for).</summary>
    public DateTimeOffset? OnlineFor { get; init; }
    public DateTimeOffset? LastActivity { get; init; }
    public DateTimeOffset? LastLogin { get; init; }
    public string? ThumbnailImageUrl { get; init; }
    public string[] Tags { get; init; } = [];
    public string DeveloperType { get; init; } = "none";
    /// <summary>VRChat's last_platform, for the mobile status dot.</summary>
    public string? Platform { get; init; }
    /// <summary>The first line of the account's memo on them, which the sidebar shows beside the name.</summary>
    public string? Nickname { get; init; }
    /// <summary>Reported offline but still inside the original's grace period, so still listed online.</summary>
    public bool PendingOffline { get; init; }
    /// <summary>For "consistent colors per user", as on the Friend List.</summary>
    public int NameHue { get; init; }
    /// <summary>The icon frame VRChat shows around their picture (an inventory template id).</summary>
    public string? IconFrame { get; init; }
    /// <summary>The friend's profile effect template id, drawn behind the Friends Locations card.</summary>
    public string? ProfileEffect { get; init; }
}

/// <summary>One row of the Friend List page: everything its columns and search need.</summary>
public sealed class FriendDetailResponse
{
    public required int FriendNumber { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string TrustLevel { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required string DeveloperType { get; init; }
    public required string Status { get; init; }
    public string? StatusDescription { get; init; }
    public string? ThumbnailImageUrl { get; init; }
    public string? Bio { get; init; }
    public required IReadOnlyList<string> BioLinks { get; init; }
    public string? Note { get; init; }
    public string? Memo { get; init; }
    /// <summary>Times the account's game log saw them join.</summary>
    public required int JoinCount { get; init; }
    public required long TimeTogetherMs { get; init; }
    public DateTimeOffset? LastSeen { get; init; }
    public required int MutualCount { get; init; }
    public required bool MutualOptedOut { get; init; }
    public DateTimeOffset? LastActivity { get; init; }
    public DateTimeOffset? LastLogin { get; init; }
    public DateOnly? DateJoined { get; init; }
    public required bool IsFavorite { get; init; }
    /// <summary>The full user object or profile has never been fetched.</summary>
    public required bool NeedsLoad { get; init; }
    /// <summary>The original's random name colour hue for this user id, 0 to 65535.</summary>
    public required int NameHue { get; init; }
}

public sealed class FriendDetailsResponse
{
    public required IReadOnlyList<FriendDetailResponse> Friends { get; init; }
    /// <summary>The account has turned off sharing its connections, so mutuals can't be loaded.</summary>
    public required bool MutualOptOut { get; init; }
}

public sealed class FriendLogEntryResponse
{
    /// <summary>Stable row key: what a delete takes.</summary>
    public required string Key { get; init; }
    /// <summary>Friend, Unfriend, FriendRequest, CancelFriendRequest, DisplayName or TrustLevel.</summary>
    public required string Type { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string UserId { get; init; }
    /// <summary>Their name at the time.</summary>
    public required string DisplayName { get; init; }
    public string? PreviousDisplayName { get; init; }
    public string? TrustLevel { get; init; }
    public string? PreviousTrustLevel { get; init; }
    public int? FriendNumber { get; init; }
}

public sealed class UserResponse
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Bio { get; init; }
    public required string[] BioLinks { get; init; }
    public string? Pronouns { get; init; }
    public required string Status { get; init; }
    public string? StatusDescription { get; init; }
    public required UserState State { get; init; }
    public required string[] Tags { get; init; }
    public required string TrustLevel { get; init; }
    public required string DeveloperType { get; init; }
    public string? CurrentAvatarImageUrl { get; init; }
    public string? CurrentAvatarThumbnailImageUrl { get; init; }
    public string? ProfilePicOverride { get; init; }
    public string? UserIcon { get; init; }
    /// <summary>Where VRChat now sends the user's icon (upstream PR 1886); preferred over UserIcon.</summary>
    public string? IconUrl { get; init; }
    /// <summary>Cosmetics VRChat shows on the user: inventory template ids.</summary>
    public string? IconFrame { get; init; }
    public string? NameplateEffect { get; init; }
    public string? ProfileEffect { get; init; }
    public string? DiscordId { get; init; }
    public bool AgeVerified { get; init; }
    public string? Platform { get; init; }
    public string? LastPlatform { get; init; }
    public DateTimeOffset? LastLogin { get; init; }
    public DateTimeOffset? LastActivity { get; init; }
    public DateOnly? DateJoined { get; init; }
    public string? AgeVerificationStatus { get; init; }
    public required bool AllowAvatarCopying { get; init; }
    public string? Location { get; init; }
    public string? TravelingToLocation { get; init; }
    public string? CurrentInstanceLocation { get; init; }
    public DateTimeOffset? PresenceChangedAt { get; init; }
    /// <summary>When they last came online, went active or went offline.</summary>
    public DateTimeOffset? StateSince { get; init; }
    public required bool IsStub { get; init; }
    public required DateTimeOffset FirstSeenAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? LastFullFetchAt { get; init; }
    public required IReadOnlyList<VRCX.Vrchat.Models.Badge> Badges { get; init; }

    public static UserResponse From(User u, VRCX.Common.Services.Presence.UserPresence p) => new()
    {
        Id = u.Id, DisplayName = u.DisplayName, Bio = u.Bio, BioLinks = u.BioLinks, Pronouns = u.Pronouns,
        Status = u.Status, StatusDescription = u.StatusDescription, State = p.State, Tags = u.Tags, TrustLevel = u.TrustLevel,
        DeveloperType = u.DeveloperType, CurrentAvatarImageUrl = u.CurrentAvatarImageUrl, CurrentAvatarThumbnailImageUrl = u.CurrentAvatarThumbnailImageUrl,
        ProfilePicOverride = u.ProfilePicOverride, UserIcon = u.UserIcon, IconUrl = u.IconUrl,
        IconFrame = NullIfEmpty(u.IconFrame), NameplateEffect = NullIfEmpty(u.NameplateEffect), ProfileEffect = NullIfEmpty(u.ProfileEffect),
        DiscordId = NullIfEmpty(u.DiscordId), AgeVerified = u.AgeVerified, Platform = p.Platform, LastPlatform = u.LastPlatform,
        LastLogin = u.LastLogin, LastActivity = u.LastActivity, DateJoined = u.DateJoined, AgeVerificationStatus = u.AgeVerificationStatus,
        AllowAvatarCopying = u.AllowAvatarCopying, Location = p.Location, TravelingToLocation = p.TravelingToLocation,
        CurrentInstanceLocation = p.CurrentInstanceLocation, PresenceChangedAt = p.PresenceChangedAt, StateSince = p.StateSince ?? p.PresenceChangedAt, IsStub = u.IsStub,
        FirstSeenAt = u.FirstSeenAt, UpdatedAt = u.UpdatedAt, LastFullFetchAt = u.LastFullFetchAt, Badges = u.Badges,
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

public sealed class UserHistoryResponse
{
    public required IReadOnlyList<UserDisplayNameChange> DisplayNames { get; init; }
    public required IReadOnlyList<UserTrustChange> Trust { get; init; }
    public required IReadOnlyList<UserStatusChange> Status { get; init; }
    public required IReadOnlyList<UserBioChange> Bio { get; init; }
    public required IReadOnlyList<UserAvatarChange> Avatars { get; init; }
    public required IReadOnlyList<PresenceEvent> Presence { get; init; }
    public required IReadOnlyList<VisitResponse> Visits { get; init; }
}

public sealed class VisitResponse
{
    public required string InstanceLocation { get; init; }
    public required string WorldName { get; init; }
    public required DateTimeOffset JoinedAt { get; init; }
    public DateTimeOffset? LeftAt { get; init; }
    public required VisitSource Source { get; init; }
}

public sealed class WorldResponse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? AuthorId { get; init; }
    public string? AuthorName { get; init; }
    public int? Capacity { get; init; }
    public int? RecommendedCapacity { get; init; }
    public string? ImageUrl { get; init; }
    public string? ThumbnailImageUrl { get; init; }
    public string? ReleaseStatus { get; init; }
    public required string[] Tags { get; init; }
    public int? Version { get; init; }
    public int? Visits { get; init; }
    public int? Favorites { get; init; }
    public required string[] Platforms { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public required bool IsStub { get; init; }
    public DateTimeOffset? FetchedAt { get; init; }

    public static WorldResponse From(World w) => new()
    {
        Id = w.Id, Name = w.Name, Description = w.Description, AuthorId = w.AuthorId, AuthorName = w.Author is { IsStub: false } author ? author.DisplayName : w.AuthorName,
        Capacity = w.Capacity, RecommendedCapacity = w.RecommendedCapacity, ImageUrl = w.ImageUrl, ThumbnailImageUrl = w.ThumbnailImageUrl,
        ReleaseStatus = w.ReleaseStatus, Tags = w.Tags, Version = w.Version, Visits = w.Visits, Favorites = w.Favorites, Platforms = w.Platforms, CreatedAt = w.CreatedAt, UpdatedAt = w.UpdatedAt,
        IsStub = w.IsStub, FetchedAt = w.FetchedAt,
    };
}

public sealed class AvatarResponse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? AuthorId { get; init; }
    public string? AuthorName { get; init; }
    public string? ImageUrl { get; init; }
    public string? ThumbnailImageUrl { get; init; }
    public string? ReleaseStatus { get; init; }
    public required string[] Tags { get; init; }
    public int? Version { get; init; }
    public string? PerformancePc { get; init; }
    public string? PerformanceAndroid { get; init; }
    public string? PerformanceIos { get; init; }
    public required string[] Platforms { get; init; }
    public string? ImpostorVersion { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public required bool IsStub { get; init; }
    public DateTimeOffset? FetchedAt { get; init; }

    public static AvatarResponse From(Avatar a) => new()
    {
        Id = a.Id, Name = a.Name, Description = a.Description, AuthorId = a.AuthorId, AuthorName = a.Author is { IsStub: false } author ? author.DisplayName : a.AuthorName, ImageUrl = a.ImageUrl,
        ThumbnailImageUrl = a.ThumbnailImageUrl, ReleaseStatus = a.ReleaseStatus, Tags = a.Tags, Version = a.Version,
        PerformancePc = a.PerformancePc, PerformanceAndroid = a.PerformanceAndroid, PerformanceIos = a.PerformanceIos, Platforms = a.Platforms,
        ImpostorVersion = a.ImpostorVersion, CreatedAt = a.CreatedAt, UpdatedAt = a.UpdatedAt, IsStub = a.IsStub, FetchedAt = a.FetchedAt,
    };
}

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
