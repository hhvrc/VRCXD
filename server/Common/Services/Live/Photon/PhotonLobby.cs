using System.Text.Json;
using System.Text.Json.Serialization;

namespace VRCX.Common.Services.Live.Photon;

/// <summary>A player as the Photon stream described them (the original's photonLobby values).</summary>
public sealed class PhotonUser
{
    public string? Id { get; init; }
    public string? DisplayName { get; init; }
    public string? Status { get; init; }
    public string? StatusDescription { get; init; }
    public string? CurrentAvatarImageUrl { get; init; }
    public string? CurrentAvatarThumbnailImageUrl { get; init; }
    public string? ProfilePicOverride { get; init; }
    public string? UserIcon { get; init; }
    public string? DeveloperType { get; init; }
    public string? LastPlatform { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>The original's photonLobbyJointime entry: when the player joined and what their join said.</summary>
public sealed class PhotonJoin
{
    public DateTimeOffset? JoinTime { get; set; }
    public bool HasInstantiated { get; set; }
    public bool? InVrMode { get; set; }
    public double? AvatarEyeHeight { get; set; }
    public bool? CanModerateInstance { get; set; }
    /// <summary>Set once a join said anything about it, even null.</summary>
    [JsonIgnore] public bool GroupKnown { get; set; }
    public string? GroupOnNameplate { get; set; }
    public bool? ShowGroupBadgeToOthers { get; set; }
    public bool? ShowSocialRank { get; set; }
    public bool? UseImpostorAsFallback { get; set; }
    public string? Platform { get; set; }
}

public sealed record PhotonAvatar(string? Id, string? Name, string? Description, string? AuthorId, string? AuthorName, string? ReleaseStatus, string? ImageUrl, string? ThumbnailImageUrl);

/// <summary>A row of the original's photonEventTable.</summary>
public sealed class PhotonFeedEntry
{
    public DateTimeOffset CreatedAt { get; init; }
    public required string Type { get; init; }
    public string? Text { get; init; }
    public string? Color { get; init; }
    public int? PhotonId { get; init; }
    public string DisplayName { get; set; } = "";
    public string UserId { get; set; } = "";
    public bool IsMaster { get; set; }
    public bool? IsModerator { get; set; }
    public PhotonAvatar? Avatar { get; init; }
    public string? Platform { get; init; }
    public string? Status { get; init; }
    public string? PreviousStatus { get; init; }
    public string? StatusDescription { get; init; }
    public string? PreviousStatusDescription { get; init; }
    public string? GroupId { get; init; }
    public string? GroupName { get; set; }
    public string? PreviousGroupId { get; init; }
    public string? PreviousGroupName { get; set; }
    public string? ShortName { get; init; }
    public string? Location { get; init; }
    public string? WorldName { get; init; }
    public string? ImageUrl { get; init; }
    public string? FileId { get; init; }
}

/// <summary>What a packet asks of the world outside the lobby; the service does it.</summary>
public abstract record PhotonEffect;
/// <summary>addPhotonEventToGameLog for an Event: a game log Event row.</summary>
public sealed record PhotonGameLogEvent(DateTimeOffset At, string Data) : PhotonEffect;
/// <summary>addPhotonPortalSpawn: the target found from its short name, then a feed row and a game log row.</summary>
public sealed record PhotonPortalSpawn(DateTimeOffset At, string? UserId, string ShortName, string WorldName) : PhotonEffect;
/// <summary>parsePhotonGroupChange: the entry waits for both group names.</summary>
public sealed record PhotonGroupChange(PhotonFeedEntry Entry) : PhotonEffect;
/// <summary>photonModerationUpdate: compared with the stored row before anything is said.</summary>
public sealed record PhotonModeration(int PhotonId, string UserId, string? DisplayName, bool Block, bool Mute, DateTimeOffset At) : PhotonEffect;
/// <summary>parsePhotonAvatar: the avatar is stored.</summary>
public sealed record PhotonAvatarSeen(JsonElement Avatar) : PhotonEffect;
/// <summary>parsePhotonUser: the user's current avatar images refreshed when the stream is live.</summary>
public sealed record PhotonUserSeen(PhotonUser User, DateTimeOffset At) : PhotonEffect;
/// <summary>A notification and the live game log table only (ChatBoxMessage, AvatarChange): nothing stored.</summary>
public sealed record PhotonNoty(string Type, DateTimeOffset At, string? UserId, string? DisplayName, string? Text, PhotonAvatar? Avatar = null) : PhotonEffect;

/// <summary>A player whose movement stopped: the original's photonLobbyTimeout row.</summary>
public sealed record PhotonTimeout(string UserId, string DisplayName, int Time, double RawTime);

/// <summary>
/// One PC's Photon lobby (the original's photon store): the players by Photon id, the master,
/// join details, portals and the event table, fed by the Photon logger's packets. It is pure;
/// what needs the database or VRChat comes back as <see cref="PhotonEffect"/>s.
/// </summary>
public sealed class PhotonLobby
{
    public const string ApiEndpoint = "https://api.vrchat.cloud/api/1";
    /// <summary>The event table keeps this many rows.</summary>
    public const int MaxEvents = 1000;

    /// <summary>shared/constants/photon.js photonEmojis.</summary>
    public static readonly string[] Emojis =
    [
        "Angry", "Blushing", "Crying", "Frown", "Hand Wave", "Hang Ten", "In Love", "Jack O Lantern", "Kiss", "Laugh", "Skull", "Smile", "Spooky Ghost", "Stoic",
        "Sunglasses", "Thinking", "Thumbs Down", "Thumbs Up", "Tongue Out", "Wow", "Arrow Point", "Can't see", "Hourglass", "Keyboard", "No Headphones", "No Mic",
        "Portal", "Shush", "Bats", "Cloud", "Fire", "Snow Fall", "Snowball", "Splash", "Web", "Beer", "Candy", "Candy Cane", "Candy Corn", "Champagne", "Drink",
        "Gingerbread", "Ice Cream", "Pineapple", "Pizza", "Tomato", "Beachball", "Coal", "Confetti", "Gift", "Gifts", "Life Ring", "Mistletoe", "Money", "Neon Shades",
        "Sun Lotion", "Boo", "Broken Heart", "Exclamation", "Go", "Heart", "Music Note", "Question", "Stop", "Zzz",
    ];

    /// <summary>The chatbox keyword blacklist the original starts with.</summary>
    public static readonly string[] DefaultChatboxBlacklist = ["NP: ", "Now Playing", "Now playing", "▶️ '", "( ▶️ ", "' - '", "' by '", "[Spotify] "];

    private sealed class Portal
    {
        public string? UserId { get; init; }
        public string? ShortName { get; init; }
        public string WorldName { get; init; } = "";
        public DateTimeOffset CreatedAt { get; init; }
        public long PlayerCount { get; set; }
        public int PendingLeave { get; set; }
    }

    private readonly Dictionary<string, Portal> _portals = new();
    private readonly Dictionary<int, string> _lastChatBox = new();
    private readonly Dictionary<int, (bool Block, bool Mute, DateTimeOffset At)> _moderationQueue = new();
    private readonly Dictionary<int, PhotonUser> _userData = new();

    /// <summary>The instance this lobby is for; another one starts it over.</summary>
    public string? Location { get; private set; }
    public Dictionary<int, PhotonUser?> Lobby { get; } = new();
    public Dictionary<int, PhotonUser?> Current { get; } = new();
    public int Master { get; private set; }
    public int CurrentUser { get; private set; }
    public Dictionary<string, string> Avatars { get; } = new(StringComparer.Ordinal);
    public Dictionary<int, string> LastModeration { get; } = new();
    public Dictionary<int, PhotonJoin> Joins { get; } = new();
    public Dictionary<int, DateTimeOffset> Event7 { get; } = new();
    public DateTimeOffset? LastEvent7List { get; private set; }
    /// <summary>Newest first.</summary>
    public List<PhotonFeedEntry> Events { get; private set; } = [];
    public List<PhotonFeedEntry> PreviousEvents { get; private set; } = [];

    /// <summary>resetLocationPhotonState, for the instance now playing.</summary>
    public void Reset(string? location)
    {
        Location = location;
        Lobby.Clear();
        Current.Clear();
        Master = 0;
        CurrentUser = 0;
        _userData.Clear();
        Avatars.Clear();
        LastModeration.Clear();
        Joins.Clear();
        _portals.Clear();
        Event7.Clear();
        LastEvent7List = null;
        _lastChatBox.Clear();
        _moderationQueue.Clear();
        if (Events.Count > 0)
        {
            PreviousEvents = Events;
            Events = [];
        }
    }

    // ---- JSON as JavaScript read it: an index is a property or an array item, numbers may be strings.

    private static JsonElement? Get(JsonElement e, string key)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Undefined) return v;
        if (e.ValueKind == JsonValueKind.Array && int.TryParse(key, out var i) && i >= 0 && i < e.GetArrayLength()) return e[i];
        return null;
    }

