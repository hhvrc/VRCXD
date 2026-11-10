using System.Text.Json.Serialization;

namespace VRCX.Vrchat.Models;

// Field names follow VRChat's JSON exactly (the community OpenAPI spec at
// vrchatapi.github.io/specification is the reference), so a property here
// is a property on the wire. Everything optional is nullable: VRChat omits,
// nulls and empties fields freely, and the same user looks different
// depending on which endpoint returned them (see LimitedUserFriend).
//
// Values that VRChat models as enums (status, state, developerType, ...)
// stay strings here: they contain spaces ("join me"), gain new values
// without notice, and we persist them as text anyway. Known values live in
// the *Values classes below.

/// <summary>GET auth/user - the logged-in account's own view of itself.</summary>
public sealed record CurrentUser
{
    public required string Id { get; init; }
    /// <summary>True when the account has turned off mutual-friend sharing (then nobody's mutuals can be fetched).</summary>
    public bool HasSharedConnectionsOptOut { get; init; }
    /// <summary>True when the account hides its Discord connections.</summary>
    public bool HasDiscordFriendsOptOut { get; init; }
    public string? Username { get; init; }
    public required string DisplayName { get; init; }
    public string Bio { get; init; } = "";
    public IReadOnlyList<string> BioLinks { get; init; } = [];
    public string Pronouns { get; init; } = "";
    public string Status { get; init; } = UserStatusValues.Offline;
    public string StatusDescription { get; init; } = "";
    public string State { get; init; } = UserStateValues.Offline;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string DeveloperType { get; init; } = "none";
    public string? CurrentAvatar { get; init; }
    public string? FallbackAvatar { get; init; }
    public string CurrentAvatarImageUrl { get; init; } = "";
    public string CurrentAvatarThumbnailImageUrl { get; init; } = "";
    public IReadOnlyList<string> CurrentAvatarTags { get; init; } = [];
    public string? ProfilePicOverride { get; init; }
    public string? ProfilePicOverrideThumbnail { get; init; }
    public string? UserIcon { get; init; }
    /// <summary>The image shown as the user's profile picture: a custom icon, or their avatar's image.</summary>
    public string? IconUrl { get; init; }
    public string? BannerUrl { get; init; }
    /// <summary>avatarBanner, color or customImage.</summary>
    public string? BannerType { get; init; }
    /// <summary>Cosmetics: the inventory template ids of the icon frame, nameplate effect and profile effect shown.</summary>
    public string? IconFrame { get; init; }
    public string? NameplateEffect { get; init; }
    public string? ProfileEffect { get; init; }
    /// <summary>The linked Discord account, when the user shares it.</summary>
    public string? DiscordId { get; init; }
    public string? HomeLocation { get; init; }
    public string? Location { get; init; }
    public string? TravelingToLocation { get; init; }
    public string? WorldId { get; init; }
    public string? InstanceId { get; init; }
    public string? Platform { get; init; }
    [JsonPropertyName("last_platform")] public string? LastPlatform { get; init; }
    [JsonPropertyName("last_login")] public DateTimeOffset? LastLogin { get; init; }
    [JsonPropertyName("last_activity")] public DateTimeOffset? LastActivity { get; init; }
    [JsonPropertyName("last_mobile")] public DateTimeOffset? LastMobile { get; init; }
    [JsonPropertyName("date_joined")] public DateOnly? DateJoined { get; init; }
    public string? AgeVerificationStatus { get; init; }
    public bool AgeVerified { get; init; }
    public bool AllowAvatarCopying { get; init; }
    public bool IsFriend { get; init; }
    public bool TwoFactorAuthEnabled { get; init; }
    public bool EmailVerified { get; init; }
    public bool HasEmail { get; init; }
    public string? ObfuscatedEmail { get; init; }
    public IReadOnlyList<string> Friends { get; init; } = [];
    public IReadOnlyList<string> OnlineFriends { get; init; } = [];
    public IReadOnlyList<string> ActiveFriends { get; init; } = [];
    public IReadOnlyList<string> OfflineFriends { get; init; } = [];
    public IReadOnlyList<string> FriendGroupNames { get; init; } = [];
    public IReadOnlyList<PastDisplayName> PastDisplayNames { get; init; } = [];
    public IReadOnlyList<string> StatusHistory { get; init; } = [];
    public bool IsBoopingEnabled { get; init; } = true;
    public IReadOnlyList<Badge> Badges { get; init; } = [];
    public CurrentUserPresence? Presence { get; init; }
    public string? QueuedInstance { get; init; }
    public string? AccountStanding { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Set only in the login response when the account has 2FA - and then nothing else is.</summary>
    public IReadOnlyList<string>? RequiresTwoFactorAuth { get; init; }
}

public sealed record CurrentUserPresence
{
    public string? Id { get; init; }
    public string? DisplayName { get; init; }
    public string? Status { get; init; }
    public string? Platform { get; init; }
    public string? World { get; init; }
    public string? Instance { get; init; }
    public string? InstanceType { get; init; }
    public string? TravelingToWorld { get; init; }
    public string? TravelingToInstance { get; init; }
    public string? AvatarThumbnail { get; init; }
    public string? UserIcon { get; init; }
    /// <summary>The image shown as the user's profile picture: a custom icon, or their avatar's image.</summary>
    public string? IconUrl { get; init; }
    public string? BannerUrl { get; init; }
    /// <summary>avatarBanner, color or customImage.</summary>
    public string? BannerType { get; init; }
    public string? ProfilePicOverride { get; init; }
    public IReadOnlyList<string>? Groups { get; init; }
}

/// <summary>GET users/{id} - what anyone can see about a user.</summary>
public sealed record User
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string Bio { get; init; } = "";
    public IReadOnlyList<string> BioLinks { get; init; } = [];
    public string Pronouns { get; init; } = "";
    public string Status { get; init; } = UserStatusValues.Offline;
    public string StatusDescription { get; init; } = "";
    public string State { get; init; } = UserStateValues.Offline;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string DeveloperType { get; init; } = "none";
    public string CurrentAvatarImageUrl { get; init; } = "";
    public string CurrentAvatarThumbnailImageUrl { get; init; } = "";
    public IReadOnlyList<string> CurrentAvatarTags { get; init; } = [];
    public string? ProfilePicOverride { get; init; }
    public string? ProfilePicOverrideThumbnail { get; init; }
    public string? UserIcon { get; init; }
    /// <summary>The image shown as the user's profile picture: a custom icon, or their avatar's image.</summary>
    public string? IconUrl { get; init; }
    public string? BannerUrl { get; init; }
    /// <summary>avatarBanner, color or customImage.</summary>
    public string? BannerType { get; init; }
    /// <summary>Cosmetics: the inventory template ids of the icon frame, nameplate effect and profile effect shown.</summary>
    public string? IconFrame { get; init; }
    public string? NameplateEffect { get; init; }
    public string? ProfileEffect { get; init; }
    /// <summary>The linked Discord account, when the user shares it.</summary>
    public string? DiscordId { get; init; }
    public string? Location { get; init; }
    public string? TravelingToLocation { get; init; }
    public string? WorldId { get; init; }
    public string? InstanceId { get; init; }
    public string? Platform { get; init; }
    [JsonPropertyName("last_platform")] public string? LastPlatform { get; init; }
    [JsonPropertyName("last_login")] public DateTimeOffset? LastLogin { get; init; }
    [JsonPropertyName("last_activity")] public DateTimeOffset? LastActivity { get; init; }
    [JsonPropertyName("last_mobile")] public DateTimeOffset? LastMobile { get; init; }
    [JsonPropertyName("date_joined")] public DateOnly? DateJoined { get; init; }
    public string? AgeVerificationStatus { get; init; }
    public bool AgeVerified { get; init; }
    public bool AllowAvatarCopying { get; init; }
    public bool IsFriend { get; init; }
    /// <summary>"null" (the string), "incoming", "outgoing" or "completed".</summary>
    public string? FriendRequestStatus { get; init; }
    public string? Note { get; init; }
    public IReadOnlyList<Badge> Badges { get; init; } = [];
}

