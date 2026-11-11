namespace VRCX.Common.VrcxDb;

// Every VRChat user we have ever heard of - friends, instance occupants,
// world/avatar authors, group owners. One row per usr_ id, shared by every
// logged-in account: a display name is the same fact no matter who looked.
// The *Change tables below are the history; this row is the latest state.
// Where they are right now lives in Redis (UserPresence), logged forever in presence_events.
public sealed class User
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }
    public string? Bio { get; set; }
    public string[] BioLinks { get; set; } = [];
    public string? Pronouns { get; set; }
    public string Status { get; set; } = "offline";
    public string? StatusDescription { get; set; }
    public string[] Tags { get; set; } = [];
    /// <summary>Derived from Tags on every write (Visitor .. Trusted User) so it can be indexed and sorted.</summary>
    public string TrustLevel { get; set; } = "Visitor";
    public string DeveloperType { get; set; } = "none";
    public string? CurrentAvatarImageUrl { get; set; }
    public string? CurrentAvatarThumbnailImageUrl { get; set; }
    public string[] CurrentAvatarTags { get; set; } = [];
    public string? ProfilePicOverride { get; set; }
    public string? UserIcon { get; set; }
    /// <summary>The profile picture VRChat shows: a custom icon, or the avatar's image. What avatar changes are detected on.</summary>
    public string? IconUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? BannerType { get; set; }
    /// <summary>Cosmetics VRChat shows on the user: inventory template ids (invt_...).</summary>
    public string? IconFrame { get; set; }
    public string? NameplateEffect { get; set; }
    public string? ProfileEffect { get; set; }
    /// <summary>The linked Discord account, when the user shares it.</summary>
    public string? DiscordId { get; set; }
    public string? LastPlatform { get; set; }
    public DateTimeOffset? LastLogin { get; set; }
    public DateTimeOffset? LastActivity { get; set; }
    public DateTimeOffset? LastMobile { get; set; }
    public DateOnly? DateJoined { get; set; }
    public string? AgeVerificationStatus { get; set; }
    public bool AgeVerified { get; set; }
    public bool AllowAvatarCopying { get; set; }
    /// <summary>The profile's badges, from profile/{id}, stored whole as jsonb.</summary>
    public List<VRCX.Vrchat.Models.Badge> Badges { get; set; } = [];

    // Bookkeeping.
    /// <summary>True while all we know is the id (and maybe a name) from a reference - an author id, a join log line.</summary>
    public bool IsStub { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Last time a GET users/{id} (the complete object) was stored.</summary>
    public DateTimeOffset? LastFullFetchAt { get; set; }
    /// <summary>When Bio was last read from profile/{id}. Null means Bio has never been read.</summary>
    public DateTimeOffset? ProfileFetchedAt { get; set; }

    public ICollection<Friendship> FriendshipsAsFriend { get; set; } = [];
    public ICollection<GroupMember> GroupMemberships { get; set; } = [];
    public ICollection<InstanceVisit> Visits { get; set; } = [];
}

/// <summary>A VRChat account logged into this server. The owner side of every owner-scoped table.</summary>
/// <summary>
/// Per-account app settings (a jsonb column on accounts). Defaults match the original's.
/// </summary>
/// <summary>The original's values for answering invite requests.</summary>
public static class AutoAcceptMode
{
    public const string Off = "Off";
    public const string AllFavorites = "All Favorites";
    public const string SelectedFavorites = "Selected Favorites";
}

public sealed record AccountSettings
{
    /// <summary>Embed world and instance data into VRChat screenshots as PNG metadata.</summary>
    public bool ScreenshotHelper { get; init; } = true;
    /// <summary>Also add the world id to the screenshot's file name.</summary>
    public bool ScreenshotHelperModifyFilename { get; init; }
    /// <summary>Copy each screenshot to the clipboard of the PC that took it.</summary>
    public bool ScreenshotHelperCopyToClipboard { get; init; }
    /// <summary>Fetch video titles and durations through the YouTube Data API.</summary>
    public bool YoutubeApi { get; init; }
    public string YoutubeApiKey { get; init; } = "";
    /// <summary>Kill VRChat after the game logs that it is quitting (it often hangs on exit).</summary>
    public bool VrcQuitFix { get; init; } = true;
    /// <summary>Store Udon string/image loads from the game log.</summary>
    public bool LogResourceLoad { get; init; }
    /// <summary>Write the game log's Udon exceptions to the server log (the original's console).</summary>
    public bool UdonExceptionLogging { get; init; }

