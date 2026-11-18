namespace VRCX.API.Controller.Groups;

/// <summary>How many groups an account may own and join, from VRChat's config.</summary>
public sealed class GroupLimits
{
    public int MaxOwned { get; init; } = 5;
    public int MaxJoined { get; init; } = 100;
    public int MaxJoinedPlus { get; init; } = 200;

    public static GroupLimits FromConfig(System.Text.Json.JsonElement config)
    {
        var defaults = new GroupLimits();
        if (config.ValueKind != System.Text.Json.JsonValueKind.Object
            || !config.TryGetProperty("constants", out var constants) || constants.ValueKind != System.Text.Json.JsonValueKind.Object
            || !constants.TryGetProperty("GROUPS", out var groups) || groups.ValueKind != System.Text.Json.JsonValueKind.Object)
            return defaults;
        int Read(string name, int fallback) => groups.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) && n > 0 ? n : fallback;
        return new GroupLimits
        {
            MaxOwned = Read("MAX_OWNED", defaults.MaxOwned),
            MaxJoined = Read("MAX_JOINED", defaults.MaxJoined),
            MaxJoinedPlus = Read("MAX_JOINED_PLUS", defaults.MaxJoinedPlus),
        };
    }
}
