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