    /// <summary>Change the status by who shares the instance.</summary>
    public bool AutoStateChangeEnabled { get; init; }
    public string AutoStateChangeAloneStatus { get; init; } = "join me";
    public string AutoStateChangeCompanyStatus { get; init; } = "busy";
    /// <summary>Instance type keys the rule is active in; empty means all.</summary>
    public string[] AutoStateChangeInstanceTypes { get; init; } = [];
    /// <summary>Alone means no friend in the instance, rather than no other player.</summary>
    public bool AutoStateChangeNoFriends { get; init; }
    public bool AutoStateChangeAloneDescEnabled { get; init; }
    public string AutoStateChangeAloneDesc { get; init; } = "";
    public bool AutoStateChangeCompanyDescEnabled { get; init; }
    public string AutoStateChangeCompanyDesc { get; init; } = "";
    /// <summary>Favorite groups whose friends count as company; empty means every friend.</summary>
    public string[] AutoStateChangeGroups { get; init; } = [];
    /// <summary>See <see cref="AutoAcceptMode"/>.</summary>
    public string AutoAcceptInviteRequests { get; init; } = AutoAcceptMode.Off;
    public string[] AutoAcceptInviteGroups { get; init; } = [];
    /// <summary>Own statuses invite requests are answered in; empty means all.</summary>
    public string[] AutoAcceptInviteStatuses { get; init; } = [];
    /// <summary>Decline friend requests from people never seen in an instance (the original's VRCX_autoDeclineFriendRequests).</summary>
    public bool AutoDeclineFriendRequests { get; init; }
    /// <summary>Delete the oldest prints that aren't favorites when the account is near its print limit (the original's VRCX_autoDeleteOldPrints).</summary>
    public bool AutoDeleteOldPrints { get; init; }
    /// <summary>Start VRChat again in its last instance when it closes without quitting (the original's VRCX_relaunchVRChatAfterCrash).</summary>
    public bool RelaunchVrchatAfterCrash { get; init; }
    /// <summary>Back up VRChat's registry every three days, keeping two weeks of auto backups (VRCX_vrcRegistryAutoBackup).</summary>
    public bool VrcRegistryAutoBackup { get; init; } = true;
    /// <summary>Offer a backup when VRChat's registry is gone; the original also backs up only with this on (VRCX_vrcRegistryAskRestore).</summary>
    public bool VrcRegistryAskRestore { get; init; } = true;
    /// <summary>Sweep VRChat's cache of old versions when the game closes (VRCX_autoSweepVRChatCache).</summary>
    public bool AutoSweepVrchatCache { get; init; }

