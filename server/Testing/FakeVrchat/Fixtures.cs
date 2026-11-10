using System.Text.Json.Nodes;

namespace VRCX.Testing.FakeVrchat;

/// <summary>
/// A VRChat object as the fake API serves it: a JSON object with realistic defaults and typed
/// setters for the fields tests usually care about. <see cref="Set"/> reaches any other field.
/// The fake serves the current state on every request, so changing a fixture after a sync and
/// syncing again is how a test makes "VRChat" change its mind.
/// </summary>
public abstract class Fixture<TSelf> where TSelf : Fixture<TSelf>
{
    protected Fixture(JsonObject json) => Json = json;

    public JsonObject Json { get; }
    public string Id => (string)Json["id"]!;

    /// <summary>Sets any field. Values are converted with <see cref="JsonValue.Create{T}(T, JsonNodeOptions?)"/> or taken as a node.</summary>
    public TSelf Set(string field, object? value)
    {
        Json[field] = value switch
        {
            null => null,
            JsonNode node => node.DeepClone(),
            string[] strings => new JsonArray(strings.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            _ => JsonValue.Create(value),
        };
        return (TSelf)this;
    }

    public TSelf Without(string field)
    {
        Json.Remove(field);
        return (TSelf)this;
    }

    public override string ToString() => Json.ToJsonString();
}

/// <summary>A user (users/{id}, friends lists, pipeline payloads). <see cref="State"/> decides which friends list it is on.</summary>
public sealed class FakeUser : Fixture<FakeUser>
{
    public FakeUser(string id, string displayName) : base(new JsonObject
    {
        ["id"] = id,
        ["displayName"] = displayName,
        ["username"] = displayName.ToLowerInvariant().Replace(' ', '_'),
        ["bio"] = "",
        ["bioLinks"] = new JsonArray(),
        ["status"] = "active",
        ["statusDescription"] = "",
        ["state"] = "offline",
        ["location"] = "offline",
        ["tags"] = new JsonArray("system_trust_basic"),
        ["developerType"] = "none",
        ["currentAvatarImageUrl"] = $"https://api.vrchat.cloud/api/1/file/file_{id}_avatar/1/file",
        ["currentAvatarThumbnailImageUrl"] = $"https://api.vrchat.cloud/api/1/image/file_{id}_avatar/1/256",
        ["currentAvatarTags"] = new JsonArray(),
        ["last_platform"] = "standalonewindows",
        ["platform"] = "",
        ["isFriend"] = false,
        ["date_joined"] = "2020-01-01",
    })
    {
    }

    public string DisplayName => (string)Json["displayName"]!;
    public string Location => (string?)Json["location"] ?? "offline";

    public FakeUser Name(string displayName) => Set("displayName", displayName);
    public FakeUser Status(string status, string description = "") => Set("status", status).Set("statusDescription", description);
    public FakeUser Bio(string bio, params string[] links) => Set("bio", bio).Set("bioLinks", links);
    public FakeUser Tags(params string[] tags) => Set("tags", tags);
    public FakeUser Trust(string trustTag) => Tags(trustTag);
    public FakeUser Avatar(string imageUrl) => Set("currentAvatarImageUrl", imageUrl).Set("currentAvatarThumbnailImageUrl", imageUrl + "/256");
    public FakeUser Platform(string platform) => Set("last_platform", platform).Set("platform", platform);

    /// <summary>Online in a real instance (or "private"/"traveling").</summary>
    public FakeUser OnlineAt(string location)
    {
        Set("state", "online").Set("location", location);
        var parsed = Vrchat.VrchatLocation.Parse(location);
        if (parsed.WorldId.Length > 0) Set("worldId", parsed.WorldId).Set("instanceId", parsed.InstanceId);
        else Without("worldId").Without("instanceId");
        return this;
    }

    /// <summary>Online on the website or app, not in the game.</summary>
    public FakeUser Active() => Set("state", "active").Set("location", "offline").Set("platform", "web");

    public FakeUser Offline() => Set("state", "offline").Set("location", "offline").Without("worldId").Without("instanceId");

    public string State => (string?)Json["state"] ?? "offline";
}

public sealed class FakeWorld : Fixture<FakeWorld>
{
    public FakeWorld(string id, string name) : base(new JsonObject
    {
        ["id"] = id,
        ["name"] = name,
        ["description"] = "",
        ["authorId"] = "usr_world_author",
        ["authorName"] = "World Author",
        ["capacity"] = 32,
        ["recommendedCapacity"] = 16,
        ["releaseStatus"] = "public",
        ["tags"] = new JsonArray(),
        ["imageUrl"] = $"https://api.vrchat.cloud/api/1/file/file_{id}/1/file",
        ["thumbnailImageUrl"] = $"https://api.vrchat.cloud/api/1/image/file_{id}/1/256",
        ["visits"] = 0,
        ["favorites"] = 0,
        ["occupants"] = 0,
        ["unityPackages"] = new JsonArray(),
        ["created_at"] = "2020-01-01T00:00:00.000Z",
        ["updated_at"] = "2020-01-01T00:00:00.000Z",
    })
    {
    }

