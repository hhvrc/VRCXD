using System.Text.Json.Nodes;
using VRCX.API.Services;
using VRCX.Testing.FakeVrchat;
using VRCX.Vrchat.Pipeline;

namespace VRCX.Testing.Pipeline;

/// <summary>
/// VRChat's pipeline websocket, from the server's side. Each call builds the frame VRChat would
/// send ({"type":...,"content":"&lt;json as a string&gt;"}, double-encoded as on the wire), decodes it
/// with the production <see cref="PipelineEvent.Parse"/> and hands it to
/// <see cref="PipelineService.DispatchAsync"/>, the code the real connection loop runs for every
/// frame. Returns once the frame is stored and its live events are published, and throws when
/// handling it logged an error (the real loop only logs those).
/// </summary>
public sealed class FakePipeline(VrcxScenario scenario)
{
    /// <summary>The account frames go to; defaults to the scenario's logged-in account.</summary>
    public string? AccountId { get; set; }

    private string Account => AccountId ?? scenario.AccountId ?? throw new InvalidOperationException("No account is logged in; call LoginAsync first or set Pipeline.AccountId.");

    /// <summary>Sends a raw frame exactly as it would arrive from wss://pipeline.vrchat.cloud.</summary>
    public async Task SendRawAsync(string frame)
    {
        var decoded = PipelineEvent.Parse(frame);
        var before = scenario.Logs.Errors.Count;
        await scenario.PipelineService.DispatchAsync(Account, decoded, CancellationToken.None);
        var errors = scenario.Logs.Errors.Skip(before).ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException($"The server logged an error handling pipeline {decoded.Type}:\n{string.Join("\n", errors)}");
    }

    /// <summary>A frame of the given type whose content is the JSON object, double-encoded as VRChat does.</summary>
    public Task SendAsync(string type, JsonNode content) =>
        SendRawAsync(new JsonObject { ["type"] = type, ["content"] = content.ToJsonString() }.ToJsonString());

    /// <summary>A frame whose content is a bare string (see-notification and friends).</summary>
    public Task SendStringAsync(string type, string content) =>
        SendRawAsync(new JsonObject { ["type"] = type, ["content"] = content }.ToJsonString());

    // ---- friends --------------------------------------------------------

    private static JsonObject FriendContent(FakeUser user, string? location = null, string? travelingTo = null, FakeWorld? world = null, string? platform = null)
    {
        var content = new JsonObject
        {
            ["userId"] = user.Id,
            ["user"] = user.Json.DeepClone(),
            ["platform"] = platform ?? (string?)user.Json["last_platform"] ?? "standalonewindows",
        };
        if (location is not null)
        {
            content["location"] = location;
            var parsed = Vrchat.VrchatLocation.Parse(location);
            content["worldId"] = parsed.WorldId;
            content["canRequestInvite"] = parsed.CanRequestInvite;
        }
        if (travelingTo is not null) content["travelingToLocation"] = travelingTo;
        if (world is not null) content["world"] = world.Json.DeepClone();
        return content;
    }

    /// <summary>friend-online. Also updates the fixture, so a later sync agrees.</summary>
    public Task FriendOnlineAsync(FakeUser friend, string location, FakeWorld? world = null)
    {
        friend.OnlineAt(location);
        return SendAsync("friend-online", FriendContent(friend, location, world: world));
    }

    /// <summary>friend-location; pass "traveling" with travelingTo for the in-between frame.</summary>
    public Task FriendLocationAsync(FakeUser friend, string location, FakeWorld? world = null, string? travelingTo = null)
    {
        friend.OnlineAt(location);
        if (travelingTo is not null) friend.Set("travelingToLocation", travelingTo);
        return SendAsync("friend-location", FriendContent(friend, location, travelingTo, world));
    }

    /// <summary>friend-offline: VRChat sends only the id and platform.</summary>
    public Task FriendOfflineAsync(FakeUser friend)
    {
        friend.Offline();
        return SendAsync("friend-offline", new JsonObject { ["userId"] = friend.Id, ["platform"] = "" });
    }

