using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VRCX.Vrchat;

/// <summary>The one JsonSerializerOptions every VRChat payload goes through.</summary>
public static class VrchatJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        // VRChat omits fields it would rather not send, and models treat
        // missing and null the same - so missing is never an error here.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        // Bodies are written as the original's JSON.stringify writes them: "+" in a base64 MD5, say,
        // stays "+" rather than becoming +. Nothing here ends up in HTML.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new LenientDateTimeOffsetConverter(),
            new LenientDateOnlyConverter(),
            new LenientStringConverter(),
            new LenientBoolConverter(),
        },
    };

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    public static T? Deserialize<T>(ReadOnlySpan<byte> json) => JsonSerializer.Deserialize<T>(json, Options);
    public static T? Deserialize<T>(JsonElement element) => element.Deserialize<T>(Options);
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}

// VRChat sends "" (and occasionally "none") where a timestamp is unknown -
// last_login for users who hide it, date_joined on some legacy accounts.
// The stock converter throws on that; these read it as null.
internal sealed class LenientDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null) return null;
        if (reader.TokenType is not JsonTokenType.String) { reader.Skip(); return null; }
        if (reader.TryGetDateTimeOffset(out var value)) return value;
        var text = reader.GetString();
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value);
    }
}

internal sealed class LenientDateOnlyConverter : JsonConverter<DateOnly?>
{
    public override DateOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null) return null;
        if (reader.TokenType is not JsonTokenType.String) { reader.Skip(); return null; }
        var text = reader.GetString();
        if (string.IsNullOrEmpty(text)) return null;
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        // Some endpoints send a full timestamp where others send a date.
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var stamp)
            ? DateOnly.FromDateTime(stamp.UtcDateTime)
            : null;
    }

    public override void Write(Utf8JsonWriter writer, DateOnly? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}

// A handful of nominally-string fields arrive as numbers or booleans from
// time to time (clientNumber, some ids in older notifications). Read any
// scalar as its text rather than failing the whole object.
internal sealed class LenientStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString(CultureInfo.InvariantCulture) : reader.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => SkipAndNull(ref reader),
        };

    private static string? SkipAndNull(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

// Some nominally-boolean fields arrive as numbers (notifications' expiryAfterSeen is a count
// of seconds on the wire) or as the strings "true"/"false". Nonzero and "true" read as true.
internal sealed class LenientBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number => reader.TryGetDouble(out var number) && number != 0,
            JsonTokenType.String => string.Equals(reader.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => SkipAndFalse(ref reader),
        };

    private static bool SkipAndFalse(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return false;
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}

// avatars/favorites sometimes sends unityPackages as something other than a list; the cards only need the
// rest of the avatar, so anything but an array reads as empty.
internal sealed class LenientListConverter<T> : JsonConverter<IReadOnlyList<T>>
{
    public override IReadOnlyList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return [];
        }
        return JsonSerializer.Deserialize<List<T>>(ref reader, options) ?? [];
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.ToList(), options);
}
