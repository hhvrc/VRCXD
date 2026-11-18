using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Notifications;

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

/// <summary>
/// The account's notifications as VRChat keeps them and the full sync mirrors them. Read only;
/// shared by the notifications endpoint and the MCP tools.
/// </summary>
public sealed class NotificationQueries(VrcxContext db)
{
    /// <summary>The account's notifications (both feeds), newest first, for the caller to page.</summary>
    public IQueryable<NotificationResponse> Notifications(string accountId, bool includeHidden) =>
        db.Notifications.AsNoTracking()
            .Where(n => n.OwnerUserId == accountId && !n.LogHidden && (includeHidden || !n.Hidden))
            // A hidden friend request or group notice is finished with, and the original drops it from the page.
            .Where(n => !n.Hidden || (n.Type != "friendRequest" && n.Type != "ignoredFriendRequest" && !n.Type.Contains('.')))
            .OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id)
            .Select(n => new NotificationResponse
            {
                Id = n.Id, Version = n.Version, Type = n.Type, SenderUserId = n.SenderUserId,
                SenderDisplayName = n.Sender != null ? n.Sender.DisplayName : n.SenderUsername,
                Title = n.Title, Message = n.Message, ImageUrl = n.ImageUrl, Link = n.Link, LinkText = n.LinkText, Details = n.Details, Responses = n.Responses,
                Seen = n.Seen, Hidden = n.Hidden, CreatedAt = n.CreatedAt, ExpiresAt = n.ExpiresAt,
            });
}