    /// <summary>OpenVR on the PC (the original's openVR): HMD AFK and the VR overlays.</summary>
    public bool OpenVr { get; init; }
    /// <summary>Keep OpenVR on while SteamVR runs, with VRChat closed or in desktop mode (openVRAlways).</summary>
    public bool OpenVrAlways { get; init; }
    /// <summary>VRCX_overlayWrist: the wrist feed overlay.</summary>
    public bool OverlayWrist { get; init; }
    /// <summary>VRCX_overlaybutton: the menu button shows the wrist overlay instead of grip.</summary>
    public bool OverlayButton { get; init; }
    /// <summary>VRCX_overlayHand: 0 either, 1 left, 2 right.</summary>
    public int OverlayHand { get; init; }
    /// <summary>The original's VRCX_selfInviteOverride toggle.</summary>
    public bool SelfInviteOverride { get; init; }
    /// <summary>Where the PC's agent saves prints, stickers and emoji; empty means VRChat's photos folder (VRCX_userGeneratedContentPath).</summary>
    public string UgcFolderPath { get; init; } = "";
    /// <summary>Save prints spawned around you into the UGC folder (VRCX_saveInstancePrints).</summary>
    public bool SaveInstancePrints { get; init; }
    /// <summary>Cut saved prints to their picture (VRCX_cropInstancePrints).</summary>
    public bool CropInstancePrints { get; init; }
    /// <summary>Save stickers spawned around you (VRCX_saveInstanceStickers).</summary>
    public bool SaveInstanceStickers { get; init; }
    /// <summary>Save emoji used around you (VRCX_saveInstanceEmoji).</summary>
    public bool SaveInstanceEmoji { get; init; }
    /// <summary>Discord Rich Presence through the PC's agent (discordActive and the rest of the original's Discord options).</summary>
    public bool DiscordActive { get; init; }
    public bool DiscordInstance { get; init; } = true;
    public bool DiscordHideInvite { get; init; } = true;
    public bool DiscordJoinButton { get; init; }
    public bool DiscordHideImage { get; init; }
    public bool DiscordShowPlatform { get; init; } = true;
    public bool DiscordWorldIntegration { get; init; } = true;
    public bool DiscordWorldNameAsDiscordStatus { get; init; }
    public bool DiscordMediaImage { get; init; }
    /// <summary>The Photon lobby: how long without movement makes a player timed out (VRCX_photonLobbyTimeoutThreshold, ms).</summary>
    public int PhotonLobbyTimeoutThreshold { get; init; } = 6000;
    /// <summary>Chatbox messages not shown: users by id with their name (VRCX_chatboxUserBlacklist), and keywords (VRCX_chatboxBlacklist).</summary>
    public Dictionary<string, string> ChatboxUserBlacklist { get; init; } = [];
    public string[] ChatboxBlacklist { get; init; } = [.. VRCX.Common.Services.Live.Photon.PhotonLobby.DefaultChatboxBlacklist];
    /// <summary>The Photon event table's type filter (VRCX_photonEventTypeFilter).</summary>
    public string[] PhotonEventTypeFilter { get; init; } = [];
    /// <summary>The Photon event HUD on the SteamVR overlay (VRCX_PhotonEventOverlay), whose events: VIP, Friends or Everyone (VRCX_PhotonEventOverlayFilter).</summary>
    public bool PhotonEventOverlay { get; init; }
    public string PhotonEventOverlayFilter { get; init; } = "Everyone";
    /// <summary>The event types the HUD shows, all when empty (VRCX_photonEventTypeOverlayFilter).</summary>
    public string[] PhotonEventTypeOverlayFilter { get; init; } = [];
    /// <summary>How long a HUD line stays, in ms (VRCX_photonOverlayMessageTimeout).</summary>
    public int PhotonOverlayMessageTimeout { get; init; } = 6000;
    /// <summary>The timeout HUD on the SteamVR overlay (VRCX_TimeoutHudOverlay) and whose timeouts it shows (VRCX_TimeoutHudOverlayFilter).</summary>
    public bool TimeoutHudOverlay { get; init; }
    public string TimeoutHudOverlayFilter { get; init; } = "Everyone";
    /// <summary>The video progress pie on the SteamVR overlay (VRCX_progressPie), only in dance worlds with their own video lines (VRCX_progressPieFilter).</summary>
    public bool ProgressPie { get; init; }
    public bool ProgressPieFilter { get; init; } = true;
    /// <summary>The app launcher: start the shortcuts in the agent's startup folder with VRChat (VRCX_enableAppLauncher).</summary>
    public bool EnableAppLauncher { get; init; } = true;
    /// <summary>Close what the app launcher started when VRChat closes (VRCX_enableAppLauncherAutoClose).</summary>
    public bool EnableAppLauncherAutoClose { get; init; } = true;
    /// <summary>Don't start a shortcut that already runs (VRCX_enableAppLauncherRunProcessOnce).</summary>
    public bool EnableAppLauncherRunProcessOnce { get; init; } = true;
    /// <summary>Favorite groups whose friends count as favorites (VIP): "friend:group_N" or "local:Name"; empty means all.</summary>
    public string[] FavoriteFriendGroups { get; init; } = [];
    /// <summary>
    /// Delete the account's avatar feed rows older than this many days, checked once a week (the
    /// original's VRCX_avatarAutoCleanup: Off, 30, 90, 180 or 365). Off deletes nothing.
    /// </summary>
    public string AvatarAutoCleanup { get; init; } = "Off";
    /// <summary>When the auto-cleanup last ran (the original's VRCX_lastAvatarCleanupDate_{userId}). Kept by the server, not the client.</summary>
    public DateTimeOffset? LastAvatarCleanupAt { get; init; }
    /// <summary>Wait before logging in again with saved credentials (the original's VRCX_autoLoginDelayEnabled).</summary>
    public bool AutoLoginDelayEnabled { get; init; }
    /// <summary>Seconds to wait, 0 to 10 (VRCX_autoLoginDelaySeconds).</summary>
    public int AutoLoginDelaySeconds { get; init; }
}

