using System.Text.Json;
using System.Text.Json.Serialization;

namespace VRCX.Vrchat.Models;

/// <summary>GET auth/user/notifications - the legacy ("v1") notification feed: invites, friend requests, votes to kick.</summary>
public sealed record Notification
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public string? SenderUserId { get; init; }
    public string? SenderUsername { get; init; }
    public string? ReceiverUserId { get; init; }
    public string Message { get; init; } = "";
    /// <summary>Type-dependent object. VRChat sometimes double-encodes this as a JSON string.</summary>
    public JsonElement? Details { get; init; }
    public bool Seen { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>GET notifications (v2) - group invites/requests, announcements, boops, event reminders.</summary>
public sealed record NotificationV2
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public string Version { get; init; } = "2";
    public string? Category { get; init; }
    public bool IsSystem { get; init; }
    public bool IgnoreDnd { get; init; }
    public string? SenderUserId { get; init; }
    public string? SenderUsername { get; init; }
    public string? ReceiverUserId { get; init; }
    public string? RelatedNotificationsId { get; init; }
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string? ImageUrl { get; init; }
    public string? Link { get; init; }
    public string? LinkText { get; init; }
    public JsonElement? Data { get; init; }
    public IReadOnlyList<NotificationV2Response>? Responses { get; init; }
    public bool Seen { get; init; }
    public bool CanDelete { get; init; }
    public bool RequireSeen { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool ExpiryAfterSeen { get; init; }
}

public sealed record NotificationV2Response
{
    public string Type { get; init; } = "";
    public string Data { get; init; } = "";
    public string Text { get; init; } = "";
    public string? Icon { get; init; }
    public string? TextKey { get; init; }
}

/// <summary>GET favorites - one favorited friend/world/avatar.</summary>
public sealed record Favorite
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    /// <summary>The favorited thing's id (usr_/wrld_/avtr_).</summary>
    public required string FavoriteId { get; init; }
    /// <summary>The favorite group it's in, as tags (e.g. ["group_0"]).</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>GET favorite/groups.</summary>
public sealed record FavoriteGroup
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    /// <summary>Internal name ("group_0"); what favorites' Tags refer to.</summary>
    public required string Name { get; init; }
    public string DisplayName { get; init; } = "";
    public string OwnerId { get; init; } = "";
    public string OwnerDisplayName { get; init; } = "";
    public string Visibility { get; init; } = "private";
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public static class FavoriteTypeValues
{
    public const string Friend = "friend";
    public const string World = "world";
    public const string Avatar = "avatar";
}

/// <summary>GET auth/user/favoritelimits. The defaults are the original's, for when VRChat leaves one out.</summary>
public sealed record FavoriteLimits
{
    public FavoriteCounts MaxFavoriteGroups { get; init; } = new() { Avatar = 6, Friend = 3, VrcPlusWorld = 4, World = 4 };
    public FavoriteCounts MaxFavoritesPerGroup { get; init; } = new() { Avatar = 50, Friend = 150, VrcPlusWorld = 100, World = 100 };
}

public sealed record FavoriteCounts
{
    public int Avatar { get; init; }
    public int Friend { get; init; }
    public int VrcPlusWorld { get; init; }
    public int World { get; init; }
}

/// <summary>GET file/{fileId} - what VRCX uses to turn an image URL into the avatar it belongs to.</summary>
public sealed record VrchatFile
{
    public required string Id { get; init; }
    /// <summary>"Avatar - Name - Image - 2024-01-01..." for avatar images.</summary>
    public string Name { get; init; } = "";
    public string? OwnerId { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<VrchatFileVersion> Versions { get; init; } = [];
    /// <summary>Emoji only: a flipbook sheet's frame count, speed and loop, for animated emoji.</summary>
    public string? AnimationStyle { get; init; }
    public int? Frames { get; init; }
    public int? FramesOverTime { get; init; }
    public string? LoopStyle { get; init; }
}

public sealed record VrchatFileVersion
{
    public int Version { get; init; }
    public string? Status { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>GET auth - the pipeline websocket token.</summary>
public sealed record AuthToken
{
    public bool Ok { get; init; }
    public string? Token { get; init; }
}

/// <summary>What auth/device/code answers: the code to poll with and the address its QR code shows.</summary>
public sealed record DeviceCodeGrant
{
    public required string DeviceCode { get; init; }
    public required string VerificationUrlComplete { get; init; }
    public int Interval { get; init; }
    public int ExpiresIn { get; init; }
}

/// <summary>What auth/twofactorauth/{method}/verify answers.</summary>
public sealed record TwoFactorVerifyResult
{
    public bool Verified { get; init; }
}

/// <summary>VRChat's error body: {"error":{"message":"...","status_code":401}}.</summary>
public sealed record ErrorEnvelope
{
    public ErrorBody? Error { get; init; }

    public sealed record ErrorBody
    {
        public string? Message { get; init; }
        [JsonPropertyName("status_code")] public int? StatusCode { get; init; }
    }
}
