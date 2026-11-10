using System.Text.Json;
using System.Text.Json.Serialization;
using VRCX.Vrchat.Models;

namespace VRCX.Vrchat.Pipeline;

/// <summary>
/// One frame from the pipeline websocket, decoded. The wire shape is
/// {"type":"friend-location","content":"{...}"} - content is usually a JSON
/// string containing JSON (double-encoded), occasionally a real object, and
/// for the see/hide/clear-notification types a bare id string.
/// </summary>
public abstract record PipelineEvent
{
    public required string Type { get; init; }
    /// <summary>The decoded content, for logging and for types this library doesn't model.</summary>
    public JsonElement Content { get; init; }

    public static PipelineEvent Parse(string frame)
    {
        using var document = JsonDocument.Parse(frame);
        var root = document.RootElement;
        // VRChat answers a bad or expired auth token with {"err":"..."} and closes.
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("err", out var err))
            return new PipelineErrorEvent { Type = "error", Message = err.ValueKind == JsonValueKind.String ? err.GetString() ?? "" : err.GetRawText() };
        var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? "" : "";
        if (!root.TryGetProperty("content", out var rawContent))
            return new UnknownEvent { Type = type };

        // Double-encoded: content is a string that is itself JSON. A bare
        // id string ("not_...") isn't JSON, so it stays a string.
        JsonElement content;
        if (rawContent.ValueKind == JsonValueKind.String)
        {
            var text = rawContent.GetString() ?? "";
            if (text.Length > 0 && text[0] is '{' or '[')
            {
                using var inner = JsonDocument.Parse(text);
                content = inner.RootElement.Clone();
            }
            else
            {
                content = rawContent.Clone();
            }
        }
        else
        {
            content = rawContent.Clone();
        }