    private static JsonElement? Get(JsonElement? e, string key) => e is { } v ? Get(v, key) : null;

    /// <summary>safeParseInt.</summary>
    public static int? Int(JsonElement? e) => e switch
    {
        { ValueKind: JsonValueKind.Number } n when n.TryGetDouble(out var d) && double.IsFinite(d) => (int)Math.Truncate(d),
        { ValueKind: JsonValueKind.String } s when int.TryParse(LeadingInt(s.GetString()!), out var i) => i,
        _ => null,
    };

    private static string LeadingInt(string s)
    {
        s = s.TrimStart();
        var end = 0;
        while (end < s.Length && (char.IsAsciiDigit(s[end]) || (end == 0 && (s[end] == '-' || s[end] == '+')))) end++;
        return s[..end];
    }

    private static long? Long(JsonElement? e) => e is { ValueKind: JsonValueKind.Number } n && n.TryGetInt64(out var l) ? l : Int(e);

    private static string? Str(JsonElement? e) => e switch
    {
        { ValueKind: JsonValueKind.String } s => s.GetString(),
        { ValueKind: JsonValueKind.Number } n => n.GetRawText(),
        _ => null,
    };

    private static bool? Bool(JsonElement? e) => e switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        _ => null,
    };

    private static bool Truthy(JsonElement? e) => e switch
    {
        null => false,
        { ValueKind: JsonValueKind.Null or JsonValueKind.False } => false,
        { ValueKind: JsonValueKind.Number } n => n.TryGetDouble(out var d) && d != 0 && !double.IsNaN(d),
        { ValueKind: JsonValueKind.String } s => s.GetString()!.Length > 0,
        _ => true,
    };

    private static DateTimeOffset Date(JsonElement? e, DateTimeOffset fallback) =>
        Str(e) is { } s && DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : fallback;

    /// <summary>replaceBioSymbols, keeping a missing text missing.</summary>
    public static string? ReplaceBioSymbols(string? text) => text is null ? null : VrchatText.ReplaceBioSymbols(text);

    /// <summary>timeToText for a duration in milliseconds.</summary>
    public static string TimeToText(double ms)
    {
        var n = (long)Math.Floor(Math.Abs(ms) / 1000);
        var parts = new List<string>();
        if (n >= 31536000) { parts.Add($"{n / 31536000}y"); n %= 31536000; }
        if (n >= 86400) { parts.Add($"{n / 86400}d"); n %= 86400; }
        if (n >= 3600) { parts.Add($"{n / 3600}h"); n %= 3600; }
        if (n >= 60) { parts.Add($"{n / 60}m"); n %= 60; }
        if (parts.Count == 0) parts.Add($"{(long)Math.Floor((n + 2.5) / 5) * 5}s");
        return string.Join(' ', parts);
    }

    public static PhotonAvatar? AvatarOf(JsonElement? a) => a is { ValueKind: JsonValueKind.Object } v
        ? new PhotonAvatar(Str(Get(v, "id")), ReplaceBioSymbols(Str(Get(v, "name"))), ReplaceBioSymbols(Str(Get(v, "description"))), Str(Get(v, "authorId")),
            Str(Get(v, "authorName")), Str(Get(v, "releaseStatus")), Str(Get(v, "imageUrl")), Str(Get(v, "thumbnailImageUrl")))
        : null;

    // ---- getDisplayNameFromPhotonId and friends.

    public string DisplayNameOf(int? photonId)
    {
        if (photonId is not { } id || id == 0) return "";
        return Lobby.GetValueOrDefault(id)?.DisplayName ?? $"ID:{id}";
    }

    public string UserIdOf(int? photonId) => photonId is { } id && id != 0 ? Lobby.GetValueOrDefault(id)?.Id ?? "" : "";

    public int? PhotonIdOfUser(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        int? found = null;
        foreach (var (id, user) in Lobby)
            if (user?.Id == userId) found = id;
        return found;
    }

    /// <summary>addEntryPhotonEvent: who it is, master and moderator, then the table.</summary>
    public PhotonFeedEntry Add(PhotonFeedEntry entry)
    {
        if (entry.PhotonId is { } id)
        {
            entry.IsMaster = id == Master;
            entry.IsModerator = Joins.GetValueOrDefault(id)?.CanModerateInstance;
            if (Lobby.GetValueOrDefault(id) is { } user)
            {
                if (entry.DisplayName.Length == 0) entry.DisplayName = user.DisplayName ?? "";
                if (entry.UserId.Length == 0) entry.UserId = user.Id ?? "";
            }
        }
        Events.Insert(0, entry);
        if (Events.Count > MaxEvents) Events.RemoveAt(Events.Count - 1);
        return entry;
    }

    // ---- Packets.

    /// <summary>One Photon packet as the logger wrote it, the original's ipcEvent cases.</summary>
    public List<PhotonEffect> Apply(JsonElement packet, PhotonSettings settings, DateTimeOffset now)
    {
        var effects = new List<PhotonEffect>();
        var at = Date(Get(packet, "dt"), now);
        switch (Str(Get(packet, "type")))
        {
            case "OnEvent" when Get(packet, "OnEventData") is { } data:
                OnEvent(data, at, now, settings, effects);
                break;
            case "OnOperationResponse" when Get(packet, "OnOperationResponseData") is { } data:
                OnOperationResponse(data, at, effects);
                break;
            case "VRCEvent" when Get(packet, "VRCEventData") is { } data:
                OnVrcEvent(data, at, effects);
                break;
            case "Event7List":
                Event7.Clear();
                if (Get(packet, "Event7List") is { ValueKind: JsonValueKind.Object } list)
                    foreach (var p in list.EnumerateObject())
                        if (int.TryParse(LeadingInt(p.Name), out var id))
                            Event7[id] = Date(p.Value, now);
                LastEvent7List = at;
                break;
        }
        return effects;
    }

    private void OnOperationResponse(JsonElement data, DateTimeOffset at, List<PhotonEffect> effects)
    {
        if (Int(Get(data, "OperationCode")) != 226) return;
        var p = Get(data, "Parameters");
        if (Get(Get(p, "248"), "248") is { } master) SetMaster(Int(master) ?? 0, at);
        if (Int(Get(p, "254")) is { } me) CurrentUser = me;
        if (Get(p, "249") is { ValueKind: JsonValueKind.Object } players)
            foreach (var player in players.EnumerateObject())
            {
                if (!int.TryParse(LeadingInt(player.Name), out var id)) continue;
                if (Get(player.Value, "user") is not { ValueKind: JsonValueKind.Object } user) continue;
                ParsePlayer(id, player.Value, user, at, effects);
                Joins[id] = JoinFrom(player.Value, at, Joins.GetValueOrDefault(id)?.HasInstantiated ?? false);
            }
        if (Get(p, "252") is { } ids) LobbyIds(ids);
        Event7.Clear();
    }

    /// <summary>parsePhotonUser, the avatar change, the avatars and the nameplate group, as every join path does.</summary>
    private void ParsePlayer(int id, JsonElement player, JsonElement user, DateTimeOffset at, List<PhotonEffect> effects)
    {
        ParseUser(id, user, at, effects);
        if (Get(player, "avatarDict") is { ValueKind: JsonValueKind.Object } avatar)
        {
            AvatarChange(id, user, avatar, at, effects);
            effects.Add(new PhotonAvatarSeen(avatar.Clone()));
        }
        if (Get(player, "favatarDict") is { ValueKind: JsonValueKind.Object } fallback) effects.Add(new PhotonAvatarSeen(fallback.Clone()));
        if (Get(player, "groupOnNameplate") is { } group) GroupChange(id, user, group, at, effects);
    }

    private static PhotonJoin JoinFrom(JsonElement player, DateTimeOffset? joinTime, bool hasInstantiated)
    {
        var group = Get(player, "groupOnNameplate");
        return new PhotonJoin
        {
            JoinTime = joinTime,
            HasInstantiated = hasInstantiated,
            InVrMode = Bool(Get(player, "inVRMode")),
            AvatarEyeHeight = Get(player, "avatarEyeHeight") is { ValueKind: JsonValueKind.Number } h ? h.GetDouble() : null,
            CanModerateInstance = Bool(Get(player, "canModerateInstance")),
            GroupKnown = group is not null,
            GroupOnNameplate = Str(group),
            ShowGroupBadgeToOthers = Bool(Get(player, "showGroupBadgeToOthers")),
            ShowSocialRank = Bool(Get(player, "showSocialRank")),
            UseImpostorAsFallback = Bool(Get(player, "useImpostorAsFallback")),
            Platform = Str(Get(player, "platform")),
        };
    }

    private void OnEvent(JsonElement data, DateTimeOffset at, DateTimeOffset now, PhotonSettings settings, List<PhotonEffect> effects)
    {
        var p = Get(data, "Parameters");
        switch (Int(Get(data, "Code")))
        {
            case 253:
                if (Int(Get(p, "253")) == -1)
                {
                    if (Get(p, "251") is { ValueKind: JsonValueKind.Object } players)
                        foreach (var player in players.EnumerateObject())
                            if (int.TryParse(LeadingInt(player.Name), out var id) && Get(player.Value, "user") is { ValueKind: JsonValueKind.Object } user)
                                SetUserProps(id, player.Value, user, at, now, effects);
                }
                else if (Int(Get(p, "253")) is { } id && Get(p, "251") is { } player && Get(player, "user") is { ValueKind: JsonValueKind.Object } user)
                {
                    SetUserProps(id, player, user, at, now, effects);
                }
                break;
            case 42:
                {
                    if (Int(Get(p, "254")) is not { } id || Get(p, "245") is not { } player || Get(player, "user") is not { ValueKind: JsonValueKind.Object } user) break;
                    ParsePlayer(id, player, user, at, effects);
                    var old = Joins.GetValueOrDefault(id);
                    var join = JoinFrom(player, old?.JoinTime, old?.HasInstantiated ?? true);
                    Joins[id] = join;
                    break;
                }
            case 255:
                {
                    if (Int(Get(p, "254")) is not { } id) break;
                    var player = Get(p, "249");
                    if (player is { } pl && Get(pl, "user") is { ValueKind: JsonValueKind.Object } user) ParsePlayer(id, pl, user, at, effects);
                    LobbyIds(Get(p, "252"));
                    var hasInstantiated = CurrentUser == id;
                    // The original read hasInstantiated off the lobby's user, which never has it.
                    if (Current.GetValueOrDefault(id) is not null) hasInstantiated = false;
                    if (Truthy(player))
                    {
                        Joins[id] = JoinFrom(player!.Value, at, hasInstantiated);
                        UserJoin(id, player.Value, at, effects);
                    }
                    break;
                }
            case 254:
                {
                    if (Int(Get(p, "254")) is not { } id) break;
                    UserLeave(id, at, now);
                    Current.Remove(id);
                    LastModeration.Remove(id);
                    Joins.Remove(id);
                    Event7.Remove(id);
                    LobbyIds(Get(p, "252"));
                    if (Int(Get(p, "203")) is { } master) SetMaster(master, at);
                    break;
                }
            case 4:
                SetMaster(Int(Get(p, "254")) ?? 0, at);
                break;
            case 33:
                Moderation(Get(p, "245"), at, effects);
                break;
            case 202:
                {
                    if (Int(Get(p, "254")) is not { } id) break;
                    Lobby.TryAdd(id, null);
                    Current.TryAdd(id, null);
                    if (Joins.TryGetValue(id, out var join)) join.HasInstantiated = true;
                    else Joins[id] = new PhotonJoin { JoinTime = at, HasInstantiated = true };
                    break;
                }
            case 43:
                {
                    var id = Int(Get(p, "254"));
                    var text = Str(Get(p, "245"));
                    if (id == CurrentUser || id is not { } photonId) break;
                    if (_lastChatBox.TryGetValue(photonId, out var last) && last == text) break;
                    _lastChatBox[photonId] = text ?? "";
                    var userId = UserIdOf(photonId);
                    if (settings.ChatboxUserBlacklist.ContainsKey(userId) || BlacklistedChatbox(text, settings.ChatboxBlacklist)) break;
                    Add(new PhotonFeedEntry { PhotonId = photonId, Text = text, Type = "ChatBoxMessage", CreatedAt = at });
                    effects.Add(new PhotonNoty("ChatBoxMessage", at, userId, DisplayNameOf(photonId), text));
                    break;
                }
            case 70:
                Portals(Get(p, "245"), at, effects);
                break;
            case 71:
                {
                    var id = Int(Get(p, "254"));
                    if (id == CurrentUser) break;
                    var emoji = Get(p, "245");
                    var kind = Int(Get(emoji, "0"));
                    string emojiName = "", imageUrl = "";
                    string? fileId = null;
                    if (kind == 0) emojiName = Int(Get(emoji, "2")) is { } e && e >= 0 && e < Emojis.Length ? Emojis[e] : "";
                    else if (kind == 1)
                    {
                        emojiName = "Custom";
                        fileId = Str(Get(emoji, "1"));
                        imageUrl = $"{ApiEndpoint}/file/{fileId}/1/";
                    }
                    Add(new PhotonFeedEntry { PhotonId = id, Text = emojiName, Type = "SpawnEmoji", CreatedAt = at, ImageUrl = imageUrl, FileId = fileId });
                    break;
                }
        }
    }

    private void SetUserProps(int id, JsonElement player, JsonElement user, DateTimeOffset at, DateTimeOffset now, List<PhotonEffect> effects)
    {
        ParsePlayer(id, player, user, at, effects);
        Joins[id] = JoinFrom(player, at, Joins.GetValueOrDefault(id)?.HasInstantiated ?? false);
        UserJoin(id, player, at, effects);
    }

    public static bool BlacklistedChatbox(string? text, IReadOnlyList<string> keywords) =>
        text is not null && keywords.Any(k => k.Length > 0 && text.Contains(k, StringComparison.Ordinal));

    private void Moderation(JsonElement? m, DateTimeOffset at, List<PhotonEffect> effects)
    {
        var kind = Int(Get(m, "0"));
        if (kind == 21)
        {
            if (Truthy(Get(m, "1")))
            {
                var id = Int(Get(m, "1")) ?? 0;
                ModerationFor(id, Truthy(Get(m, "10")), Truthy(Get(m, "11")), at, effects);
                return;
            }
            static List<int> Ids(JsonElement? e) => e is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray().Select(x => Int(x)).OfType<int>().ToList() : [];
            var blocks = Ids(Get(m, "10"));
            var mutes = Ids(Get(m, "11"));
            var list = new Dictionary<int, (bool Mute, bool Block)>();
            foreach (var id in blocks) list[id] = (mutes.Contains(id), true);
            foreach (var id in mutes) list.TryAdd(id, (true, false));
            foreach (var (id, (mute, block)) in list) ModerationFor(id, block, mute, at, effects);
        }
        else if (kind is 13 or 25)
        {
            var msg = Str(Get(m, "2"));
            if (msg is not null && Get(m, "14") is { ValueKind: JsonValueKind.Object } values)
                foreach (var v in values.EnumerateObject())
                {
                    var i = msg.IndexOf("{{" + v.Name + "}}", StringComparison.Ordinal);
                    if (i >= 0) msg = msg[..i] + (Str(v.Value) ?? v.Value.GetRawText()) + msg[(i + v.Name.Length + 4)..];
                }
            Add(new PhotonFeedEntry { Text = msg, Type = "Moderation", Color = "yellow", CreatedAt = at });
        }
    }

    private void ModerationFor(int id, bool block, bool mute, DateTimeOffset at, List<PhotonEffect> effects)
    {
        if (Lobby.GetValueOrDefault(id) is { Id: { } userId } user) effects.Add(new PhotonModeration(id, userId, user.DisplayName, block, mute, at));
        else _moderationQueue[id] = (block, mute, at);
    }

    private void Portals(JsonElement? e, DateTimeOffset at, List<PhotonEffect> effects)
    {
        var kind = Int(Get(e, "0"));
        var portalId = Str(Get(e, "1")) ?? "";
        switch (kind)
        {
            case 20 or 21:
                {
                    var userId = Str(Get(e, "2"));
                    var shortName = Str(Get(e, "5")) ?? "";
                    var worldName = kind == 20 ? Str(Get(Get(e, "8"), "name")) ?? "" : "";
                    effects.Add(new PhotonPortalSpawn(at, userId, shortName, worldName));
                    _portals[portalId] = new Portal { UserId = userId, ShortName = shortName, WorldName = worldName, CreatedAt = at };
                    break;
                }
            case 22:
                {
                    var text = "DeletedPortal";
                    if (_portals.TryGetValue(portalId, out var portal))
                        text = $"DeletedPortal after {TimeToText((at - portal.CreatedAt).TotalMilliseconds)} with {portal.PlayerCount} players to \"{portal.WorldName}\"";
                    Add(new PhotonFeedEntry { Text = text, Type = "DeletedPortal", CreatedAt = at });
                    _portals.Remove(portalId);
                    break;
                }
            case 23:
                if (_portals.TryGetValue(portalId, out var p))
                {
                    p.PendingLeave++;
                    p.PlayerCount = Long(Get(e, "3")) ?? 0;
                }
                break;
            case 24:
                Add(new PhotonFeedEntry { Text = "PortalError failed to create portal", Type = "DeletedPortal", CreatedAt = at });
                break;
        }
    }

    private void OnVrcEvent(JsonElement e, DateTimeOffset at, List<PhotonEffect> effects)
    {
        var sender = Int(Get(e, "Sender"));
        var name = Str(Get(e, "EventName"));
        var type = Int(Get(e, "EventType"));
        if (name == "_SendOnSpawn") return;
        if (type > 34)
        {
            effects.Add(new PhotonGameLogEvent(at, $"{DisplayNameOf(sender)} called non existent RPC {type}"));
            return;
        }
        if (type != 14) return;
        var kind = "Event";
        string? text;
        var data = Get(e, "Data");
        switch (name)
        {
            case "ChangeVisibility":
                text = Get(data, "0") switch { { ValueKind: JsonValueKind.True } => "EnableCamera", { ValueKind: JsonValueKind.False } => "DisableCamera", _ => null };
                kind = "Camera";
                break;
            case "PhotoCapture" or "TimerBloop":
                text = name;
                kind = "Camera";
                break;
            case "ReloadAvatarNetworkedRPC":
                text = "AvatarReset";
                break;
            case "ReleaseBones":
                text = "ResetPhysBones";
                break;
            case "SpawnEmojiRPC":
                text = null;
                kind = "SpawnEmoji";
                break;
            default:
                var args = data is { ValueKind: JsonValueKind.Array } d && d.GetArrayLength() > 0 ? " " + UnquoteKeys(d.GetRawText()) : "";
                text = $"{name}{args}";
                break;
        }
        Add(new PhotonFeedEntry { PhotonId = sender, Text = text, Type = kind, CreatedAt = at });
    }

    /// <summary>JSON.stringify(...).replace(/"([^(")"]+)":/g, '$1:').</summary>
    public static string UnquoteKeys(string json)
    {
        var compact = JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json));
        return System.Text.RegularExpressions.Regex.Replace(compact, "\"([^(\")\"]+)\":", "$1:");
    }

    private void LobbyIds(JsonElement? ids)
    {
        if (ids is not { ValueKind: JsonValueKind.Array } list) return;
        var all = list.EnumerateArray().Select(x => Int(x)).OfType<int>().ToHashSet();
        foreach (var id in all)
        {
            Lobby.TryAdd(id, null);
            Current.TryAdd(id, null);
        }
        foreach (var id in Current.Keys.ToList())
            if (!all.Contains(id))
            {
                Current.Remove(id);
                Event7.Remove(id);
            }
    }

    private void SetMaster(int id, DateTimeOffset at)
    {
        if (Master == id) return;
        if (Master != 0) Add(new PhotonFeedEntry { PhotonId = id, Text = "Photon Master Migrate", Type = "MasterMigrate", CreatedAt = at });
        Master = id;
    }

    private void ParseUser(int id, JsonElement user, DateTimeOffset at, List<PhotonEffect> effects)
    {
        var photonUser = new PhotonUser
        {
            Id = Str(Get(user, "id")),
            DisplayName = Str(Get(user, "displayName")),
            DeveloperType = Str(Get(user, "developerType")),
            ProfilePicOverride = Str(Get(user, "profilePicOverride")),
            CurrentAvatarImageUrl = Str(Get(user, "currentAvatarImageUrl")),
            CurrentAvatarThumbnailImageUrl = Str(Get(user, "currentAvatarThumbnailImageUrl")),
            UserIcon = Str(Get(user, "userIcon")),
            LastPlatform = Str(Get(user, "last_platform")),
            Status = Str(Get(user, "status")),
            StatusDescription = Str(Get(user, "statusDescription")),
            Tags = Get(user, "tags") is { ValueKind: JsonValueKind.Array } tags ? tags.EnumerateArray().Select(t => Str(t)).OfType<string>().ToList() : [],
        };
        Lobby[id] = photonUser;
        Current[id] = photonUser;
        // photonLobbyUserDataUpdate: a status change of someone else.
        if (_userData.TryGetValue(id, out var before) && id != CurrentUser && (photonUser.Status != before.Status || photonUser.StatusDescription != before.StatusDescription))
            Add(new PhotonFeedEntry
            {
                PhotonId = id, Type = "ChangeStatus", Status = photonUser.Status, PreviousStatus = before.Status,
                StatusDescription = ReplaceBioSymbols(photonUser.StatusDescription), PreviousStatusDescription = ReplaceBioSymbols(before.StatusDescription), CreatedAt = at,
            });
        _userData[id] = photonUser;
        if (photonUser.Id is null) return;
        effects.Add(new PhotonUserSeen(photonUser, at));
        if (_moderationQueue.Remove(id, out var queued)) effects.Add(new PhotonModeration(id, photonUser.Id, photonUser.DisplayName, queued.Block, queued.Mute, queued.At));
    }

    private void UserJoin(int id, JsonElement player, DateTimeOffset at, List<PhotonEffect> effects)
    {
        if (id == CurrentUser) return;
        var platform = Str(Get(player, "last_platform")) switch
        {
            "android" => "Android",
            "ios" => "iOS",
            _ => Bool(Get(player, "inVRMode")) == true ? "VR" : "Desktop",
        };
        // photonUserSusieCheck.
        var text = Get(player, "modTag") is { } modTag ? $"Moderator has joined {Str(modTag) ?? modTag.GetRawText()}"
            : Truthy(Get(player, "isInvisible")) ? "User joined invisible" : "";
        if (text.Length > 0)
        {
            Add(new PhotonFeedEntry { PhotonId = id, Text = text, Type = "Event", Color = "yellow", CreatedAt = at });
            effects.Add(new PhotonGameLogEvent(at, $"{text} - {DisplayNameOf(id)} ({UserIdOf(id)})"));
        }
        Add(new PhotonFeedEntry { PhotonId = id, Text = "has joined", Type = "OnPlayerJoined", CreatedAt = at, Avatar = AvatarOf(Get(player, "avatarDict")), Platform = platform });
    }

    private void UserLeave(int id, DateTimeOffset at, DateTimeOffset now)
    {
        if (!Current.ContainsKey(id)) return;
        var text = "has left";
        if (Event7.TryGetValue(id, out var last) && (now - last).TotalMilliseconds > 10_000) text = $"has timed out after {TimeToText((now - last).TotalMilliseconds)}";
        foreach (var portal in _portals.Values)
            if (portal.PendingLeave > 0)
            {
                text = $"has left through portal to \"{portal.WorldName}\"";
                portal.PendingLeave--;
            }
        Add(new PhotonFeedEntry { PhotonId = id, Text = text, Type = "OnPlayerLeft", CreatedAt = at });
    }

    private void AvatarChange(int id, JsonElement user, JsonElement avatarJson, DateTimeOffset at, List<PhotonEffect> effects)
    {
        var userId = Str(Get(user, "id"));
        if (userId is null) return;
        var avatar = AvatarOf(avatarJson)!;
        if (Avatars.TryGetValue(userId, out var old) && old != avatar.Id && id != CurrentUser)
        {
            var displayName = Str(Get(user, "displayName"));
            effects.Add(new PhotonNoty("AvatarChange", at, userId, displayName, avatar.Name, avatar));
            Add(new PhotonFeedEntry { PhotonId = id, DisplayName = displayName ?? "", UserId = userId, Text = $"ChangeAvatar {avatar.Name}", Type = "ChangeAvatar", CreatedAt = at, Avatar = avatar });
        }
        if (avatar.Id is not null) Avatars[userId] = avatar.Id;
    }

    private void GroupChange(int id, JsonElement user, JsonElement group, DateTimeOffset at, List<PhotonEffect> effects)
    {
        if (!Joins.TryGetValue(id, out var join) || !join.GroupKnown) return;
        var groupId = Str(group);
        if (join.GroupOnNameplate == groupId || id == CurrentUser) return;
        effects.Add(new PhotonGroupChange(new PhotonFeedEntry
        {
            PhotonId = id, DisplayName = Str(Get(user, "displayName")) ?? "", UserId = Str(Get(user, "id")) ?? "", Type = "ChangeGroup", CreatedAt = at,
            GroupId = groupId, PreviousGroupId = join.GroupOnNameplate,
        }));
    }

    /// <summary>photonLobbyWatcher: players whose movement stopped past the threshold, 70 seconds after they joined.</summary>
    public List<PhotonTimeout> Timeouts(DateTimeOffset now, int thresholdMs, int players)
    {
        if (Current.Count == 0 || LastEvent7List is not { } last || now > last.AddSeconds(1.5) || players <= 1) return [];
        var list = new List<PhotonTimeout>();
        foreach (var (id, seen) in Event7)
        {
            var since = (now - seen).TotalMilliseconds;
            if (since <= thresholdMs || id == CurrentUser) continue;
            if (Joins.GetValueOrDefault(id)?.JoinTime is { } joined && joined.AddSeconds(70) < now)
                list.Add(new PhotonTimeout(UserIdOf(id), DisplayNameOf(id), (int)Math.Round(since / 1000), since));
        }
        return list.OrderBy(t => t.RawTime).ToList();
    }
}

/// <summary>The account's Photon options the parser needs.</summary>
public sealed record PhotonSettings(IReadOnlyDictionary<string, string> ChatboxUserBlacklist, IReadOnlyList<string> ChatboxBlacklist);
