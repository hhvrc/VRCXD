using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using VRCX.Common.Services.Sessions;
using VRCX.Vrchat;

namespace VRCX.Testing.FakeVrchat;

/// <summary>One request the server sent to the fake VRChat.</summary>
public sealed record RecordedRequest(string Method, string Path, string Query, string? Body)
{
    /// <summary>"METHOD path?query", the form the older tests compare against.</summary>
    public string Call => $"{Method} {Path}{Query}";

    public JsonNode? Json => string.IsNullOrEmpty(Body) || Body[0] is not ('{' or '[') ? null : JsonNode.Parse(Body);

    public override string ToString() => Body is null ? Call : $"{Call} {Body}";
}

/// <summary>What a custom route handler gets.</summary>
public sealed record FakeRequest(string Method, string Path, string Query, string? Body, HttpRequestMessage Message)
{
    public string? QueryValue(string name) => HttpUtility.ParseQueryString(Query).Get(name);
    public JsonNode? Json => string.IsNullOrEmpty(Body) || Body[0] is not ('{' or '[') ? null : JsonNode.Parse(Body);
}

public sealed class FakeVrchatAssertionException(string message) : Exception(message);

/// <summary>
/// Stands in for api.vrchat.cloud inside the test host, and is the <see cref="IVrchatClientFactory"/>
/// the server opens every session through. It is stateful: the users, friends, worlds, avatars,
/// groups, instances, notifications and favorites a test adds are what the GET endpoints the
/// <see cref="VrchatApiClient"/> uses answer with, paged the way VRChat pages them, so a login and
/// a full sync work with no per-endpoint setup. Writes the model knows (favorites, status, avatar
/// selection, moderations) change that state; any other write answers 200 {}. Unknown GETs are
/// 404, as VRChat answers them. Routes added with <see cref="On"/>, <see cref="Json(string, string, HttpStatusCode)"/> or
/// <see cref="Status"/> win over the model, newest first. Every request is recorded.
/// </summary>
public sealed class FakeVrchatApi : HttpMessageHandler, IVrchatClientFactory
{
    private readonly Lock _gate = new();
    private readonly List<(string? Method, string PathAndQuery, Func<FakeRequest, HttpResponseMessage> Handler)> _routes = [];
    private readonly List<RecordedRequest> _requests = [];
    private readonly Dictionary<string, FakeUser> _users = new(StringComparer.Ordinal);
    private readonly List<string> _friends = [];
    private readonly Dictionary<string, FakeWorld> _worlds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FakeAvatar> _avatars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FakeGroup> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FakeInstance> _instances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string GroupId, bool Representing)>> _memberships = new(StringComparer.Ordinal);
    private readonly List<FakeNotification> _notifications = [];
    private readonly List<FakeNotification> _hiddenFriendRequests = [];
    private readonly List<JsonObject> _notificationsV2 = [];
    private readonly List<FakeFavorite> _favorites = [];
    private readonly List<FakeFavoriteGroup> _favoriteGroups = [];
    private readonly List<JsonObject> _playerModerations = [];
    private readonly List<JsonObject> _userNotes = [];
    private int _ids;

    public FakeVrchatApi(TimeProvider? time = null) => Time = time ?? TimeProvider.System;

    public TimeProvider Time { get; }

    /// <summary>The image downloader the server uses: every image is a 1x1 PNG (see <see cref="FakeImageFetcher"/>).</summary>
    public FakeImageFetcher Images { get; } = new();

    /// <summary>The logged-in account (auth/user). Null until <see cref="SetMe"/>: auth/user is then a 401.</summary>
    public FakeUser? Me { get; private set; }

    /// <summary>When set, auth/user with credentials answers requiresTwoFactorAuth until this code is verified.</summary>
    public string? TwoFactorCode { get; set; }
    /// <summary>The methods auth/user names while <see cref="TwoFactorCode"/> is unverified ("emailOtp" for an email code).</summary>
    public string[] TwoFactorMethods { get; set; } = ["totp", "otp"];
    private bool _twoFactorVerified;

    /// <summary>The token GET auth hands out for the pipeline.</summary>
    public string PipelineToken { get; set; } = "fake-pipeline-token";

    // ---- the model ------------------------------------------------------

    /// <summary>Sets the logged-in account. Its friends lists are computed from <see cref="Friend(FakeUser)"/>.</summary>
    public FakeUser SetMe(string id = "usr_me", string displayName = "Me", Action<FakeUser>? configure = null)
    {
        var me = new FakeUser(id, displayName).Status("active").Set("state", "online").Tags("system_trust_trusted")
            .Set("homeLocation", "").Set("friendGroupNames", new[] { "group_0", "group_1", "group_2" })
            .Set("twoFactorAuthEnabled", false).Set("hasSharedConnectionsOptOut", false).Set("currentAvatar", "avtr_me_current");
        configure?.Invoke(me);
        lock (_gate)
        {
            Me = me;
            _users[id] = me;
        }
        return me;
    }

    /// <summary>A user VRChat knows (users/{id}), not a friend.</summary>
    public FakeUser User(string id, string displayName, Action<FakeUser>? configure = null)
    {
        var user = new FakeUser(id, displayName);
        configure?.Invoke(user);
        return Add(user);
    }

    public FakeUser Add(FakeUser user)
    {
        lock (_gate) _users[user.Id] = user;
        return user;
    }

    /// <summary>A friend of <see cref="Me"/>. Offline unless configured otherwise (OnlineAt, Active).</summary>
    public FakeUser Friend(string id, string displayName, Action<FakeUser>? configure = null)
    {
        var user = User(id, displayName, configure);
        return Friend(user);
    }

    public FakeUser Friend(FakeUser user)
    {
        lock (_gate)
        {
            _users[user.Id] = user;
            user.Set("isFriend", true);
            if (!_friends.Contains(user.Id)) _friends.Add(user.Id);
        }
        return user;
    }

    /// <summary>Ends a friendship on VRChat's side; the user stays known.</summary>
    public void Unfriend(string userId)
    {
        lock (_gate)
        {
            _friends.Remove(userId);
            if (_users.TryGetValue(userId, out var user)) user.Set("isFriend", false);
        }
    }

    public FakeUser GetUser(string id)
    {
        lock (_gate) return _users[id];
    }

    public IReadOnlyList<FakeUser> Friends
    {
        get { lock (_gate) return _friends.Select(id => _users[id]).ToList(); }
    }

    public FakeWorld World(string id, string name, Action<FakeWorld>? configure = null)
    {
        var world = new FakeWorld(id, name);
        configure?.Invoke(world);
        lock (_gate) _worlds[id] = world;
        return world;
    }

    public FakeAvatar Avatar(string id, string name, Action<FakeAvatar>? configure = null)
    {
        var avatar = new FakeAvatar(id, name);
        configure?.Invoke(avatar);
        lock (_gate) _avatars[id] = avatar;
        return avatar;
    }

    public FakeGroup Group(string id, string name, Action<FakeGroup>? configure = null)
    {
        var group = new FakeGroup(id, name);
        configure?.Invoke(group);
        lock (_gate) _groups[id] = group;
        return group;
    }

    /// <summary>Puts a user in a group (users/{id}/groups), optionally as the one they represent.</summary>
    public void Member(string userId, FakeGroup group, bool representing = false)
    {
        lock (_gate)
        {
            var list = _memberships.TryGetValue(userId, out var existing) ? existing : _memberships[userId] = [];
            list.RemoveAll(m => m.GroupId == group.Id);
            if (representing)
                for (var i = 0; i < list.Count; i++) list[i] = (list[i].GroupId, false);
            list.Add((group.Id, representing));
        }
    }

    public void LeaveGroup(string userId, string groupId)
    {
        lock (_gate)
            if (_memberships.TryGetValue(userId, out var list)) list.RemoveAll(m => m.GroupId == groupId);
    }

    public FakeInstance Instance(string location, Action<FakeInstance>? configure = null)
    {
        var parsed = VrchatLocation.Parse(location);
        FakeWorld? world;
        lock (_gate) world = _worlds.GetValueOrDefault(parsed.WorldId);
        var instance = new FakeInstance(location, world);
        configure?.Invoke(instance);
        lock (_gate) _instances[parsed.InstanceLocation.Length > 0 ? parsed.InstanceLocation : location] = instance;
        return instance;
    }

    /// <summary>A version 1 notification to <see cref="Me"/> (auth/user/notifications). Pass it to the pipeline too when it should arrive live.</summary>
    public FakeNotification Notification(string type, FakeUser sender, Action<FakeNotification>? configure = null, string? id = null)
    {
        var notification = new FakeNotification(id ?? NewId("not"), type, sender, Me?.Id ?? "usr_me", Time.GetUtcNow());
        configure?.Invoke(notification);
        lock (_gate) _notifications.Add(notification);
        return notification;
    }

    /// <summary>A notification that is only in VRChat's ignored friend requests (auth/user/notifications?hidden=true).</summary>
    public FakeNotification IgnoredFriendRequest(FakeUser sender, string? id = null)
    {
        var notification = new FakeNotification(id ?? NewId("frq"), "friendRequest", sender, Me?.Id ?? "usr_me", Time.GetUtcNow());
        lock (_gate) _hiddenFriendRequests.Add(notification);
        return notification;
    }

    /// <summary>Removes a notification from VRChat's lists (answered elsewhere, expired).</summary>
    public void RemoveNotification(string id)
    {
        lock (_gate)
        {
            _notifications.RemoveAll(n => n.Id == id);
            _hiddenFriendRequests.RemoveAll(n => n.Id == id);
            _notificationsV2.RemoveAll(n => (string?)n["id"] == id);
        }
    }

    /// <summary>A version 2 notification (notifications). Only the given fields plus id/type/created_at are set.</summary>
    public JsonObject NotificationV2(string type, JsonObject? fields = null, string? id = null)
    {
        var json = fields?.DeepClone().AsObject() ?? [];
        json["id"] = id ?? NewId("not");
        json["type"] = type;
        json["version"] ??= 2;
        json["receiverUserId"] ??= Me?.Id ?? "usr_me";
        json["createdAt"] ??= Time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        lock (_gate) _notificationsV2.Add(json);
        return json;
    }

    public FakeFavoriteGroup FavoriteGroup(string type, string name, string? displayName = null)
    {
        var group = new FakeFavoriteGroup(NewId("fvgrp"), type, name, displayName ?? name, Me?.Id ?? "usr_me");
        lock (_gate)
        {
            _favoriteGroups.RemoveAll(g => g.Type == type && g.Name == name);
            _favoriteGroups.Add(group);
        }
        return group;
    }

    /// <summary>A favorite of <see cref="Me"/>. type is friend, world or avatar; group the internal name (group_0, worlds1, avatars1).</summary>
    public FakeFavorite Favorite(string type, string targetId, string group)
    {
        var favorite = new FakeFavorite(NewId("fvrt"), type, targetId, group);
        lock (_gate)
        {
            if (!_favoriteGroups.Any(g => g.Type == type && g.Name == group))
                _favoriteGroups.Add(new FakeFavoriteGroup(NewId("fvgrp"), type, group, group, Me?.Id ?? "usr_me"));
            _favorites.Add(favorite);
        }
        return favorite;
    }

    public void Unfavorite(string targetId)
    {
        lock (_gate) _favorites.RemoveAll(f => f.TargetId == targetId || f.Id == targetId);
    }

    public IReadOnlyList<FakeFavorite> Favorites
    {
        get { lock (_gate) return _favorites.ToList(); }
    }

    public JsonObject PlayerModeration(string type, FakeUser target)
    {
        var json = new JsonObject
        {
            ["id"] = NewId("pmod"),
            ["type"] = type,
            ["sourceUserId"] = Me?.Id ?? "usr_me",
            ["sourceDisplayName"] = Me?.DisplayName ?? "Me",
            ["targetUserId"] = target.Id,
            ["targetDisplayName"] = target.DisplayName,
            ["created"] = Time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        };
        lock (_gate) _playerModerations.Add(json);
        return json;
    }

    public JsonObject UserNote(string targetUserId, string note)
    {
        var json = new JsonObject
        {
            ["id"] = NewId("unt"),
            ["userId"] = Me?.Id ?? "usr_me",
            ["targetUserId"] = targetUserId,
            ["note"] = note,
            ["createdAt"] = Time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        };
        lock (_gate)
        {
            _userNotes.RemoveAll(n => (string?)n["targetUserId"] == targetUserId);
            _userNotes.Add(json);
        }
        return json;
    }

    private string NewId(string prefix) => $"{prefix}_{Interlocked.Increment(ref _ids):D4}{Guid.NewGuid():N}"[..(prefix.Length + 13)];

    // ---- overrides ------------------------------------------------------

    /// <summary>A custom answer for one route. method null matches any; pathAndQuery matches "path?query" exactly, or "path" whatever the query.</summary>
    public FakeVrchatApi On(string? method, string pathAndQuery, Func<FakeRequest, HttpResponseMessage> handler)
    {
        lock (_gate) _routes.Add((method?.ToUpperInvariant(), pathAndQuery, handler));
        return this;
    }

    /// <summary>Canned JSON for a route, any method (the shape the older tests use).</summary>
    public FakeVrchatApi Json(string pathAndQuery, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        On(null, pathAndQuery, _ => Response(status, json));

    public FakeVrchatApi Json(string method, string pathAndQuery, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        On(method, pathAndQuery, _ => Response(status, json));

    public FakeVrchatApi Status(string pathAndQuery, HttpStatusCode status, string body = "") =>
        On(null, pathAndQuery, _ => Response(status, body));

    /// <summary>VRChat's error envelope with the given status for a route.</summary>
    public FakeVrchatApi Fail(string? method, string pathAndQuery, HttpStatusCode status, string message = "error") =>
        On(method, pathAndQuery, _ => Error(status, message));

    public static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Ok(JsonNode? json) => Response(HttpStatusCode.OK, json?.ToJsonString() ?? "null");

    public static HttpResponseMessage Error(HttpStatusCode status, string message) =>
        Response(status, new JsonObject { ["error"] = new JsonObject { ["message"] = message, ["status_code"] = (int)status } }.ToJsonString());

    // ---- recording and assertions ---------------------------------------

    public IReadOnlyList<RecordedRequest> Requests
    {
        get { lock (_gate) return _requests.ToList(); }
    }

    /// <summary>"METHOD path?query" for every request, oldest first.</summary>
    public IReadOnlyList<string> Calls => Requests.Select(r => r.Call).ToList();

    /// <summary>The last body sent under each "METHOD path?query" (the shape the older tests use).</summary>
    public IReadOnlyDictionary<string, string> Bodies =>
        Requests.Where(r => r.Body is not null).GroupBy(r => r.Call).ToDictionary(g => g.Key, g => g.Last().Body!);

    /// <summary>The requests to one route (path without query).</summary>
    public IReadOnlyList<RecordedRequest> RequestsTo(string method, string path) =>
        Requests.Where(r => r.Method == method.ToUpperInvariant() && r.Path == path).ToList();

    public int CountOf(string method, string path) => RequestsTo(method, path).Count;

    /// <summary>The last request to a route; fails when there was none.</summary>
    public RecordedRequest AssertCalled(string method, string path)
    {
        var calls = RequestsTo(method, path);
        if (calls.Count == 0)
            throw new FakeVrchatAssertionException($"Expected {method} {path} to be called. Calls were:\n  {string.Join("\n  ", Calls)}");
        return calls[^1];
    }

    public void AssertNotCalled(string method, string path)
    {
        var calls = RequestsTo(method, path);
        if (calls.Count > 0)
            throw new FakeVrchatAssertionException($"Expected no {method} {path}, but it was called {calls.Count} time(s):\n  {string.Join("\n  ", calls)}");
    }

    /// <summary>Waits for a request the server sends in the background (a reaction to the pipeline, say).</summary>
    public async Task<RecordedRequest> WaitForCallAsync(string method, string path, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            var calls = RequestsTo(method, path);
            if (calls.Count > 0) return calls[^1];
            await Task.Delay(20);
        }
        return AssertCalled(method, path);
    }

    public void ClearRequests()
    {
        lock (_gate) _requests.Clear();
    }

    // ---- IVrchatClientFactory -------------------------------------------

    public VrchatApiClient Create(CookieContainer? cookies = null, VrchatEndpoint? endpoint = null)
    {
        lock (_gate) CreatedFor.Add(endpoint);
        return VrchatApiClient.Create(cookies, new NonDisposingHandler(this), VrchatRateLimiter.Unlimited, endpoint);
    }

    /// <summary>The endpoint each client was made for, so tests can check a custom one was used.</summary>
    public List<VrchatEndpoint?> CreatedFor { get; } = [];

    // Each VrchatApiClient disposes its handler chain; this one is shared by all of them.
    private sealed class NonDisposingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override void Dispose(bool disposing) { }
    }

    // ---- the HTTP side ----------------------------------------------------

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        if (!uri.AbsolutePath.StartsWith("/api/1/", StringComparison.Ordinal))
            throw new FakeVrchatAssertionException($"The server called {uri} outside the VRChat API; tests must stay hermetic.");
        var path = uri.AbsolutePath["/api/1/".Length..];
        var query = uri.Query;
        var method = request.Method.Method;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var fake = new FakeRequest(method, path, query, body, request);

        Func<FakeRequest, HttpResponseMessage>? handler = null;
        lock (_gate)
        {
            _requests.Add(new RecordedRequest(method, path, query, body));
            for (var i = _routes.Count - 1; i >= 0; i--)
            {
                var route = _routes[i];
                if (route.Method is not null && route.Method != method) continue;
                if (route.PathAndQuery == path + query || route.PathAndQuery == path)
                {
                    handler = route.Handler;
                    break;
                }
            }
        }
        if (handler is not null) return handler(fake);

        HttpResponseMessage? modelled;
        lock (_gate) modelled = method == "GET" ? Get(fake) : Write(fake);
        return modelled ?? Error(HttpStatusCode.NotFound, "Not Found");
    }

    private static string[] Segments(string path) => path.Split('/').Select(Uri.UnescapeDataString).ToArray();

    private static int IntQuery(FakeRequest r, string name, int fallback) =>
        int.TryParse(r.QueryValue(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static HttpResponseMessage Page(IEnumerable<JsonNode> items, FakeRequest r, int defaultSize)
    {
        var n = IntQuery(r, "n", defaultSize);
        var offset = IntQuery(r, "offset", 0);
        return Ok(new JsonArray(items.Skip(offset).Take(n).Select(i => (JsonNode?)i.DeepClone()).ToArray()));
    }

    private JsonObject CurrentUserJson()
    {
        var me = Me!.Json.DeepClone().AsObject();
        string[] Ids(Func<FakeUser, bool> state) => _friends.Where(id => state(_users[id])).ToArray();
        me["friends"] = new JsonArray(_friends.Select(id => (JsonNode?)id).ToArray());
        me["onlineFriends"] = new JsonArray(Ids(u => u.State == "online").Select(id => (JsonNode?)id).ToArray());
        me["activeFriends"] = new JsonArray(Ids(u => u.State == "active").Select(id => (JsonNode?)id).ToArray());
        me["offlineFriends"] = new JsonArray(Ids(u => u.State == "offline").Select(id => (JsonNode?)id).ToArray());
        return me;
    }

    private HttpResponseMessage? Get(FakeRequest r)
    {
        var s = Segments(r.Path);
        switch (s)
        {
            case ["auth", "user"]:
                if (Me is null) return Error(HttpStatusCode.Unauthorized, "Missing Credentials");
                if (TwoFactorCode is not null && !_twoFactorVerified)
                    return Ok(new JsonObject { ["requiresTwoFactorAuth"] = new JsonArray(TwoFactorMethods.Select(m => (JsonNode?)m).ToArray()) });
                return Ok(CurrentUserJson());
            case ["auth"]:
                return Me is null ? Error(HttpStatusCode.Unauthorized, "Missing Credentials") : Ok(new JsonObject { ["ok"] = true, ["token"] = PipelineToken });
            case ["auth", "user", "friends"]:
                {
                    var offline = r.QueryValue("offline") == "true";
                    var list = _friends.Select(id => _users[id]).Where(u => (u.State == "offline") == offline).Select(u => (JsonNode)u.Json);
                    return Page(list, r, 100);
                }
            case ["users", var id] when _users.TryGetValue(id, out var user):
                return Ok(user.Json);
            case ["profile", var id] when _users.TryGetValue(id, out var user):
                return Ok(new JsonObject { ["id"] = id, ["bio"] = user.Json["bio"]?.DeepClone(), ["bioLinks"] = user.Json["bioLinks"]?.DeepClone(), ["badges"] = user.Json["badges"]?.DeepClone() ?? new JsonArray(), ["hasVrcPlus"] = user.Json["tags"] is JsonArray tags && tags.Any(t => (string?)t == "system_supporter") });
            case ["user", var id, "friendStatus"]:
                return Ok(new JsonObject { ["isFriend"] = _friends.Contains(id), ["incomingRequest"] = false, ["outgoingRequest"] = false });
            case ["users", var id, "groups"]:
                return Ok(new JsonArray(Memberships(id).Select(m => (JsonNode?)m).ToArray()));
            case ["users", var id, "groups", "represented"]:
                return Ok(Memberships(id).FirstOrDefault(m => (bool)m["isRepresenting"]!) ?? []);
            case ["users", _, "mutuals"]:
                return Ok(new JsonObject { ["friends"] = 0, ["groups"] = 0 });
            case ["users", _, "mutuals", "friends"]:
                return Page([], r, 100);
            case ["users", _, "instances", "groups"]:
                return Ok(new JsonObject { ["instances"] = new JsonArray() });
            case ["worlds", var id] when _worlds.TryGetValue(id, out var world):
                return Ok(world.Json);
            case ["avatars", var id] when _avatars.TryGetValue(id, out var avatar):
                return Ok(avatar.Json);
            case ["groups", var id] when _groups.TryGetValue(id, out var group):
                return Ok(group.Json);
            case ["groups", var id, "roles"] when _groups.ContainsKey(id):
                return Ok(new JsonArray());
            case ["instances", var location]:
                {
                    var key = VrchatLocation.Parse(location).InstanceLocation;
                    return _instances.TryGetValue(key.Length > 0 ? key : location, out var instance) ? Ok(instance.Json) : null;
                }
            case ["favorite", "groups"]:
                return Ok(new JsonArray(_favoriteGroups.Select(g => (JsonNode?)g.Json.DeepClone()).ToArray()));
            case ["favorites"]:
                {
                    var type = r.QueryValue("type");
                    return Page(_favorites.Where(f => type is null || f.Type == type).Select(f => (JsonNode)f.Json), r, 100);
                }
            case ["avatars", "favorites"]:
                return Page(_favorites.Where(f => f.Type == "avatar" && _avatars.ContainsKey(f.TargetId)).Select(f => (JsonNode)_avatars[f.TargetId].Json), r, 50);
            case ["worlds", "favorites"]:
                return Page(_favorites.Where(f => f.Type is "world" or "vrcPlusWorld" && _worlds.ContainsKey(f.TargetId)).Select(f => (JsonNode)_worlds[f.TargetId].Json), r, 50);
            case ["auth", "user", "favoritelimits"]:
                return Ok(new JsonObject
                {
                    ["maxFavoriteGroups"] = new JsonObject { ["avatar"] = 6, ["friend"] = 3, ["vrcPlusWorld"] = 4, ["world"] = 4 },
                    ["maxFavoritesPerGroup"] = new JsonObject { ["avatar"] = 50, ["friend"] = 150, ["vrcPlusWorld"] = 100, ["world"] = 100 },
                });
            case ["auth", "user", "playermoderations"]:
                return Ok(new JsonArray(_playerModerations.Select(m => (JsonNode?)m.DeepClone()).ToArray()));
            case ["auth", "user", "avatarmoderations"]:
                return Ok(new JsonArray());
            case ["userNotes"]:
                return Page(_userNotes, r, 100);
            case ["auth", "user", "notifications"]:
                return Page((r.QueryValue("hidden") == "true" ? _hiddenFriendRequests : _notifications).Select(n => (JsonNode)n.Json), r, 100);
            case ["notifications"]:
                return Page(_notificationsV2, r, 100);
            case ["avatars"] when r.QueryValue("user") == "me":
                return Page(_avatars.Values.Where(a => (string?)a.Json["authorId"] == Me?.Id).Select(a => (JsonNode)a.Json), r, 50);
            case ["worlds"] when r.QueryValue("user") is "me" or "friends":
                {
                    var author = r.QueryValue("user") == "me" ? Me?.Id : r.QueryValue("userId");
                    return Page(_worlds.Values.Where(w => (string?)w.Json["authorId"] == author).Select(w => (JsonNode)w.Json), r, 100);
                }
            default:
                return null;
        }
    }

    private List<JsonObject> Memberships(string userId) =>
        (_memberships.GetValueOrDefault(userId) ?? []).Where(m => _groups.ContainsKey(m.GroupId)).Select(m =>
        {
            var group = _groups[m.GroupId].Json;
            return new JsonObject
            {
                ["id"] = $"gmem_{userId}_{m.GroupId}",
                ["groupId"] = m.GroupId,
                ["name"] = group["name"]?.DeepClone(),
                ["shortCode"] = group["shortCode"]?.DeepClone(),
                ["discriminator"] = group["discriminator"]?.DeepClone(),
                ["description"] = group["description"]?.DeepClone(),
                ["iconUrl"] = group["iconUrl"]?.DeepClone(),
                ["bannerUrl"] = group["bannerUrl"]?.DeepClone(),
                ["ownerId"] = group["ownerId"]?.DeepClone(),
                ["privacy"] = group["privacy"]?.DeepClone(),
                ["memberCount"] = group["memberCount"]?.DeepClone(),
                ["memberVisibility"] = "visible",
                ["isRepresenting"] = m.Representing,
                ["mutualGroup"] = false,
            };
        }).ToList();

    private HttpResponseMessage? Write(FakeRequest r)
    {
        var s = Segments(r.Path);
        var body = r.Json as JsonObject;
        switch (r.Method, s)
        {
            case ("PUT", ["logout"]):
                return Ok(new JsonObject());
            case ("POST", ["auth", "twofactorauth", _, "verify"]):
                {
                    var verified = TwoFactorCode is not null && (string?)body?["code"] == TwoFactorCode;
                    if (verified) _twoFactorVerified = true;
                    return verified ? Ok(new JsonObject { ["verified"] = true }) : Error(HttpStatusCode.BadRequest, "Invalid code");
                }
            case ("POST", ["favorites"]) when body is not null:
                {
                    var type = (string?)body["type"] ?? "friend";
                    var target = (string?)body["favoriteId"] ?? "";
                    var group = body["tags"] switch
                    {
                        JsonArray array => (string?)array.FirstOrDefault() ?? "",
                        JsonValue value => (string?)value ?? "",
                        _ => "",
                    };
                    var favorite = new FakeFavorite(NewId("fvrt"), type, target, group);
                    _favorites.Add(favorite);
                    return Ok(favorite.Json);
                }
            case ("DELETE", ["favorites", var id]):
                _favorites.RemoveAll(f => f.Id == id || f.TargetId == id);
                return Ok(new JsonObject { ["success"] = new JsonObject { ["message"] = "OK", ["status_code"] = 200 } });
            case ("PUT", ["users", var id]) when Me is not null && id == Me.Id && body is not null:
                foreach (var (key, value) in body) Me.Json[key] = value?.DeepClone();
                return Ok(CurrentUserJson());
            case ("PUT", ["users", var id, "badges", var badgeId]) when Me is not null && id == Me.Id && body is not null:
                {
                    var badge = (Me.Json["badges"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(b => (string?)b["badgeId"] == badgeId);
                    if (badge is null) return Error(HttpStatusCode.NotFound, "Badge not found");
                    badge["hidden"] = body["hidden"]?.DeepClone();
                    badge["showcased"] = body["showcased"]?.DeepClone();
                    return Ok(badge.DeepClone());
                }
            case ("PUT", ["avatars", var id, "select"]) when Me is not null:
                Me.Set("currentAvatar", id);
                if (_avatars.TryGetValue(id, out var avatar))
                    Me.Set("currentAvatarImageUrl", (string?)avatar.Json["imageUrl"]).Set("currentAvatarThumbnailImageUrl", (string?)avatar.Json["thumbnailImageUrl"]);
                return Ok(CurrentUserJson());
            case ("POST", ["auth", "user", "playermoderations"]) when body is not null && _users.TryGetValue((string?)body["moderated"] ?? "", out var target):
                return Ok(PlayerModeration((string?)body["type"] ?? "block", target));
            case ("PUT", ["auth", "user", "unplayermoderate"]):
                {
                    var targetId = (string?)body?["moderated"];
                    var type = (string?)body?["type"];
                    _playerModerations.RemoveAll(m => (targetId is null || (string?)m["targetUserId"] == targetId) && (type is null || (string?)m["type"] == type));
                    return Ok(new JsonObject());
                }
            case ("PUT", ["auth", "user", "notifications", var id, "hide" or "accept"]):
                _notifications.RemoveAll(n => n.Id == id);
                return Ok(new JsonObject());
            default:
                return Ok(new JsonObject());
        }
    }
}
