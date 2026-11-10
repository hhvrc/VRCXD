using NpgsqlTypes;
using VRCX.Common.Utils;

namespace VRCX.Common.VrcxDb;

// Postgres-native enums, registered with the model through NpgsqlEnumExtensions.
// Only vocabularies we own or that VRChat has kept fixed for years live here;
// anything VRChat extends without notice (status "join me", membership status,
// release status, developer type) stays text so a new value never breaks ingestion.

/// <summary>VRChat's user state: in game, on the website/app, or gone.</summary>
[PgEnum(Name = "user_state")]
public enum UserState
{
    [PgName("offline")] Offline = 0,
    [PgName("active")] Active = 1,
    [PgName("online")] Online = 2,
}

/// <summary>What kind of presence transition a presence_events row records.</summary>
[PgEnum(Name = "presence_kind")]
public enum PresenceKind
{
    [PgName("online")] Online = 0,
    [PgName("active")] Active = 1,
    [PgName("offline")] Offline = 2,
    [PgName("location")] Location = 3,
}

/// <summary>Where an instance_visits row came from.</summary>
[PgEnum(Name = "visit_source")]
public enum VisitSource
{
    /// <summary>friend-location / friend-online over the pipeline websocket.</summary>
    [PgName("pipeline")] Pipeline = 0,
    /// <summary>OnPlayerJoined/Left in the local game log, shipped by the desktop app.</summary>
    [PgName("game_log")] GameLog = 1,
    /// <summary>The users list of a GET instances/… response.</summary>
    [PgName("api")] Api = 2,
}

/// <summary>VRChat favorite kinds.</summary>
[PgEnum(Name = "favorite_type")]
public enum FavoriteType
{
    [PgName("friend")] Friend = 0,
    [PgName("world")] World = 1,
    [PgName("avatar")] Avatar = 2,
}

/// <summary>An instance's access type, as encoded in its location tag.</summary>
[PgEnum(Name = "instance_access_type")]
public enum InstanceAccessType
{
    [PgName("public")] Public = 0,
    /// <summary>Friends+ ("hidden" in the tag).</summary>
    [PgName("hidden")] Hidden = 1,
    [PgName("friends")] Friends = 2,
    /// <summary>Invite / Invite+.</summary>
    [PgName("private")] Private = 3,
    [PgName("group")] Group = 4,
}

/// <summary>String ↔ enum for the values VRChat sends on the wire.</summary>
public static class VrcxEnums
{
    public static UserState ParseUserState(string? value) => value switch
    {
        "online" => UserState.Online,
        "active" => UserState.Active,
        _ => UserState.Offline,
    };

    public static FavoriteType? TryParseFavoriteType(string? value) => value switch
    {
        "friend" => FavoriteType.Friend,
        "world" or "vrcPlusWorld" => FavoriteType.World,
        "avatar" => FavoriteType.Avatar,
        _ => null,
    };

    public static InstanceAccessType ParseInstanceAccessType(string? value) => value switch
    {
        "hidden" => InstanceAccessType.Hidden,
        "friends" => InstanceAccessType.Friends,
        "private" => InstanceAccessType.Private,
        "group" => InstanceAccessType.Group,
        _ => InstanceAccessType.Public,
    };
}
