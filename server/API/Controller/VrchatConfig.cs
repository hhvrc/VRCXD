using System.Text.Json;

namespace VRCX.API.Controller;

/// <summary>What the endpoints read out of VRChat's config (GET config), which several features show parts of.</summary>
internal static class VrchatConfig
{
    /// <summary>VRChat's spoken language options, code to name: the original's subsetOfLanguages. Empty when the config has none.</summary>
    public static Dictionary<string, string> SpokenLanguages(JsonElement config)
    {
        var options = new Dictionary<string, string>();
        if (config.ValueKind == JsonValueKind.Object
            && config.TryGetProperty("constants", out var constants) && constants.ValueKind == JsonValueKind.Object
            && constants.TryGetProperty("LANGUAGE", out var language) && language.ValueKind == JsonValueKind.Object
            && language.TryGetProperty("SPOKEN_LANGUAGE_OPTIONS", out var spoken) && spoken.ValueKind == JsonValueKind.Object)
        {
            foreach (var option in spoken.EnumerateObject())
                if (option.Value.ValueKind == JsonValueKind.String) options[option.Name] = option.Value.GetString()!;
        }
        return options;
    }
}
