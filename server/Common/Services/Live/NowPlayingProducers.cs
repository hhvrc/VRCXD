using System.Text.Json;
using System.Text.RegularExpressions;

namespace VRCX.Common.Services.Live;

/// <summary>What the ingestor does for one nowplaying line.</summary>
public abstract record NowPlayingEffect;

/// <summary>addGameLogVideo: a producer's unnamed URL, stored and named like any video line.</summary>
public sealed record NowPlayingAddVideo(string VideoUrl, string? OwnerName, string? MediaId) : NowPlayingEffect;

/// <summary>setNowPlaying: a named (or hidden) media, stored as a VideoPlay row (without its URL when hidden).</summary>
public sealed record NowPlayingSet(string VideoUrl, string VideoId, string VideoName, double VideoLength, string? OwnerName, bool UrlHidden) : NowPlayingEffect;

/// <summary>updateNowPlayingVideoName: names the stored row when it was logged without one.</summary>
public sealed record NowPlayingRename(string VideoName) : NowPlayingEffect;

/// <summary>applyTiming: the followed media's state for the status bar.</summary>
public sealed record NowPlayingTiming(string VideoUrl, string Name, string ThumbnailUrl, double Length, double Speed, bool Paused, double Position, double At, bool UrlHidden) : NowPlayingEffect;

/// <summary>clearNowPlaying.</summary>
public sealed record NowPlayingClear : NowPlayingEffect;

/// <summary>
/// The original's createNowPlayingSpecHandler (https://protv.dev/guides/nowplaying): each producer's
/// media and clock, following the high-priority one, else the one changed last. One per PC session;
/// <see cref="Reset"/> on every instance change and when the game stops, as the original did.
/// </summary>
public sealed class NowPlayingProducers
{
    private sealed class Producer(string id)
    {
        public string Id { get; } = id;
        public bool HasMedia;
        public string Url = "", PublishedUrl = "", Name = "", Image = "", OwnerName = "", MediaId = "";
        public double Length, Speed = 1, AnchorPos, AnchorWall;
        public bool HideUrl, HighPriority, Paused, Stopped, Loading;
        public long LastChange;
    }

    private readonly Dictionary<string, Producer> producers = new(StringComparer.Ordinal);
    private string followedId = "";
    private long changeSequence;

    // The original's nowPlaying ref, as far as the handler reads it.
    private string npUrl = "", npName = "", npVideoName = "", npThumbnail = "";
    private double npLength;
    private bool npSpec;

    public bool HasProducer => producers.Count > 0;

    /// <summary>resetNowPlayingProducers with clearNowPlaying.</summary>
    public void Reset()
    {
        producers.Clear();
        followedId = "";
        ClearState();
    }

    private void ClearState()
    {
        (npUrl, npName, npVideoName, npThumbnail, npLength, npSpec) = ("", "", "", "", 0, false);
    }