    /// <summary>friend-active: on the website or app, not in the game.</summary>
    public Task FriendActiveAsync(FakeUser friend)
    {
        friend.Active();
        return SendAsync("friend-active", new JsonObject { ["userId"] = friend.Id, ["user"] = friend.Json.DeepClone(), ["platform"] = "web" });
    }

    /// <summary>friend-update: profile fields changed (name, status, bio, avatar...).</summary>
    public Task FriendUpdateAsync(FakeUser friend) =>
        SendAsync("friend-update", new JsonObject { ["userId"] = friend.Id, ["user"] = friend.Json.DeepClone() });

    /// <summary>friend-add. Also adds the friendship to the fake API.</summary>
    public Task FriendAddAsync(FakeUser friend)
    {
        scenario.Vrchat.Friend(friend);
        return SendAsync("friend-add", new JsonObject { ["userId"] = friend.Id, ["user"] = friend.Json.DeepClone() });
    }

    /// <summary>friend-delete. Also ends the friendship in the fake API.</summary>
    public Task FriendDeleteAsync(FakeUser friend)
    {
        scenario.Vrchat.Unfriend(friend.Id);
        return SendAsync("friend-delete", new JsonObject { ["userId"] = friend.Id });
    }

    // ---- the account itself ----------------------------------------------

    /// <summary>user-update with the current <see cref="FakeVrchatApi.Me"/>.</summary>
    public Task UserUpdateAsync() =>
        SendAsync("user-update", new JsonObject { ["userId"] = scenario.Vrchat.Me!.Id, ["user"] = scenario.Vrchat.Me.Json.DeepClone() });

    public Task UserLocationAsync(string location, string? travelingTo = null)
    {
        var content = new JsonObject { ["userId"] = scenario.Vrchat.Me!.Id, ["location"] = location, ["instance"] = location };
        if (travelingTo is not null) content["travelingToLocation"] = travelingTo;
        return SendAsync("user-location", content);
    }

    // ---- notifications ----------------------------------------------------

    /// <summary>"notification" with a version 1 notification (invite, requestInvite, friendRequest, ...).</summary>
    public Task NotificationAsync(FakeNotification notification) => SendAsync("notification", notification.Json.DeepClone());

    public Task NotificationV2Async(JsonObject notification) => SendAsync("notification-v2", notification.DeepClone());

    public Task SeeNotificationAsync(string id) => SendStringAsync("see-notification", id);
    public Task HideNotificationAsync(string id) => SendStringAsync("hide-notification", id);
    public Task ClearNotificationAsync(string id) => SendStringAsync("clear-notification", id);

    public Task NotificationV2UpdateAsync(string id, JsonObject updates) =>
        SendAsync("notification-v2-update", new JsonObject { ["id"] = id, ["updates"] = updates.DeepClone() });

    public Task NotificationV2DeleteAsync(params string[] ids) =>
        SendAsync("notification-v2-delete", new JsonObject { ["ids"] = new JsonArray(ids.Select(i => (JsonNode?)i).ToArray()), ["version"] = 2 });

    // ---- groups and instances ----------------------------------------------

    public Task GroupJoinedAsync(string groupId) => SendAsync("group-joined", new JsonObject { ["groupId"] = groupId });
    public Task GroupLeftAsync(string groupId) => SendAsync("group-left", new JsonObject { ["groupId"] = groupId });

    public Task InstanceQueueReadyAsync(string location) =>
        SendAsync("instance-queue-ready", new JsonObject { ["instanceLocation"] = location, ["expiry"] = DateTimeOffset.UtcNow.AddMinutes(2).ToString("O") });

    public Task InstanceClosedAsync(string location) =>
        SendAsync("instance-closed", new JsonObject { ["instanceLocation"] = location, ["closedAt"] = DateTimeOffset.UtcNow.ToString("O") });
}
