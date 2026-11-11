using System.Collections.Concurrent;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VRCX.Common.Live;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.Common.Services.Live;

/// <summary>What the presence is made from: the PC's instance and what the server knows about it.</summary>
public sealed record PresenceInput(
    VrchatLocation Location,
    DateTimeOffset? JoinedAt,
    string? WorldName,
    string? WorldThumbnail,
    int WorldCapacity,
    bool WorldPublic,
    string? GroupName,
    int Players,
    bool GameNoVr,
    string? Status,
    string? VideoName,
    DateTimeOffset? VideoStartedAt,
    double VideoLength);

/// <summary>
/// The original's Discord Rich Presence (discordPresence.js updateDiscord, shown by Discord.cs):
/// built here from the PC's game log session and sent to its agent, which talks to Discord. It is
/// rebuilt when the log, the game's state or the settings change, and every 30 seconds for the
/// status and the video; only a change is sent.
/// </summary>
public sealed class DiscordPresenceService(
    IDbContextFactory<VrcxContext> dbFactory,
    DesktopClients desktops,
    ILogger<DiscordPresenceService> logger) : BackgroundService
{
    public const string VrchatAppId = "883308884863901717";

    /// <summary>A world with its own Discord app: how it shows a video.</summary>
    public sealed record Rpc(int ActivityType, int StatusDisplayType, string AppId, string BigIcon);

    /// <summary>RPC_WORLD_CONFIGS: the dance and cinema worlds with their own Discord apps.</summary>
    private static readonly Dictionary<string, Rpc> RpcWorlds = new()
    {
        ["wrld_f20326da-f1ac-45fc-a062-609723b097b1"] = new(2, 2, "784094509008551956", "pypy"),
        ["wrld_10e5e467-fc65-42ed-8957-f02cace1398c"] = new(2, 2, "784094509008551956", "pypy"),
        ["wrld_04899f23-e182-4a8d-b2c7-2c74c7c15534"] = new(2, 2, "784094509008551956", "pypy"),
        ["wrld_42377cf1-c54f-45ed-8996-5875b0573a83"] = new(2, 2, "846232616054030376", "vr_dancing"),
        ["wrld_dd6d2888-dbdc-47c2-bc98-3d631b2acd7c"] = new(2, 2, "846232616054030376", "vr_dancing"),
        ["wrld_52bdcdab-11cd-4325-9655-0fb120846945"] = new(2, 2, "939473404808007731", "zuwa_zuwa_dance"),
        ["wrld_2d40da63-8f1f-4011-8a9e-414eb8530acd"] = new(2, 2, "939473404808007731", "zuwa_zuwa_dance"),
        ["wrld_74970324-58e8-4239-a17b-2c59dfdf00db"] = new(3, 2, "968292722391785512", "ls_media"),
        ["wrld_db9d878f-6e76-4776-8bf2-15bcdd7fc445"] = new(3, 2, "968292722391785512", "ls_media"),
        ["wrld_435bbf25-f34f-4b8b-82c6-cd809057eb8e"] = new(3, 2, "968292722391785512", "ls_media"),
        ["wrld_f767d1c8-b249-4ecc-a56f-614e433682c8"] = new(3, 2, "968292722391785512", "ls_media"),
        ["wrld_266523e8-9161-40da-acd0-6bd82e075833"] = new(3, 2, "1095440531821170820", "popcorn_palace"),
        ["wrld_27c7e6b2-d938-447e-a270-3d1a873e2cf3"] = new(3, 2, "1095440531821170820", "popcorn_palace"),
    };

    /// <summary>getRpcWorldConfig.</summary>
    public static Rpc? RpcWorldOf(string worldId) => RpcWorlds.GetValueOrDefault(worldId);

    private sealed record Tracked(string? AccountId, string? Location);

    private readonly ConcurrentDictionary<string, Tracked> _clients = new();
    private readonly ConcurrentDictionary<string, DiscordPresence> _sent = new();

    /// <summary>getLaunchURL.</summary>
    public static string LaunchUrl(VrchatLocation l) =>
        l.InstanceId.Length == 0 ? $"https://vrchat.com/home/launch?worldId={Uri.EscapeDataString(l.WorldId)}"
        : l.ShortName is { Length: > 0 } s ? $"https://vrchat.com/home/launch?worldId={Uri.EscapeDataString(l.WorldId)}&instanceId={Uri.EscapeDataString(l.InstanceId)}&shortName={Uri.EscapeDataString(s)}"
        : $"https://vrchat.com/home/launch?worldId={Uri.EscapeDataString(l.WorldId)}&instanceId={Uri.EscapeDataString(l.InstanceId)}";

    /// <summary>getStatusInfo.</summary>
    public static (string Name, string Image, bool HidePrivate) StatusInfo(string? status, bool hideInvite) => status switch
    {
        "active" => ("Active", "active", false),
        "join me" => ("Join Me", "joinme", false),
        "ask me" => ("Ask Me", "askme", hideInvite),
        "busy" => ("Do Not Disturb", "busy", true),
        _ => ("Offline", "offline", true),
    };

    /// <summary>Discord.LimitByteLength: at most that many UTF-8 bytes, whole characters only.</summary>
    public static string LimitBytes(string text, int max)
    {
        if (Encoding.UTF8.GetByteCount(text) <= max) return text;
        var end = text.Length;
        while (end > 0 && Encoding.UTF8.GetByteCount(text.AsSpan(0, end)) > max) end--;
        if (end > 0 && char.IsHighSurrogate(text[end - 1])) end--;
        return text[..end];
    }

    /// <summary>updateDiscord: the presence for this instance, or an inactive one to clear it.</summary>
    public static DiscordPresence Build(PresenceInput input, AccountSettings settings, DateTimeOffset now)
    {
        var l = input.Location;
        if (!settings.DiscordActive || !l.IsRealInstance) return new DiscordPresence { Active = false };

        var platform = settings.DiscordShowPlatform ? (input.GameNoVr ? " (Desktop)" : " (VR)") : "";
        var groupAccessName = l.GroupAccessType switch { "public" => "Public", "plus" => "Plus", _ => "" };
        var joinUrl = "";
        var accessName = l.AccessType switch
        {
            "public" => $"Public #{l.InstanceName}{platform}",
            "invite+" => $"Invite+ #{l.InstanceName}{platform}",
            "invite" => $"Invite #{l.InstanceName}{platform}",
            "friends" => $"Friends #{l.InstanceName}{platform}",
            "friends+" => $"Friends+ #{l.InstanceName}{platform}",
            "group" => input.GroupName is { Length: > 0 } g ? $"Group{groupAccessName}({g}) #{l.InstanceName}{platform}" : $"Group #{l.InstanceName}{platform}",
            _ => "",
        };
        if (l.AccessType == "public") joinUrl = LaunchUrl(l);

        var hidePrivate = settings.DiscordHideInvite && (l.AccessType is "invite" or "invite+" || l.GroupAccessType == "members");
        var status = StatusInfo(input.Status, settings.DiscordHideInvite);
        if (status.HidePrivate) hidePrivate = true;

        var details = input.WorldName ?? "";
        var state = accessName;
        long start = input.JoinedAt?.ToUnixTimeMilliseconds() ?? 0, end = 0;
        var activityType = 0;
        var statusDisplay = settings.DiscordWorldNameAsDiscordStatus ? 2 : 0;
        var appId = VrchatAppId;
        var bigIcon = "vrchat";
        var detailsUrl = input.WorldPublic ? $"https://vrchat.com/home/world/{l.WorldId}" : "";
        var partyId = $"{l.WorldId}:{l.InstanceName}";
        var partySize = input.Players;
        var partyMax = input.WorldCapacity;
        if (partySize > partyMax) partyMax = partySize;
        if (partySize == 0) partyMax = 0;
        if (!settings.DiscordInstance)
        {
            partySize = 0;
            partyMax = 0;
            state = "";
        }
        var (buttonText, buttonUrl) = settings.DiscordJoinButton ? ("Join", joinUrl) : ("", "");
        if (!settings.DiscordHideImage && input.WorldThumbnail is { Length: > 0 } thumb) bigIcon = thumb;

        if (settings.DiscordWorldIntegration && input.VideoName is { Length: > 0 } video && input.VideoStartedAt is { } videoStart)
        {
            activityType = 3;
            if (RpcWorldOf(l.WorldId) is { } rpc)
            {
                (activityType, statusDisplay, appId, bigIcon) = (rpc.ActivityType, rpc.StatusDisplayType, rpc.AppId, rpc.BigIcon);
                details = video;
            }
            if (input.VideoLength > 0)
            {
                start = videoStart.ToUnixTimeMilliseconds();
                end = start + (long)(input.VideoLength * 1000);
            }
        }

        if (hidePrivate)
        {
            partyId = "";
            partySize = 0;
            partyMax = 0;
            buttonText = "";
            buttonUrl = "";
            detailsUrl = "";
            details = "Private World";
            state = "";
            start = 0;
            end = 0;
            appId = VrchatAppId;
            bigIcon = "vrchat";
            activityType = 0;
            statusDisplay = 0;
        }
        // Discord ignores a details line shorter than two characters.
        if (details.Length < 2) details += new string('ﾠ', 2 - details.Length);

        return new DiscordPresence
        {
            Active = true,
            Details = LimitBytes(details, 127),
            State = LimitBytes(state, 127),
            DetailsUrl = detailsUrl,
            LargeImageKey = bigIcon,
            LargeImageText = "Powered by VRCX",
            SmallImageKey = status.Image,
            SmallImageText = status.Name,
            StartTime = start,
            EndTime = end,
            PartyId = partyId,
            PartySize = partySize,
            PartyMax = partyMax,
            ButtonText = buttonText,
            ButtonUrl = buttonUrl,
            AppId = appId,
            ActivityType = activityType,
            StatusDisplayType = statusDisplay,
        };
    }

    /// <summary>The PC's session changed (log, game state): rebuild its presence in the background.</summary>
    public void Changed(string clientId, string? accountId, string? location)
    {
        _clients[clientId] = new Tracked(accountId, location);
        _ = Task.Run(() => UpdateAsync(clientId, CancellationToken.None), CancellationToken.None);
    }

    /// <summary>The account's settings were saved: rebuild the presence of each of its PCs.</summary>
    public void SettingsChanged(string accountId)
    {
        foreach (var (clientId, tracked) in _clients)
            if (tracked.AccountId == accountId)
                _ = Task.Run(() => UpdateAsync(clientId, CancellationToken.None), CancellationToken.None);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            foreach (var clientId in _clients.Keys)
                await UpdateAsync(clientId, stoppingToken);
    }

    private async Task UpdateAsync(string clientId, CancellationToken ct)
    {
        try
        {
            if (desktops.Get(clientId) is not { } desktop)
            {
                _clients.TryRemove(clientId, out _);
                _sent.TryRemove(clientId, out _);
                return;
            }
            if (!desktop.Can(Capability.Discord) || !_clients.TryGetValue(clientId, out var tracked) || tracked.AccountId is not { } accountId) return;
            var presence = await PresenceForAsync(clientId, accountId, tracked.Location, desktop.GameRunning, ct);
            if (_sent.TryGetValue(clientId, out var last) && last.Equals(presence)) return;
            // Nothing was ever shown: no need to clear.
            if (last is null && !presence.Active) return;
            _sent[clientId] = presence;
            await desktops.PostAsync(clientId, new ClientAction { SetDiscordPresence = presence }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Updating Discord presence for {Client} failed", clientId);
        }
    }

    private async Task<DiscordPresence> PresenceForAsync(string clientId, string accountId, string? location, bool gameRunning, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => a.Settings).FirstOrDefaultAsync(ct);
        if (settings is not { DiscordActive: true } || !gameRunning || location is null) return new DiscordPresence { Active = false };
        var l = VrchatLocation.Parse(location);
        if (!l.IsRealInstance) return new DiscordPresence { Active = false };

        var world = await db.Worlds.AsNoTracking().Where(w => w.Id == l.WorldId).Select(w => new { w.Name, w.ThumbnailImageUrl, w.Capacity, w.ReleaseStatus }).FirstOrDefaultAsync(ct);
        var group = l.GroupId is { } groupId ? await db.Groups.AsNoTracking().Where(g => g.Id == groupId).Select(g => g.Name).FirstOrDefaultAsync(ct) : null;
        var visits = db.InstanceVisits.AsNoTracking().Where(v => v.InstanceLocation == location && v.Source == VisitSource.GameLog && v.LeftAt == null);
        var players = await visits.CountAsync(ct);
        var joinedAt = await visits.Where(v => v.UserId == accountId).OrderByDescending(v => v.JoinedAt).Select(v => (DateTimeOffset?)v.JoinedAt).FirstOrDefaultAsync(ct);
        var noVr = await db.GameLogClients.AsNoTracking().Where(c => c.ClientId == clientId).Select(c => c.GameNoVr).FirstOrDefaultAsync(ct);
        var status = await db.Users.AsNoTracking().Where(u => u.Id == accountId).Select(u => u.Status).FirstOrDefaultAsync(ct);
        // nowPlaying: the instance's last video since joining, while it plays.
        var video = await db.VideoPlays.AsNoTracking()
            .Where(v => v.InstanceLocation == location && (joinedAt == null || v.At >= joinedAt))
            .OrderByDescending(v => v.At)
            .Select(v => new { v.At, v.VideoName, v.VideoPos, v.VideoLength })
            .FirstOrDefaultAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var videoStart = video is null ? (DateTimeOffset?)null : video.At.AddSeconds(-(video.VideoPos ?? 0));
        var playing = video is { VideoName.Length: > 0 } && (video.VideoLength is not > 0 || videoStart!.Value.AddSeconds(video.VideoLength.Value) > now);

        var input = new PresenceInput(
            l, joinedAt, world?.Name, world?.ThumbnailImageUrl, world?.Capacity ?? 0, world?.ReleaseStatus == "public", group, players, noVr, status,
            playing ? video!.VideoName : null, playing ? videoStart : null, playing ? video!.VideoLength ?? 0 : 0);
        return Build(input, settings, now);
    }
}
