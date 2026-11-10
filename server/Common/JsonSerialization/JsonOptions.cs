using System.Text.Json;
using System.Text.Json.Serialization;

namespace VRCX.Common.JsonSerialization;

/// <summary>The JSON shape of the API (not of VRChat's payloads - that is <see cref="Vrchat.VrchatJson"/>).</summary>
public static class JsonOptions
{
    static JsonOptions()
    {
        ConfigureDefault(Default);
    }

    public static void ConfigureDefault(JsonSerializerOptions options)
    {
        options.PropertyNameCaseInsensitive = true;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public static readonly JsonSerializerOptions Default = new();
}
