using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VRCX.Common.Services.Live;

/// <summary>A video a world's player started, as the original's mediaParsers made of a log line.</summary>
public sealed record MediaPlay(string VideoUrl, string? VideoId, string? VideoName, string? DisplayName, double? VideoPos = null, double? VideoLength = null);

/// <summary>
/// The original's gameLog/mediaParsers.js: YouTube ids in video URLs, and the "[VRCX] ..." lines dance
/// and cinema worlds write about what they play (VRDancing, LSMedia, PopcornPalace).
/// </summary>
public static partial class MediaParsers
{
    /// <summary>
    /// Worlds with their own video integration (the original's rpcWorlds): their plain video lines are
    /// skipped, since the world's own "[VRCX]" line names the video properly.
    /// </summary>
    public static readonly IReadOnlySet<string> RpcWorlds = new HashSet<string>(StringComparer.Ordinal)
    {
        "wrld_f20326da-f1ac-45fc-a062-609723b097b1",
        "wrld_42377cf1-c54f-45ed-8996-5875b0573a83",
        "wrld_dd6d2888-dbdc-47c2-bc98-3d631b2acd7c",
        "wrld_52bdcdab-11cd-4325-9655-0fb120846945",
        "wrld_2d40da63-8f1f-4011-8a9e-414eb8530acd",
        "wrld_10e5e467-fc65-42ed-8957-f02cace1398c",
        "wrld_04899f23-e182-4a8d-b2c7-2c74c7c15534",
        "wrld_435bbf25-f34f-4b8b-82c6-cd809057eb8e",
        "wrld_db9d878f-6e76-4776-8bf2-15bcdd7fc445",
        "wrld_f767d1c8-b249-4ecc-a56f-614e433682c8",
        "wrld_74970324-58e8-4239-a17b-2c59dfdf00db",
        "wrld_266523e8-9161-40da-acd0-6bd82e075833",
        "wrld_27c7e6b2-d938-447e-a270-3d1a873e2cf3",
    };

    public static bool IsRpcWorld(string? location) =>
        location is not null && RpcWorlds.Contains(location.Split(':')[0]);