/// <summary>GET auth/user/friends - a friend as the list endpoint returns them (no date joined, no state).</summary>
/// <summary>GET profile/{id}: the only place VRChat serves a user's bio from now.</summary>
public sealed record PublicProfile
{
    public string Id { get; init; } = "";
    public string? Bio { get; init; }
    public IReadOnlyList<string>? BioLinks { get; init; }
    public IReadOnlyList<Badge>? Badges { get; init; }
    /// <summary>Profile theme colours (hex, with or without #), shown in the user dialog.</summary>
    public string? ThemeIconColor { get; init; }
    public string? ThemeButtonColor { get; init; }
    public string? ThemeSubtextColor { get; init; }
    /// <summary>"gradient" or "texture".</summary>
    public string? BackgroundType { get; init; }
    public string? BackgroundGradientTop { get; init; }
    public string? BackgroundGradientBottom { get; init; }
    public string? BackgroundTextureId { get; init; }
    /// <summary>Shown as a blue check after the name in the user dialog (upstream issue 1846).</summary>
    public bool? IsEconomyCreator { get; init; }
    public bool? HasVrcPlus { get; init; }

    public const string SupporterBadgeId = "bdg_754f9935-0f97-49d8-b857-95afb9b673fa";

    /// <summary>
    /// The original's applyPublicProfile: VRChat leaves the VRC+ Supporter badge out of a supporter's
    /// badges, so it goes first, hidden and not showcased.
    /// </summary>
    public PublicProfile WithSupporterBadge()
    {
        if (HasVrcPlus != true || Badges is null || Badges.Any(b => b.BadgeId == SupporterBadgeId)) return this;
        Badge supporter = new()
        {
            BadgeId = SupporterBadgeId,
            BadgeName = "Supporter",
            BadgeDescription = "Supports VRChat through VRC+",
            BadgeImageUrl = "https://assets.vrchat.com/badges/fa/bdgai_583f6b13-91ab-4e1b-974e-ab91600b06cb.png",
            Hidden = true,
            Showcased = false,
        };
        return this with { Badges = [supporter, .. Badges] };
    }
}