    public string Name => (string)Json["name"]!;
    public FakeWorld Author(string userId, string name) => Set("authorId", userId).Set("authorName", name);
    public FakeWorld Private() => Set("releaseStatus", "private");
}

public sealed class FakeAvatar : Fixture<FakeAvatar>
{
    public FakeAvatar(string id, string name) : base(new JsonObject
    {
        ["id"] = id,
        ["name"] = name,
        ["description"] = "",
        ["authorId"] = "usr_avatar_author",
        ["authorName"] = "Avatar Author",
        ["releaseStatus"] = "public",
        ["tags"] = new JsonArray(),
        ["imageUrl"] = $"https://api.vrchat.cloud/api/1/file/file_{id}/1/file",
        ["thumbnailImageUrl"] = $"https://api.vrchat.cloud/api/1/image/file_{id}/1/256",
        ["unityPackages"] = new JsonArray(),
        ["version"] = 1,
        ["created_at"] = "2020-01-01T00:00:00.000Z",
        ["updated_at"] = "2020-01-01T00:00:00.000Z",
    })
    {
    }

    public FakeAvatar Author(string userId, string name) => Set("authorId", userId).Set("authorName", name);
    public FakeAvatar Private() => Set("releaseStatus", "private");
}

public sealed class FakeGroup : Fixture<FakeGroup>
{
    public FakeGroup(string id, string name) : base(new JsonObject
    {
        ["id"] = id,
        ["name"] = name,
        ["shortCode"] = "GRP",
        ["discriminator"] = "0000",
        ["description"] = "",
        ["iconUrl"] = $"https://api.vrchat.cloud/api/1/file/file_{id}_icon/1/file",
        ["bannerUrl"] = $"https://api.vrchat.cloud/api/1/file/file_{id}_banner/1/file",
        ["privacy"] = "default",
        ["ownerId"] = "usr_group_owner",
        ["memberCount"] = 1,
        ["joinState"] = "open",
        ["tags"] = new JsonArray(),
        ["roles"] = new JsonArray(),
        ["created_at"] = "2020-01-01T00:00:00.000Z",
    })
    {
    }

    public string Name => (string)Json["name"]!;
}

/// <summary>An instance (instances/{location}). The id is the full location tag.</summary>
public sealed class FakeInstance : Fixture<FakeInstance>
{
    public FakeInstance(string location, FakeWorld? world = null) : base(Build(location, world))
    {
    }

    private static JsonObject Build(string location, FakeWorld? world)
    {
        var parsed = Vrchat.VrchatLocation.Parse(location);
        var json = new JsonObject
        {
            ["id"] = location,
            ["location"] = location,
            ["instanceId"] = parsed.InstanceId,
            ["name"] = parsed.InstanceName,
            ["worldId"] = parsed.WorldId,
            ["type"] = parsed.AccessType,
            ["region"] = parsed.Region ?? "us",
            ["capacity"] = 32,
            ["n_users"] = 0,
            ["userCount"] = 0,
            ["platforms"] = new JsonObject { ["standalonewindows"] = 0, ["android"] = 0, ["ios"] = 0 },
            ["closedAt"] = null,
            ["users"] = new JsonArray(),
        };
        if (world is not null) json["world"] = world.Json.DeepClone();
        return json;
    }

    public FakeInstance Users(params FakeUser[] users)
    {
        Json["users"] = new JsonArray(users.Select(u => (JsonNode?)u.Json.DeepClone()).ToArray());
        return Set("n_users", users.Length).Set("userCount", users.Length);
    }

    public FakeInstance Closed(DateTimeOffset at) => Set("closedAt", at.ToString("O"));
}

/// <summary>A version 1 notification (auth/user/notifications, and the pipeline's "notification").</summary>
public sealed class FakeNotification : Fixture<FakeNotification>
{
    public FakeNotification(string id, string type, FakeUser sender, string receiverUserId, DateTimeOffset createdAt) : base(new JsonObject
    {
        ["id"] = id,
        ["type"] = type,
        ["senderUserId"] = sender.Id,
        ["senderUsername"] = sender.DisplayName,
        ["receiverUserId"] = receiverUserId,
        ["message"] = "",
        ["details"] = new JsonObject(),
        ["seen"] = false,
        ["created_at"] = createdAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
    })
    {
    }

    public string Type => (string)Json["type"]!;
    public FakeNotification Message(string message) => Set("message", message);
    public FakeNotification Details(JsonObject details) => Set("details", details);
}

/// <summary>A favorite (favorites), pointing at a user, world or avatar in a named group.</summary>
public sealed class FakeFavorite : Fixture<FakeFavorite>
{
    public FakeFavorite(string id, string type, string targetId, string groupName) : base(new JsonObject
    {
        ["id"] = id,
        ["type"] = type,
        ["favoriteId"] = targetId,
        ["tags"] = new JsonArray(groupName),
    })
    {
    }

    public string TargetId => (string)Json["favoriteId"]!;
    public string Type => (string)Json["type"]!;
    public string Group => (string)Json["tags"]![0]!;
}

public sealed class FakeFavoriteGroup : Fixture<FakeFavoriteGroup>
{
    public FakeFavoriteGroup(string id, string type, string name, string displayName, string ownerId) : base(new JsonObject
    {
        ["id"] = id,
        ["type"] = type,
        ["name"] = name,
        ["displayName"] = displayName,
        ["ownerId"] = ownerId,
        ["ownerDisplayName"] = "",
        ["visibility"] = "private",
        ["tags"] = new JsonArray(),
    })
    {
    }

    public string Name => (string)Json["name"]!;
    public string Type => (string)Json["type"]!;
}