    public List<NowPlayingEffect> Apply(string data, DateTimeOffset dt)
    {
        var effects = new List<NowPlayingEffect>();
        JsonElement e;
        try
        {
            e = JsonDocument.Parse(data).RootElement;
        }
        catch (JsonException)
        {
            return effects;
        }
        if (e.ValueKind != JsonValueKind.Object || Str(e, "id") is not { Length: > 0 } id || Str(e, "type") is not { } type) return effects;
        var now = dt.ToUnixTimeMilliseconds() / 1000.0;
        if (!producers.TryGetValue(id, out var producer)) producers[id] = producer = new Producer(id);

        switch (type)
        {
            case "mediaChange":
                {
                    var url = Str(e, "mediaUrl") is { } u ? DecodeUri(u) : "";
                    producer.HasMedia = true;
                    producer.Url = url;
                    producer.Name = Str(e, "mediaName") ?? "";
                    producer.Image = MediaImage(Str(e, "mediaImage"), url);
                    producer.OwnerName = Str(e, "mediaOwnerName") ?? "";
                    producer.MediaId = MediaId(e);
                    producer.Length = Num(e, "mediaLength") is > 0 and var l ? l : 0;
                    producer.Speed = Num(e, "mediaSpeed") is > 0 and var s ? s : 1;
                    producer.HideUrl = Bool(e, "hideUrl");
                    producer.HighPriority = Bool(e, "highPriority");
                    producer.AnchorPos = 0;
                    producer.AnchorWall = now;
                    producer.Paused = producer.Stopped = producer.Loading = false;
                    producer.LastChange = ++changeSequence;
                    Sync(producer, true, now, effects);
                    return effects;
                }
            case "mediaInfo":
                if (Num(e, "mediaSpeed") is > 0 and var speed)
                {
                    SetAnchor(producer, now, null);
                    producer.Speed = speed;
                }
                var name = Str(e, "mediaName");
                if (name is not null) producer.Name = name;
                var image = Str(e, "mediaImage");
                if (image is not null) producer.Image = MediaImage(image, producer.Url);
                var (hasName, hasImage) = (name is not null, image is not null);
                if (producer.Id != followedId) return effects;
                if (producer.PublishedUrl.Length == 0)
                {
                    if (producer.Name.Length > 0)
                    {
                        Publish(producer, effects);
                        ApplyTiming(producer, now, effects);
                    }
                    return effects;
                }
                if (npSpec)
                {
                    if (hasName || hasImage)
                    {
                        npName = producer.Name + (producer.OwnerName.Length > 0 ? $" ({producer.OwnerName})" : "");
                        npVideoName = producer.Name;
                        npThumbnail = producer.Image;
                        if (producer.Name.Length > 0) effects.Add(new NowPlayingRename(producer.Name));
                    }
                    ApplyTiming(producer, now, effects);
                }
                return effects;
            case "mediaLoading":
                producer.Loading = Bool(e, "loading");
                return effects;
            case "mediaError":
                producer.Loading = false;
                return effects;
            case "mediaPlay":
                SetAnchor(producer, now, Num(e, "mediaTime"));
                producer.Paused = producer.Stopped = producer.Loading = false;
                break;
            case "mediaPause":
                SetAnchor(producer, now, Num(e, "mediaTime"));
                producer.Paused = true;
                producer.Loading = false;
                break;
            case "mediaSeek":
                SetAnchor(producer, now, Num(e, "mediaTime"));
                break;
            case "mediaStop":
                SetAnchor(producer, now, null);
                producer.Stopped = true;
                producer.Loading = false;
                break;
            default:
                return effects;
        }
        Sync(producer, false, now, effects);
        return effects;
    }

    private static double Position(Producer p, double now)
    {
        if (p.Paused || p.Stopped) return p.AnchorPos;
        var pos = p.AnchorPos + (now - p.AnchorWall) * p.Speed;
        return p.Length > 0 ? Math.Min(pos, p.Length) : pos;
    }

    private static void SetAnchor(Producer p, double now, double? mediaTime)
    {
        p.AnchorPos = mediaTime is { } t ? Math.Max(0, t) : Position(p, now);
        p.AnchorWall = now;
    }

    private Producer? PickFollowed()
    {
        Producer? best = null;
        foreach (var p in producers.Values)
        {
            if (!p.HasMedia || p.Stopped) continue;
            if (best is null || (p.HighPriority && !best.HighPriority) || (p.HighPriority == best.HighPriority && p.LastChange > best.LastChange))
                best = p;
        }
        return best;
    }