/// <summary>GET users/{id}/mutuals: how many friends and groups the account shares with a user.</summary>
public sealed record MutualCounts
{
    public int Friends { get; init; }
    public int Groups { get; init; }
}

public sealed record LimitedUserFriend
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Bio { get; init; }
    public IReadOnlyList<string>? BioLinks { get; init; }
    public string Status { get; init; } = UserStatusValues.Offline;
    public string StatusDescription { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string DeveloperType { get; init; } = "none";
    public string? CurrentAvatarImageUrl { get; init; }
    public string? CurrentAvatarThumbnailImageUrl { get; init; }
    public IReadOnlyList<string>? CurrentAvatarTags { get; init; }
    public string? ProfilePicOverride { get; init; }
    public string? ProfilePicOverrideThumbnail { get; init; }
    public string? UserIcon { get; init; }
    /// <summary>The image shown as the user's profile picture: a custom icon, or their avatar's image.</summary>
    public string? IconUrl { get; init; }
    public string? BannerUrl { get; init; }
    /// <summary>avatarBanner, color or customImage.</summary>
    public string? BannerType { get; init; }
    /// <summary>Cosmetics: the inventory template ids of the icon frame, nameplate effect and profile effect shown.</summary>
    public string? IconFrame { get; init; }
    public string? NameplateEffect { get; init; }
    public string? ProfileEffect { get; init; }
    public string Location { get; init; } = "offline";
    public string? Platform { get; init; }
    [JsonPropertyName("last_platform")] public string? LastPlatform { get; init; }
    [JsonPropertyName("last_login")] public DateTimeOffset? LastLogin { get; init; }
    [JsonPropertyName("last_activity")] public DateTimeOffset? LastActivity { get; init; }
    [JsonPropertyName("last_mobile")] public DateTimeOffset? LastMobile { get; init; }
    public bool IsFriend { get; init; } = true;
}

