namespace VRCX.Vrchat;

/// <summary>
/// A parsed VRChat location tag: "wrld_...:12345~hidden(usr_...)~region(eu)~nonce(...)",
/// or one of the sentinel values "offline", "private", "traveling".
/// </summary>
public sealed record VrchatLocation
{
    public required string Tag { get; init; }
    public bool IsOffline { get; init; }
    public bool IsPrivate { get; init; }
    public bool IsTraveling { get; init; }
    /// <summary>"wrld_..." or "" for the sentinels and local test worlds.</summary>
    public string WorldId { get; init; } = "";
    /// <summary>The part after the colon, with any "&amp;shortName=" stripped; "" when there is none.</summary>
    public string InstanceId { get; init; } = "";
    /// <summary>The numeric/alphanumeric instance name before the first "~".</summary>
    public string InstanceName { get; init; } = "";
    /// <summary>public, hidden (friends+), friends, private (invite/invite+), group.</summary>
    public string AccessType { get; init; } = "public";
    /// <summary>Friendlier label: Public, Friends+, Friends, Invite+, Invite, Group Public, Group+, Group.</summary>
    public string AccessTypeName { get; init; } = "Public";
    /// <summary>The instance owner's user id (hidden/friends/private instances).</summary>
    public string? OwnerUserId { get; init; }
    public string? GroupId { get; init; }
    /// <summary>public, plus or members - only for group instances.</summary>
    public string? GroupAccessType { get; init; }
    public string? Region { get; init; }
    public string? Nonce { get; init; }
    public bool CanRequestInvite { get; init; }
    public bool AgeGate { get; init; }
    public bool Strict { get; init; }
    public string? ShortName { get; init; }

    /// <summary>Location + instance: "wrld_x:1~..." - the id an Instance row is keyed on. "" for sentinels.</summary>
    public string InstanceLocation => WorldId.Length > 0 && InstanceId.Length > 0 ? $"{WorldId}:{InstanceId}" : "";

    /// <summary>True for a joinable world instance (not offline/private/traveling/local, has an instance part).</summary>
    public bool IsRealInstance => InstanceLocation.Length > 0;

    public static VrchatLocation Parse(string? tag)
    {
        tag ??= "";
        switch (tag)
        {
            case "" or "offline" or "offline:offline":
                return new VrchatLocation { Tag = tag, IsOffline = true };
            case "private" or "private:private":
                return new VrchatLocation { Tag = tag, IsPrivate = true };
            case "traveling" or "traveling:traveling":
                return new VrchatLocation { Tag = tag, IsTraveling = true };
        }
        if (tag.StartsWith("local", StringComparison.Ordinal))
            return new VrchatLocation { Tag = tag };

        string? shortName = null;
        var shortNameIndex = tag.IndexOf("&shortName=", StringComparison.Ordinal);
        if (shortNameIndex >= 0)
        {
            shortName = tag[(shortNameIndex + "&shortName=".Length)..];
            tag = tag[..shortNameIndex];
        }

        var colon = tag.IndexOf(':');
        if (colon < 0)
            return new VrchatLocation { Tag = tag, WorldId = tag, ShortName = shortName };

        var worldId = tag[..colon];
        var instanceId = tag[(colon + 1)..];
        var segments = instanceId.Split('~');
        var instanceName = segments[0];

        string? ownerUserId = null, groupId = null, groupAccessType = null, region = null, nonce = null;
        var accessType = "public";
        var canRequestInvite = false;
        var ageGate = false;
        var strict = false;
        foreach (var segment in segments.Skip(1))
        {
            var open = segment.IndexOf('(');
            var close = open >= 0 ? segment.LastIndexOf(')') : -1;
            var key = open >= 0 ? segment[..open] : segment;
            var value = open >= 0 && close > open ? segment[(open + 1)..close] : "";
            switch (key)
            {
                case "hidden": accessType = "hidden"; ownerUserId = value; break;
                case "friends": accessType = "friends"; ownerUserId = value; break;
                case "private": accessType = "private"; ownerUserId = value; break;
                case "group": accessType = "group"; groupId = value; break;
                case "groupAccessType": groupAccessType = value; break;
                case "canRequestInvite": canRequestInvite = true; break;
                case "ageGate": ageGate = true; break;
                case "strict": strict = true; break;
                case "region": region = value; break;
                case "nonce": nonce = value; break;
            }
        }

        var accessTypeName = accessType switch
        {
            "hidden" => "Friends+",
            "friends" => "Friends",
            "private" => canRequestInvite ? "Invite+" : "Invite",
            "group" => groupAccessType switch
            {
                "public" => "Group Public",
                "plus" => "Group+",
                _ => "Group",
            },
            _ => "Public",
        };

        return new VrchatLocation
        {
            Tag = tag,
            WorldId = worldId,
            InstanceId = instanceId,
            InstanceName = instanceName,
            AccessType = accessType,
            AccessTypeName = accessTypeName,
            OwnerUserId = ownerUserId,
            GroupId = groupId,
            GroupAccessType = groupAccessType,
            Region = region,
            Nonce = nonce,
            CanRequestInvite = canRequestInvite,
            AgeGate = ageGate,
            Strict = strict,
            ShortName = shortName,
        };
    }
}