    private void Publish(Producer p, List<NowPlayingEffect> effects)
    {
        var videoUrl = p.HideUrl ? p.Name : p.Url;
        p.PublishedUrl = videoUrl;
        if (videoUrl.Length == 0)
        {
            if (npSpec)
            {
                ClearState();
                effects.Add(new NowPlayingClear());
            }
            return;
        }
        if (npUrl == videoUrl)
        {
            if (p.Name.Length > 0)
            {
                npName = p.Name + (p.OwnerName.Length > 0 ? $" ({p.OwnerName})" : "");
                npVideoName = p.Name;
                effects.Add(new NowPlayingRename(p.Name));
            }
            npThumbnail = p.Image;
            return;
        }
        if (p.Name.Length == 0 && !p.HideUrl)
        {
            (npUrl, npName, npVideoName, npThumbnail, npLength, npSpec) = (videoUrl, "", "", "", 0, false);
            effects.Add(new NowPlayingAddVideo(videoUrl, Blank(p.OwnerName), Blank(p.MediaId)));
            return;
        }
        var videoId = p.MediaId.Length > 0 ? p.MediaId : p.Id.Split('-')[0];
        effects.Add(new NowPlayingSet(videoUrl, videoId, p.Name, p.Length, Blank(p.OwnerName), p.HideUrl));
        var named = p.Name.Length > 0 ? p.Name + (p.OwnerName.Length > 0 ? $" ({p.OwnerName})" : "") : "";
        (npUrl, npName, npVideoName, npThumbnail, npLength, npSpec) = (videoUrl, named, p.Name, p.Image, p.Length, false);
    }

    private void ApplyTiming(Producer p, double now, List<NowPlayingEffect> effects)
    {
        if (npUrl.Length == 0 || npUrl != p.PublishedUrl) return;
        npSpec = true;
        if (p.Length > 0) npLength = p.Length;
        effects.Add(new NowPlayingTiming(npUrl, npName, npThumbnail, npLength, p.Speed > 0 ? p.Speed : 1, p.Paused, Position(p, now), now, p.HideUrl));
    }

    private void Sync(Producer changed, bool isMediaChange, double now, List<NowPlayingEffect> effects)
    {
        var followed = PickFollowed();
        if (followed is null)
        {
            if (followedId.Length > 0 && npSpec)
            {
                ClearState();
                effects.Add(new NowPlayingClear());
            }
            followedId = "";
            return;
        }
        var switched = followed.Id != followedId;
        if (switched || (isMediaChange && followed == changed))
        {
            followedId = followed.Id;
            Publish(followed, effects);
        }
        if (switched || followed == changed || isMediaChange) ApplyTiming(followed, now, effects);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? Blank(string s) => s.Length == 0 ? null : s;

    /// <summary>parseMediaId: a trimmed string, or a finite number as text.</summary>
    private static string MediaId(JsonElement e)
    {
        if (!e.TryGetProperty("mediaId", out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString()!.Trim(),
            JsonValueKind.Number when v.TryGetDouble(out var d) && double.IsFinite(d) => v.GetRawText(),
            _ => "",
        };
    }

    /// <summary>getMediaImage: the producer's image, else YouTube's thumbnail for a YouTube URL.</summary>
    public static string MediaImage(string? image, string url)
    {
        if (!string.IsNullOrEmpty(image)) return image;
        var id = YouTubeVideoId(url);
        return id.Length > 0 ? $"https://i.ytimg.com/vi/{id}/hqdefault.jpg" : "";
    }

    private static readonly Regex VideoIdPattern = new(@"^[\w-]{11}$", RegexOptions.CultureInvariant);

    /// <summary>getYouTubeVideoId: youtu.be/ID, youtube.com/watch?v=ID and /shorts, /live, /embed.</summary>
    public static string YouTubeVideoId(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return "";
        var host = Regex.Replace(parsed.Host, @"^(www|m|music)\.", "");
        var parts = parsed.AbsolutePath.Split('/');
        string? id = null;
        if (host == "youtu.be") id = parts.ElementAtOrDefault(1);
        else if (host == "youtube.com")
        {
            var section = parts.ElementAtOrDefault(1);
            if (section == "watch") id = System.Web.HttpUtility.ParseQueryString(parsed.Query)["v"];
            else if (section is "shorts" or "live" or "embed") id = parts.ElementAtOrDefault(2);
        }
        return id is not null && VideoIdPattern.IsMatch(id) ? id : "";
    }

    /// <summary>decodeURI, falling back to the URL as given.</summary>
    private static string DecodeUri(string url)
    {
        try
        {
            return GameLogIngestor.DecodeUri(url);
        }
        catch (UriFormatException)
        {
            return url;
        }
    }
}