/// <summary>GET users?search= - the search result shape.</summary>
public sealed record LimitedUserSearch
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Bio { get; init; }
    public IReadOnlyList<string>? BioLinks { get; init; }
    public string? Pronouns { get; init; }
    public string Status { get; init; } = UserStatusValues.Offline;
    public string StatusDescription { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string DeveloperType { get; init; } = "none";
    public string? CurrentAvatarImageUrl { get; init; }
    public string? CurrentAvatarThumbnailImageUrl { get; init; }
    public string? ProfilePicOverride { get; init; }
    public string? UserIcon { get; init; }
    /// <summary>The image shown as the user's profile picture: a custom icon, or their avatar's image.</summary>
    public string? IconUrl { get; init; }
    public string? BannerUrl { get; init; }
    /// <summary>avatarBanner, color or customImage.</summary>
    public string? BannerType { get; init; }
    /// <summary>Cosmetics: the inventory template ids of the icon frame, nameplate effect and profile effect shown.</summary>
    public string? IconFrame { get; init; }
    public string? NameplateEffect { get; init; }
    public string? ProfileEffect { get; init; }
    [JsonPropertyName("last_platform")] public string? LastPlatform { get; init; }
    public bool IsFriend { get; init; }
}

public sealed record PastDisplayName
{
    public required string DisplayName { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; init; }
    public bool Reverted { get; init; }
}

public sealed record Badge
{
    public required string BadgeId { get; init; }
    public string BadgeName { get; init; } = "";
    public string BadgeDescription { get; init; } = "";
    public string BadgeImageUrl { get; init; } = "";
    public bool Showcased { get; init; }
    public bool? Hidden { get; init; }
    public DateTimeOffset? AssignedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>GET user/{id}/friendStatus.</summary>
public sealed record FriendStatus
{
    public bool IsFriend { get; init; }
    public bool IncomingRequest { get; init; }
    public bool OutgoingRequest { get; init; }
}

/// <summary>GET userNotes - the notes the account has written about other users.</summary>
public sealed record UserNote
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public required string TargetUserId { get; init; }
    public string Note { get; init; } = "";
    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>GET auth/user/playermoderations - blocks, mutes, avatar hides.</summary>
public sealed record PlayerModeration
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string SourceUserId { get; init; }
    public string SourceDisplayName { get; init; } = "";
    public required string TargetUserId { get; init; }
    public string TargetDisplayName { get; init; } = "";
    public DateTimeOffset? Created { get; init; }
}

public static class UserStatusValues
{
    public const string Active = "active";
    public const string JoinMe = "join me";
    public const string AskMe = "ask me";
    public const string Busy = "busy";
    public const string Offline = "offline";
}

public static class UserStateValues
{
    /// <summary>In VRChat right now.</summary>
    public const string Online = "online";
    /// <summary>Logged in on the website/app, not in-game.</summary>
    public const string Active = "active";
    public const string Offline = "offline";
}

public static class PlayerModerationTypes
{
    public const string Block = "block";
    public const string Mute = "mute";
    public const string Unmute = "unmute";
    public const string HideAvatar = "hideAvatar";
    public const string ShowAvatar = "showAvatar";
    public const string InteractOn = "interactOn";
    public const string InteractOff = "interactOff";
    public const string MuteChat = "muteChat";
}

/// <summary>The trust rank implied by a user's system_trust_* tags, as VRCX labels it.</summary>
public static class TrustLevel
{
    public const string Visitor = "Visitor";
    public const string NewUser = "New User";
    public const string UserRank = "User";
    public const string KnownUser = "Known User";
    public const string TrustedUser = "Trusted User";

    // Same mapping as VRCX's userTransforms.js#computeTrustLevel: the
    // "veteran" tag is what the UI calls "Trusted User".
    public static string FromTags(IEnumerable<string> tags)
    {
        var best = Visitor;
        foreach (var tag in tags)
        {
            var level = tag switch
            {
                "system_trust_veteran" => TrustedUser,
                "system_trust_trusted" => KnownUser,
                "system_trust_known" => UserRank,
                "system_trust_basic" => NewUser,
                _ => null,
            };
            if (level is not null && Rank(level) > Rank(best)) best = level;
        }
        return best;
    }

    public static int Rank(string level) => level switch
    {
        TrustedUser => 4,
        KnownUser => 3,
        UserRank => 2,
        NewUser => 1,
        _ => 0,
    };
}
