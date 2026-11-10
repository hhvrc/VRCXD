using System.Text.Json;

namespace VRCX.Common.VrcxDb;

/// <summary>
/// Both notification feeds in one table: Version 1 (invites, friend
/// requests, votes to kick) and Version 2 (group invites/requests,
/// announcements, boops). The type-specific payload is Details.
/// </summary>
public sealed class Notification
{
    public required string Id { get; set; }
    public required string OwnerUserId { get; set; }
    public Account Owner { get; set; } = null!;
    public int Version { get; set; }
    public required string Type { get; set; }
    public string? SenderUserId { get; set; }
    public User? Sender { get; set; }
    public string? SenderUsername { get; set; }
    public string? ReceiverUserId { get; set; }
    public string? Title { get; set; }
    public string Message { get; set; } = "";
    public string? ImageUrl { get; set; }
    public string? Link { get; set; }
    public string? LinkText { get; set; }
    /// <summary>v1 details or v2 data, as VRChat sent it.</summary>
    public JsonDocument? Details { get; set; }
    /// <summary>v2 response options.</summary>
    public JsonDocument? Responses { get; set; }
    /// <summary>v2 only: the separate details object (boop emoji, gift) next to data.</summary>
    public JsonDocument? V2Details { get; set; }
    public bool Seen { get; set; }
    /// <summary>Hidden/expired/deleted on VRChat's side - kept for history.</summary>
    public bool Hidden { get; set; }
    /// <summary>Removed from the Notifications page by the user. The row is kept.</summary>
    public bool LogHidden { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>A VRChat-side favorite (friend/world/avatar) of an account.</summary>
public sealed class Favorite
{
    public required string Id { get; set; }
    public required string OwnerUserId { get; set; }
    public Account Owner { get; set; } = null!;
    public required FavoriteType Type { get; set; }
    /// <summary>usr_/wrld_/avtr_ - which table it points into depends on Type.</summary>
    public required string TargetId { get; set; }
    /// <summary>The favorite group's internal name ("group_0") - FavoriteGroup.Name.</summary>
    public string? GroupName { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>A print the account wants to keep. VRChat has no such flag, so only this app knows it.</summary>
public sealed class PrintFavorite
{
    public required string OwnerUserId { get; set; }
    public Account Owner { get; set; } = null!;
    public required string PrintId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A favorite in a group only VRCX knows of: the original's local favorites. VRChat never sees these.</summary>
public sealed class LocalFavorite
{
    public required string OwnerUserId { get; set; }
    public Account Owner { get; set; } = null!;
    public required FavoriteType Type { get; set; }
    public required string GroupName { get; set; }
    public required string TargetId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A server-wide value, such as the install's VRCX-ID that the original keeps as VRCX_id.</summary>
public sealed class ServerSetting
{
    public required string Key { get; set; }
    public required string Value { get; set; }
}

/// <summary>
/// A setting from the original VRCX's configs table, kept verbatim by the importer. Most are
/// desktop UI preferences this server has no use for; the ones it does use are also mapped
/// into accounts.settings and server_settings.
/// </summary>
public sealed class LegacySetting
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
}

/// <summary>A local favorite group the user made. Its favorites name it; this row lets it exist while empty.</summary>
public sealed class LocalFavoriteGroup
{
    public required string OwnerUserId { get; set; }
    public Account Owner { get; set; } = null!;
    public required FavoriteType Type { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FavoriteGroup
{
    public required string Id { get; set; }
    public required string OwnerUserId { get; set; }
    public Account Owner { get; set; } = null!;
    public required string Type { get; set; }
    public required string Name { get; set; }
    public string DisplayName { get; set; } = "";
    public string Visibility { get; set; } = "private";
    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>Game log: a video URL played in an instance (the log line names the player who queued it).</summary>
public sealed class VideoPlay
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string? InstanceLocation { get; set; }
    public Instance? Instance { get; set; }
    public string? UserId { get; set; }
    public User? User { get; set; }
    public string? DisplayName { get; set; }
    public required string VideoUrl { get; set; }
    public string? VideoName { get; set; }
    public string? VideoId { get; set; }
    /// <summary>Where playback was when it was logged, in seconds (the original's videoPos; 8 for a plain video URL, its loading delay).</summary>
    public double? VideoPos { get; set; }
    /// <summary>The video's length in seconds, from the world's own line or the YouTube API (the original's videoLength); null when unknown.</summary>
    public double? VideoLength { get; set; }
}

/// <summary>Game log: a line with no table of its own. Kind says which, the original's Event or External.</summary>
public sealed class GameLogEvent
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string? InstanceLocation { get; set; }
    public Instance? Instance { get; set; }
    public required string Kind { get; set; }
    public required string Data { get; set; }
    public string? UserId { get; set; }
    public string? DisplayName { get; set; }
}

/// <summary>A row removed from the account's game log view. The row itself is kept.</summary>
public sealed class GameLogHiddenEntry
{
    public required string OwnerUserId { get; set; }
    public Account Owner { get; set; } = null!;
    /// <summary>The row's key, such as VideoPlay:12.</summary>
    public required string EntryKey { get; set; }
    public DateTimeOffset HiddenAt { get; set; }
}

/// <summary>Game log: a sticker spawned in the instance.</summary>
public sealed class StickerSpawn
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string? InstanceLocation { get; set; }
    public Instance? Instance { get; set; }
    public string? UserId { get; set; }
    public User? User { get; set; }
    public string? DisplayName { get; set; }
    public required string InventoryId { get; set; }
}

/// <summary>Game log: a world loaded a string or image from a URL (Udon).</summary>
public sealed class ResourceLoad
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string? InstanceLocation { get; set; }
    public Instance? Instance { get; set; }
    public required string Url { get; set; }
    /// <summary>string or image.</summary>
    public required string Kind { get; set; }
}

/// <summary>
/// One game-log-shipping client (one gaming PC): which account plays there, and how far its log has been ingested - what a reconnecting client resumes from.
/// </summary>
public sealed class GameLogClient
{
    public required string ClientId { get; set; }
    public string? ClientName { get; set; }
    public string? AccountId { get; set; }
    public User? Account { get; set; }
    /// <summary>Timestamp of the newest log line stored; the client sends only lines after it.</summary>
    public DateTimeOffset? LastEntryAt { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    /// <summary>When it last connected. Its heartbeat while connected, and its current instance, are in Redis.</summary>
    public DateTimeOffset LastSeenAt { get; set; }
    /// <summary>When VRChat started on this PC, while it runs (the original's currentUser.$online_for); null when stopped.</summary>
    public DateTimeOffset? GameStartedAt { get; set; }
    /// <summary>The last finished game session (the original's VRCX_lastGameSessionMs and VRCX_lastGameOfflineAt).</summary>
    public DateTimeOffset? LastGameStartedAt { get; set; }
    public DateTimeOffset? LastGameStoppedAt { get; set; }
    /// <summary>VRChat on this PC last started in desktop mode (its log's desktop-mode line) rather than with OpenVR (the original's isGameNoVR).</summary>
    public bool GameNoVr { get; set; }
}

/// <summary>Game log: someone dropped a portal.</summary>
public sealed class PortalSpawn
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string? InstanceLocation { get; set; }
    public Instance? Instance { get; set; }
    public string? UserId { get; set; }
    public User? User { get; set; }
    public string? DisplayName { get; set; }
    public string? TargetInstanceLocation { get; set; }
    public string? TargetWorldName { get; set; }
}

/// <summary>
/// One edge of the mutual-friend network: the account's friend shares a friend with the
/// account. Filled by GET users/{friend}/mutuals/friends, replaced per friend on each fetch.
/// </summary>
public sealed class MutualFriendLink
{
    public long Id { get; set; }
    public required string OwnerUserId { get; set; }
    public Account Owner { get; set; } = null!;
    public required string FriendUserId { get; set; }
    public User Friend { get; set; } = null!;
    public required string MutualUserId { get; set; }
    public User Mutual { get; set; } = null!;
    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>When a friend's mutuals were last fetched, and whether they have sharing turned off.</summary>
public sealed class MutualGraphMeta
{
    public required string OwnerUserId { get; set; }
    public Account Owner { get; set; } = null!;
    public required string FriendUserId { get; set; }
    public User Friend { get; set; } = null!;
    public DateTimeOffset LastFetchedAt { get; set; }
    public bool OptedOut { get; set; }
}