    /// <summary>The YouTube video id in a URL (youtu.be, shorts, watch?v=, behind the usual proxies), or null.</summary>
    public static string? YouTubeId(string videoUrl)
    {
        try
        {
            var url = new Uri(videoUrl);
            var origin = url.GetLeftPart(UriPartial.Authority);
            if (origin is "https://t-ne.x0.to" or "https://nextnex.com" or "https://r.0cm.org"
                && System.Web.HttpUtility.ParseQueryString(url.Query)["url"] is { } inner)
                url = new Uri(inner);
            if (videoUrl.StartsWith("https://u2b.cx/", StringComparison.Ordinal)) url = new Uri(videoUrl[15..]);
            var path = url.AbsolutePath;
            var v = System.Web.HttpUtility.ParseQueryString(url.Query)["v"];
            string? id = null;
            if (path.Length == 12) id = path.Substring(1, 11); // https://youtu.be/
            if (path.Length == 19) id = path.Substring(8, 11); // https://www.youtube.com/shorts/
            if (v is { Length: 11 }) id = v;                    // watch?v=, music.youtube.com
            return id;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    /// <summary>A "[VRCX] ..." line's video, by the type word it starts with; null when it is not one.</summary>
    public static MediaPlay? ParseVrcxLine(string data)
    {
        var space = data.IndexOf(' ');
        if (space < 0) return null;
        return data[..space] switch
        {
            "VideoPlay(VRDancing)" => VrDancing(data),
            "LSMedia" => LsMedia(data),
            "VideoPlay(PopcornPalace)" => PopcornPalace(data),
            _ => null,
        };
    }

    [GeneratedRegex("VideoPlay\\(VRDancing\\) \"(.+?)\",([\\d.]+),([\\d.]+),(-?[\\d.]+),\"(.+?)\",\"(.+?)\"")]
    private static partial Regex VrDancingLine();

    private static MediaPlay? VrDancing(string data)
    {
        var m = VrDancingLine().Match(data);
        if (!m.Success) return null;
        var videoId = m.Groups[4].Value == "-1" ? "YouTube" : m.Groups[4].Value;
        var name = m.Groups[6].Value;
        var cut = name.IndexOf("]</b> ", StringComparison.Ordinal);
        if (cut != -1) name = name[(cut + 6)..];
        var (pos, length) = (Number(m.Groups[2].Value), Number(m.Groups[3].Value));
        // As the original: a position equal to the length means it has only just started.
        if (pos == length) pos = 0;
        // A YouTube one is named by the YouTube lookup instead, as the original does.
        return new MediaPlay(m.Groups[1].Value, videoId, videoId == "YouTube" ? null : name, Blank(m.Groups[5].Value), pos, length);
    }

    [GeneratedRegex(@"LSMedia ([\d.]+),([\d.]+),(.+?),(.+?),(?=[^,]*$)")]
    private static partial Regex LsMediaLine();

    private static MediaPlay? LsMedia(string data)
    {
        // [VRCX] LSMedia 0,6298.292,Natsumi-sama,The Outfit (2022), 1080p
        var m = LsMediaLine().Match(data);
        if (!m.Success) return null;
        var name = VrchatText.ReplaceBioSymbols(m.Groups[4].Value);
        return new MediaPlay(name, "LSMedia", name, Blank(m.Groups[3].Value), Number(m.Groups[1].Value), Number(m.Groups[2].Value));
    }

    private static MediaPlay? PopcornPalace(string data)
    {
        // [VRCX] VideoPlay(PopcornPalace) {"videoName": "...", "videoPos": 37.2, "videoLength": 11474.05, "displayName": "...", ...}
        var brace = data.IndexOf('{');
        if (brace < 0) return null;
        try
        {
            using var json = JsonDocument.Parse(data[brace..]);
            var root = json.RootElement;
            var name = root.TryGetProperty("videoName", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            // An empty name means playback stopped: nothing to store.
            if (string.IsNullOrEmpty(name)) return null;
            var who = root.TryGetProperty("displayName", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            double? Read(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
            return new MediaPlay(name, "PopcornPalace", name, Blank(who), Read("videoPos"), Read("videoLength"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Blank(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static double? Number(string s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>
    /// The original's convertYoutubeTime: an ISO 8601 duration from the YouTube API ("PT1H2M3S") in
    /// seconds; 0 for anything it can't read.
    /// </summary>
    public static double YouTubeSeconds(string? duration)
    {
        if (string.IsNullOrEmpty(duration)) return 0;
        try
        {
            return System.Xml.XmlConvert.ToTimeSpan(duration).TotalSeconds;
        }
        catch (FormatException)
        {
            return 0;
        }
    }
}

/// <summary>A YouTube video's title from the YouTube Data API, as the original's lookupYouTubeVideo.</summary>
public sealed class YouTubeLookup(IHttpClientFactory http, Microsoft.Extensions.Logging.ILogger<YouTubeLookup> logger)
{
    public const string ClientName = "youtube";

    public async Task<YouTubeVideo?> LookupAsync(string videoId, string apiKey, CancellationToken ct)
    {
        var url = $"https://www.googleapis.com/youtube/v3/videos?id={Uri.EscapeDataString(videoId)}&part=snippet,contentDetails&key={Uri.EscapeDataString(apiKey)}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // The original's key restriction: keys are commonly limited to this referrer.
            request.Headers.Referrer = new Uri("https://vrcx.app");
            using var response = await http.CreateClient(ClientName).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("YouTube lookup for {VideoId} answered {Status}", videoId, (int)response.StatusCode);
                return null;
            }
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var items = json.RootElement.GetProperty("items");
            if (items.GetArrayLength() == 0 || items[0].GetProperty("snippet").GetProperty("title").GetString() is not { } title) return null;
            var duration = items[0].TryGetProperty("contentDetails", out var details) && details.TryGetProperty("duration", out var d) ? d.GetString() : null;
            var seconds = MediaParsers.YouTubeSeconds(duration);
            return new YouTubeVideo(title, seconds > 0 ? seconds : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "YouTube lookup for {VideoId} failed", videoId);
            return null;
        }
    }
}

/// <summary>A YouTube video's title and length in seconds (null when the API leaves it out).</summary>
public sealed record YouTubeVideo(string Title, double? Length);