public sealed class Account
{
    public required string UserId { get; set; }
    public User User { get; set; } = null!;
    public string? Username { get; set; }
    /// <summary>The API the account logged in against when it is not VRChat's (the original's development endpoint).</summary>
    public string? ApiEndpoint { get; set; }
    /// <summary>The pipeline websocket that goes with <see cref="ApiEndpoint"/>.</summary>
    public string? PipelineEndpoint { get; set; }
    /// <summary>The session cookie jar, so a restart does not force a re-login.</summary>
    public List<StoredCookie> Cookies { get; set; } = [];
    public string? HomeLocation { get; set; }
    public string? CurrentAvatarId { get; set; }
    /// <summary>When CurrentAvatarId was put on, so time worn can be added to avatar_usages at the next swap.</summary>
    public DateTimeOffset? CurrentAvatarSince { get; set; }
    /// <summary>The Favorites page's avatar history starts after this. Clearing it keeps the usage rows.</summary>
    public DateTimeOffset? AvatarHistoryClearedAt { get; set; }
    public string[] FriendGroupNames { get; set; } = [];
    public bool TwoFactorEnabled { get; set; }
    /// <summary>The account turned off mutual-friend sharing, so the mutual graph cannot be fetched.</summary>
    public bool SharedConnectionsOptOut { get; set; }
    /// <summary>False once VRChat answered 401 - the row stays so history keeps its owner, but nothing runs for it.</summary>
    public bool SessionValid { get; set; } = true;
    public DateTimeOffset LoggedInAt { get; set; }
    /// <summary>The app settings the original kept in its config file; what the server asks the desktop app to do.</summary>
    public AccountSettings Settings { get; set; } = new();
    public DateTimeOffset? LastFriendSyncAt { get; set; }
    public DateTimeOffset? LastFullSyncAt { get; set; }

    public ICollection<Friendship> Friendships { get; set; } = [];
}

public sealed class StoredCookie
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public string Path { get; set; } = "/";
    public string Domain { get; set; } = "";
    public DateTimeOffset? Expires { get; set; }
}

/// <summary>
/// A copy of VRChat's registry (its PlayerPrefs) from the account's PC: the original's
/// VRCX_VRChatRegistryBackups, one row per backup.
/// </summary>
public sealed class VrchatRegistryBackup
{
    public const string AutoName = "Auto Backup";

    public long Id { get; set; }
    public required string AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>{"&lt;key&gt;": {"data": .., "type": 3 | 4 | 100}}, as the agent read it.</summary>
    public required System.Text.Json.JsonDocument Data { get; set; }
    /// <summary>The user was already offered this backup once VRChat's registry was gone (VRCX_VRChatRegistryLastRestoreCheck).</summary>
    public bool RestoreOffered { get; set; }
}

/// <summary>How a saved password is encrypted.</summary>
public static class CredentialProtection
{
    /// <summary>With the server's Data Protection keys: the server can log in again on its own.</summary>
    public const string DataProtection = "data_protection";
    /// <summary>AES-GCM with a key derived from the primary password: only usable when the user types it.</summary>
    public const string PrimaryPassword = "primary_password";
}

/// <summary>
/// The original's saved credentials: a VRChat password kept so the account can log in again when
/// its session expires. Never stored or returned in clear.
/// </summary>
public sealed class SavedCredential
{
    public required string AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public required string Username { get; set; }
    /// <summary>See <see cref="CredentialProtection"/>.</summary>
    public required string Protection { get; set; }
    /// <summary>The encrypted password: a Data Protection payload, or AES-GCM ciphertext followed by its tag.</summary>
    public required byte[] Secret { get; set; }
    /// <summary>PBKDF2 salt, nonce and iteration count for the primary password scheme.</summary>
    public byte[]? Salt { get; set; }
    public byte[]? Nonce { get; set; }
    public int? Iterations { get; set; }
    public DateTimeOffset SavedAt { get; set; }
}