        return Build(type, content);
    }

    private static PipelineEvent Build(string type, JsonElement content)
    {
        try
        {
            return type switch
            {
                "friend-online" or "friend-active" or "friend-offline" or "friend-location" or "friend-update"
                    or "friend-add" or "friend-delete" or "friend-request" =>
                    Fill(new FriendEvent { Type = type, Content = content, Payload = Read<FriendPayload>(content) }),
                "user-update" or "user-location" or "user-badge-assigned" or "user-badge-unassigned" =>
                    Fill(new CurrentUserEvent { Type = type, Content = content, Payload = Read<CurrentUserPayload>(content) }),
                "notification" =>
                    Fill(new NotificationEvent { Type = type, Content = content, Notification = Read<Notification>(content) }),
                "notification-v2" =>
                    Fill(new NotificationV2Event { Type = type, Content = content, Notification = Read<NotificationV2>(content) }),
                "notification-v2-update" =>
                    Fill(new NotificationV2UpdateEvent { Type = type, Content = content, Payload = Read<NotificationV2UpdatePayload>(content) }),
                "notification-v2-delete" =>
                    Fill(new NotificationV2DeleteEvent { Type = type, Content = content, Payload = Read<NotificationV2DeletePayload>(content) }),
                "see-notification" or "hide-notification" or "clear-notification" =>
                    Fill(new NotificationIdEvent { Type = type, Content = content, NotificationId = content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" : "" }),
                "response-notification" =>
                    Fill(new NotificationResponseEvent { Type = type, Content = content, Payload = Read<NotificationResponsePayload>(content) }),
                "group-joined" or "group-left" or "group-member-updated" or "group-role-updated" =>
                    Fill(new GroupEvent { Type = type, Content = content, Payload = Read<GroupPayload>(content) }),
                "instance-queue-joined" or "instance-queue-position" or "instance-queue-ready" or "instance-queue-left" or "instance-closed" =>
                    Fill(new InstanceEvent { Type = type, Content = content, Payload = Read<InstancePayload>(content) }),
                "economy-update" =>
                    Fill(new EconomyUpdateEvent { Type = type, Content = content, Payload = Read<EconomyUpdatePayload>(content) }),
                "content-refresh" =>
                    Fill(new ContentRefreshEvent { Type = type, Content = content, Payload = Read<ContentRefreshPayload>(content) }),
                _ => new UnknownEvent { Type = type, Content = content },
            };
        }
        catch (JsonException)
        {
            // A payload we mis-modelled must not take the connection down;
            // the consumer logs these and moves on.
            return new UnknownEvent { Type = type, Content = content };
        }
    }

    private static T Read<T>(JsonElement content) =>
        content.Deserialize<T>(VrchatJson.Options) ?? throw new JsonException($"{typeof(T).Name} content was null");

    private static T Fill<T>(T e) where T : PipelineEvent => e;
}

/// <summary>friend-online/active/offline/location/update/add/delete. Which fields are set depends on the type.</summary>
public sealed record FriendEvent : PipelineEvent
{
    public required FriendPayload Payload { get; init; }
    public string UserId => Payload.UserId;
}

public sealed record FriendPayload
{
    public required string UserId { get; init; }
    public string? Location { get; init; }
    public string? TravelingToLocation { get; init; }
    public string? WorldId { get; init; }
    public string? Platform { get; init; }
    public bool? CanRequestInvite { get; init; }
    /// <summary>The user as of this event. Sent on online/active/location/update/add; absent on offline/delete.</summary>
    public User? User { get; init; }
    /// <summary>friend-location may also carry the world.</summary>
    public World? World { get; init; }
}

/// <summary>user-update / user-location / user-badge-* - about the logged-in account itself.</summary>
public sealed record CurrentUserEvent : PipelineEvent
{
    public required CurrentUserPayload Payload { get; init; }
}

public sealed record CurrentUserPayload
{
    public string? UserId { get; init; }
    public string? Location { get; init; }
    public string? Instance { get; init; }
    public string? TravelingToLocation { get; init; }
    public string? WorldId { get; init; }
    public CurrentUser? User { get; init; }
    public Badge? Badge { get; init; }
}

public sealed record NotificationEvent : PipelineEvent
{
    public required Notification Notification { get; init; }
}

public sealed record NotificationV2Event : PipelineEvent
{
    public required NotificationV2 Notification { get; init; }
}

public sealed record NotificationV2UpdateEvent : PipelineEvent
{
    public required NotificationV2UpdatePayload Payload { get; init; }
}

public sealed record NotificationV2UpdatePayload
{
    public required string Id { get; init; }
    public string? Version { get; init; }
    /// <summary>A partial NotificationV2: only the changed fields.</summary>
    public JsonElement? Updates { get; init; }
}

public sealed record NotificationV2DeleteEvent : PipelineEvent
{
    public required NotificationV2DeletePayload Payload { get; init; }
}

public sealed record NotificationV2DeletePayload
{
    public IReadOnlyList<string> Ids { get; init; } = [];
    public string? Version { get; init; }
}

/// <summary>see-notification / hide-notification / clear-notification - content is just the id.</summary>
public sealed record NotificationIdEvent : PipelineEvent
{
    public required string NotificationId { get; init; }
}

public sealed record NotificationResponseEvent : PipelineEvent
{
    public required NotificationResponsePayload Payload { get; init; }
}

public sealed record NotificationResponsePayload
{
    public string? NotificationId { get; init; }
    public string? ResponseId { get; init; }
}

public sealed record GroupEvent : PipelineEvent
{
    public required GroupPayload Payload { get; init; }
}

public sealed record GroupPayload
{
    public string? GroupId { get; init; }
    public GroupMember? Member { get; init; }
    public GroupRole? Role { get; init; }
}

public sealed record InstanceEvent : PipelineEvent
{
    public required InstancePayload Payload { get; init; }
}

public sealed record InstancePayload
{
    public string? InstanceLocation { get; init; }
    public int? Position { get; init; }
    public int? QueueSize { get; init; }
    public DateTimeOffset? Expiry { get; init; }
}

public sealed record ContentRefreshEvent : PipelineEvent
{
    public required ContentRefreshPayload Payload { get; init; }
}

public sealed record ContentRefreshPayload
{
    public string? ContentType { get; init; }
    public string? ActionType { get; init; }
}

/// <summary>economy-update: the account's VRChat credits changed (the original's setCurrentUserCredits).</summary>
public sealed record EconomyUpdateEvent : PipelineEvent
{
    public required EconomyUpdatePayload Payload { get; init; }
}

public sealed record EconomyUpdatePayload
{
    public long? WalletBalance { get; init; }
}

public sealed record UnknownEvent : PipelineEvent;

/// <summary>An {"err":"..."} frame, such as "authToken doesn't correspond with an active session".</summary>
public sealed record PipelineErrorEvent : PipelineEvent
{
    public required string Message { get; init; }
    public bool IsAuthError => Message.Contains("authToken", StringComparison.OrdinalIgnoreCase) || Message.Contains("session", StringComparison.OrdinalIgnoreCase);
}