/// <summary>
/// A friendship between an account and a user, over time. Unfriending ends
/// the row (EndedAt) rather than deleting it, and re-friending opens a new
/// one - so "friend log" is this table ordered by StartedAt/EndedAt.
/// </summary>
public sealed class Friendship
{
    public long Id { get; set; }
    public required string OwnerUserId { get; set; }
    public Account Owner { get; set; } = null!;
    public required string FriendUserId { get; set; }
    public User Friend { get; set; } = null!;
    /// <summary>The account's running "Nth friend" counter, assigned once when the friendship is first recorded.</summary>
    public int FriendNumber { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    /// <summary>Last friend-list fetch that still listed them.</summary>
    public DateTimeOffset LastConfirmedAt { get; set; }
    /// <summary>The friend's name when the original VRCX logged the friendship (imported history). Null for rows this server recorded.</summary>
    public string? DisplayName { get; set; }
    /// <summary>The friend's name when the original VRCX logged the unfriend (imported history).</summary>
    public string? EndedDisplayName { get; set; }
}

public sealed class UserDisplayNameChange
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTimeOffset ChangedAt { get; set; }
    public required string PreviousDisplayName { get; set; }
    public required string DisplayName { get; set; }
}

public sealed class UserTrustChange
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTimeOffset ChangedAt { get; set; }
    public required string PreviousTrustLevel { get; set; }
    public required string TrustLevel { get; set; }
    /// <summary>The name the original VRCX stored with the row, for history imported from it. Null for rows this server recorded.</summary>
    public string? DisplayName { get; set; }
}

public sealed class UserStatusChange
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTimeOffset ChangedAt { get; set; }
    public required string Status { get; set; }
    public string? StatusDescription { get; set; }
    public required string PreviousStatus { get; set; }
    public string? PreviousStatusDescription { get; set; }
    /// <summary>The name the original VRCX stored with the row, for history imported from it. Null for rows this server recorded.</summary>
    public string? DisplayName { get; set; }
}

public sealed class UserBioChange
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTimeOffset ChangedAt { get; set; }
    public string? Bio { get; set; }
    public string[] BioLinks { get; set; } = [];
    public string? PreviousBio { get; set; }
    public string[] PreviousBioLinks { get; set; } = [];
    /// <summary>The name the original VRCX stored with the row, for history imported from it. Null for rows this server recorded.</summary>
    public string? DisplayName { get; set; }
}

public sealed class UserAvatarChange
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTimeOffset ChangedAt { get; set; }
    public string? ImageUrl { get; set; }
    public string? ThumbnailImageUrl { get; set; }
    public string? PreviousImageUrl { get; set; }
    public string? PreviousThumbnailImageUrl { get; set; }
    /// <summary>From GET file/{id} of the image: the avatar's name and who published it.</summary>
    public string? AvatarName { get; set; }
    public string? AvatarOwnerId { get; set; }
    public string? PreviousAvatarName { get; set; }
    public string? PreviousAvatarOwnerId { get; set; }
    /// <summary>Resolved when the image belongs to an avatar we know (its author published it, or it was fetched).</summary>
    public string? AvatarId { get; set; }
    public Avatar? Avatar { get; set; }
    /// <summary>The name the original VRCX stored with the row, for history imported from it. Null for rows this server recorded.</summary>
    public string? DisplayName { get; set; }
}

/// <summary>Every presence transition the pipeline (or a fetch) reported: came online, went offline, moved instance.</summary>
public sealed class PresenceEvent
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTimeOffset At { get; set; }
    public required PresenceKind Kind { get; set; }
    public string? Location { get; set; }
    public string? PreviousLocation { get; set; }
    public string? TravelingToLocation { get; set; }
    public string? InstanceLocation { get; set; }
    public Instance? Instance { get; set; }
    public string? Platform { get; set; }
    /// <summary>The state before this event, so online/offline transitions can be told apart from moves.</summary>
    public UserState? PreviousState { get; set; }
    /// <summary>How long they had been at PreviousLocation (GPS) or at Location before going offline.</summary>
    public TimeSpan? DurationAtPrevious { get; set; }
    /// <summary>The user's, world's and group's names as the original VRCX stored them with the row (imported history). Null for rows this server recorded.</summary>
    public string? DisplayName { get; set; }
    public string? WorldName { get; set; }
    public string? GroupName { get; set; }
}

/// <summary>A user being in an instance for a span of time. The connecting table between users and instances.</summary>
public sealed class InstanceVisit
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public User User { get; set; } = null!;
    public required string InstanceLocation { get; set; }
    public Instance Instance { get; set; } = null!;
    public DateTimeOffset JoinedAt { get; set; }
    /// <summary>Null while they are (as far as we know) still there.</summary>
    public DateTimeOffset? LeftAt { get; set; }
    public required VisitSource Source { get; set; }
    /// <summary>The player's, world's and group's names as the original VRCX stored them (imported history). Null for rows this server recorded.</summary>
    public string? DisplayName { get; set; }
    public string? WorldName { get; set; }
    public string? GroupName { get; set; }
}

/// <summary>GET userNotes - the account's private note on another user, as VRChat stores it.</summary>
public sealed class UserNote
{
    public required string Id { get; set; }
    public required string OwnerUserId { get; set; }
    public Account Owner { get; set; } = null!;
    public required string TargetUserId { get; set; }
    public User Target { get; set; } = null!;
    public string Note { get; set; } = "";
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>GET auth/user/playermoderations - one block/mute/hide the account has applied.</summary>
public sealed class PlayerModeration
{
    public required string Id { get; set; }
    public required string OwnerUserId { get; set; }
    public Account Owner { get; set; } = null!;
    public required string TargetUserId { get; set; }
    public User Target { get; set; } = null!;
    public required string Type { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>A friend request the account sent or cancelled through VRCX; VRChat keeps no record of these.</summary>
public sealed class FriendRequestEvent
{
    public long Id { get; set; }
    public required string OwnerUserId { get; set; }
    public Account Owner { get; set; } = null!;
    /// <summary>No foreign key: a request can go to someone the server has never stored.</summary>
    public required string TargetUserId { get; set; }
    public required string DisplayName { get; set; }
    /// <summary>FriendRequest or CancelFriendRequest.</summary>
    public required string Type { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A row removed from the account's friend log view. The underlying history is kept.</summary>
public sealed class FriendLogHiddenEntry
{
    public required string OwnerUserId { get; set; }
    public Account Owner { get; set; } = null!;
    /// <summary>The row's key, such as Unfriend:12.</summary>
    public required string EntryKey { get; set; }
    public DateTimeOffset HiddenAt { get; set; }
}

/// <summary>VRCX's own local memo on a user (not a VRChat note).</summary>
public sealed class UserMemo
{
    public required string UserId { get; set; }
    public User User { get; set; } = null!;
    public required string Memo { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A tag another program on the account's PC gave a user over the VRCX named pipe (the original's
/// customUserTags). The user need not be known yet, so there is no key to users.
/// </summary>
public sealed class CustomUserTag
{
    public required string OwnerUserId { get; set; }
    public Account Owner { get; set; } = null!;
    public required string UserId { get; set; }
    public required string Tag { get; set; }
    /// <summary>A CSS colour, as the program sent it.</summary>
    public string? Colour { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A game log stay of a player the log named only by display name, and whom no user id could be
/// matched to (older VRChat logs had no ids). Kept so the stay is not lost; instance_visits needs a user.
/// </summary>
public sealed class UnidentifiedVisit
{
    public long Id { get; set; }
    public required string DisplayName { get; set; }
    public required string InstanceLocation { get; set; }
    public Instance Instance { get; set; } = null!;
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
}

/// <summary>
/// Someone in the account's instance who blocked or muted the account, as Photon reported it
/// (the original's per-account moderation table). The other direction is player_moderations.
/// </summary>
public sealed class ModerationAgainst
{
    public required string OwnerUserId { get; set; }
    public Account Owner { get; set; } = null!;
    public required string UserId { get; set; }
    public User User { get; set; } = null!;
    /// <summary>Their name when it was recorded.</summary>
    public string? DisplayName { get; set; }
    public bool Block { get; set; }
    public bool Mute { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
