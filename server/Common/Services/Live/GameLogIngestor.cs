using Microsoft.EntityFrameworkCore;
using VRCX.Common.Live;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.Services.Presence;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.Common.Services.Live;

/// <summary>
/// What one game-log-shipping client (one gaming PC) is in the middle of: which account plays
/// there, which instance it is in, and how far its log has been stored. Mirrors the
/// game_log_clients row; kept in memory by the websocket connection between batches.
/// </summary>
public sealed class GameLogSession
{
    public required string ClientId { get; init; }
    public string? ClientName { get; set; }
    /// <summary>The usr_ id the game authenticated as (from the User Authenticated log line).</summary>
    public string? AccountId { get; set; }
    /// <summary>The instances row the PC is currently in, if any.</summary>
    public string? CurrentInstanceLocation { get; set; }
    public DateTimeOffset? LastEntryAt { get; set; }
    /// <summary>The original skipped a resource load repeating the previous URL.</summary>
    public string? LastResourceLoadUrl { get; set; }
    /// <summary>The video last stored: the same one again (a world re-logging it) is not a new play.</summary>
    public string? LastVideoUrl { get; set; }
    /// <summary>
    /// The original's lastLocationAvatarList: the avatar each player in the instance was last seen in,
    /// by display name, so only a change (not the first sighting or a repeat) is stored.
    /// </summary>
    public Dictionary<string, string> AvatarsByName { get; } = new(StringComparer.Ordinal);
    /// <summary>Whether VRChat runs on the PC, as its GameState last said; null until it has.</summary>
    public bool? GameRunning { get; set; }
}

/// <summary>What a batch produced: how many lines were understood, and what the client should do now.</summary>
public sealed record GameLogBatchResult(int Accepted, IReadOnlyList<ClientAction> Actions);

public interface IGameLogIngestor
{
    /// <summary>Loads (or creates) the client's game_log_clients row into the session, so a reconnect continues where it left off.</summary>
    Task ResumeAsync(GameLogSession session, CancellationToken ct);
    /// <summary>Stores a batch of parsed log lines in one unit of work and publishes what changed. Returns how many entries were understood and the actions to send back.</summary>
    Task<GameLogBatchResult> HandleBatchAsync(GameLogSession session, IReadOnlyList<LogEntry> entries, CancellationToken ct);
    /// <summary>
    /// VRChat is not running: the PC's own stay and everyone it had seen there end at <paramref name="at"/>,
    /// as the original's runLastLocationResetFlow does when the game stops. Nothing happens when no stay is open.
    /// </summary>
    Task GameStoppedAsync(GameLogSession session, DateTimeOffset at, CancellationToken ct);
    /// <summary>
    /// Keeps the PC's game session times: a start when VRChat is seen running, and the finished session
    /// when it stops (at <paramref name="at"/>, or the last log line when it stopped unseen).
    /// </summary>
    Task GameStateAsync(GameLogSession session, bool running, DateTimeOffset at, CancellationToken ct);
}

/// <summary>
/// Turns the desktop app's parsed log lines into rows: the PC's own instance visits
/// (source game_log) from "Entering Room"/"Joining", other players' visits from
/// OnPlayerJoined/OnPlayerLeft, video plays, portal spawns, sticker spawns and Udon resource
/// loads. Lines the schema has no home for (api-request, avatar change, screenshots, ...) are
/// counted as understood and dropped.
/// </summary>
public sealed class GameLogIngestor(
    IDbContextFactory<VrcxContext> dbFactory,
    VrchatStore store,
    IGameClientState clientState,
    ILiveEventBus bus,
    YouTubeLookup youTube,
    ILogger<GameLogIngestor> logger,
    DesktopClients? desktops = null) : IGameLogIngestor
{
    public async Task ResumeAsync(GameLogSession session, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var row = await db.GameLogClients.FindAsync([session.ClientId], ct);
        if (row is null)
        {
            row = new GameLogClient { ClientId = session.ClientId, FirstSeenAt = now };
            db.GameLogClients.Add(row);
        }
        row.ClientName = session.ClientName ?? row.ClientName;
        row.LastSeenAt = now;
        await db.SaveChangesAsync(ct);

        session.AccountId = row.AccountId;
        // If Redis lost it, the account's open game log visit says the same thing.
        var live = await clientState.GetAsync(session.ClientId, ct);
        session.CurrentInstanceLocation = live is not null
            ? live.CurrentInstanceLocation
            : await db.InstanceVisits.AsNoTracking()
                .Where(v => v.UserId == row.AccountId && v.Source == VisitSource.GameLog && v.LeftAt == null)
                .Select(v => v.InstanceLocation).FirstOrDefaultAsync(ct);
        await clientState.SetAsync(session.ClientId, new GameClientLive(session.CurrentInstanceLocation, now), ct);
        session.LastEntryAt = row.LastEntryAt;
    }

    public async Task<GameLogBatchResult> HandleBatchAsync(GameLogSession session, IReadOnlyList<LogEntry> entries, CancellationToken ct)
    {
        var before = Snapshot(session);
        try
        {
            return await ApplyAsync(session, entries, new Repeats(), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && IsTransient(ex))
        {
            // The database is briefly unreachable. Not acknowledging is right: the connection drops
            // and the app sends the batch again once it reconnects.
            Restore(session, before);
            throw;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Some row in the batch is already stored (a resent batch, or the same video twice in one
            // second), or one line cannot be stored at all. The rest must not be lost with it, and an
            // unacknowledged batch is sent again forever, stopping everything after it: start over one
            // line at a time, so only the lines already stored, or that fail, are skipped.
            if (ex is DbUpdateException dup && IsDuplicate(dup))
                logger.LogDebug(ex, "Game-log batch from {Client} contained rows already stored; storing it line by line", session.ClientId);
            else
                logger.LogWarning(ex, "Game-log batch from {Client} could not be stored whole; storing it line by line", session.ClientId);
            Restore(session, before);
            var accepted = 0;
            var actions = new List<ClientAction>();
            var repeats = new Repeats();
            foreach (var entry in entries)
            {
                try
                {
                    var result = await ApplyAsync(session, [entry], repeats, ct);
                    accepted += result.Accepted;
                    actions.AddRange(result.Actions);
                }
                catch (DbUpdateException single) when (IsDuplicate(single))
                {
                    // Already stored; what the line says about the session still holds.
                    accepted++;
                    AdvanceResume(session, entry);
                }
                catch (Exception transient) when (!ct.IsCancellationRequested && IsTransient(transient))
                {
                    // An outage mid-way: lines stored so far stay, the rest come again with the resend.
                    logger.LogWarning(transient, "Database unavailable while storing a game-log batch from {Client}; it will be resent", session.ClientId);
                    throw;
                }
                catch (Exception bad) when (!ct.IsCancellationRequested)
                {
                    // Lost on its own rather than holding up every line after it.
                    logger.LogError(bad, "Skipped a game-log line from {Client} that could not be stored: {Line}", session.ClientId, entry.ToString());
                    GameLogMetrics.SkippedLines.Add(1);
                    AdvanceResume(session, entry);
                }
            }
            return new GameLogBatchResult(accepted, actions);
        }
    }

    private static void AdvanceResume(GameLogSession session, LogEntry entry)
    {
        if (ResumeTime(entry) is { } at && (session.LastEntryAt is null || at > session.LastEntryAt)) session.LastEntryAt = at;
    }

    /// <summary>
    /// How often each event or spawn has come up in the batch being stored. These rows have no natural
    /// key (two portals in one second are two rows), so a resent batch is recognised by count: the n-th
    /// identical row of a batch is stored only while fewer than n are stored already.
    /// </summary>
    private sealed class Repeats
    {
        private readonly Dictionary<(string, DateTimeOffset, string?, string?), int> _seen = [];

        public int Next(string table, DateTimeOffset at, string? instance, string? content)
        {
            var key = (table, at, instance, content);
            var n = _seen.GetValueOrDefault(key) + 1;
            _seen[key] = n;
            return n;
        }
    }

    /// <summary>
    /// An error that may not happen on a second try: the database unreachable, restarting or timing out.
    /// Only errors that fail the same way every time (bad data, a constraint, the server's own code) may
    /// cost a line; anything else must leave the batch unacknowledged so it is sent again.
    /// </summary>
    private static bool IsTransient(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case Npgsql.PostgresException pg:
                    // 22: data exception, 23: integrity constraint. Everything else (57P: shutting down,
                    // 08: connection, 53: out of resources, 40: serialization) may pass on retry.
                    return !(pg.SqlState.StartsWith("22", StringComparison.Ordinal) || pg.SqlState.StartsWith("23", StringComparison.Ordinal));
                case Npgsql.NpgsqlException:
                case TimeoutException:
                case System.Net.Sockets.SocketException:
                case System.IO.IOException:
                    return true;
            }
        }
        return false;
    }

    private static string? Blank(string? s) => string.IsNullOrEmpty(s) ? null : s;

    /// <summary>JavaScript's decodeURI: escapes decoded except those of reserved characters.</summary>
    private static string DecodeUri(string url)
    {
        if (string.IsNullOrEmpty(url) || !url.Contains('%')) return url;
        try
        {
            return System.Text.RegularExpressions.Regex.Replace(url, "(%[0-9A-Fa-f]{2})+", m =>
            {
                var decoded = Uri.UnescapeDataString(m.Value);
                var kept = new System.Text.StringBuilder();
                // Reserved characters and '#' stay escaped, as decodeURI leaves them.
                if (decoded.Contains('\uFFFD')) return m.Value;
                foreach (var ch in decoded)
                    kept.Append(";/?:@&=+$,#".Contains(ch) ? $"%{(int)ch:X2}" : ch.ToString());
                return kept.ToString();
            });
        }
        catch (Exception)
        {
            return url;
        }
    }

    private static bool IsDuplicate(DbUpdateException ex) => ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    private sealed record SessionState(string? AccountId, string? CurrentInstanceLocation, DateTimeOffset? LastEntryAt, string? LastResourceLoadUrl, string? LastVideoUrl);

    private static SessionState Snapshot(GameLogSession s) => new(s.AccountId, s.CurrentInstanceLocation, s.LastEntryAt, s.LastResourceLoadUrl, s.LastVideoUrl);

    private static void Restore(GameLogSession s, SessionState state)
    {
        s.AccountId = state.AccountId;
        s.CurrentInstanceLocation = state.CurrentInstanceLocation;
        s.LastEntryAt = state.LastEntryAt;
        s.LastResourceLoadUrl = state.LastResourceLoadUrl;
        s.LastVideoUrl = state.LastVideoUrl;
    }

    /// <summary>
    /// The time a line moves the resume point to. A Udon exception's continuation lines carry no time of
    /// their own (older desktop apps stamp them with the moment they were read), so they never move it:
    /// during catch-up that would jump the resume point past everything not yet read.
    /// </summary>
    private static DateTimeOffset? ResumeTime(LogEntry entry) =>
        entry.EntryCase == LogEntry.EntryOneofCase.UdonException ? null : entry.CreatedAt?.ToDateTimeOffset();

    private async Task<GameLogBatchResult> ApplyAsync(GameLogSession session, IReadOnlyList<LogEntry> entries, Repeats repeats, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var published = new List<ServerEvent>();
        var actions = new List<ClientAction>();
        // Built after the save, so the metadata sees the world name and the players this batch added.
        var pendingScreenshots = new List<(string Path, AccountSettings Settings)>();
        var accepted = 0;
        bool? gameNoVr = null;
        AccountSettings? settings = null;
        async Task<AccountSettings> SettingsAsync()
        {
            // The account playing on this PC, or the only account when the log has not said yet.
            settings ??= (session.AccountId is { } id
                ? await db.Accounts.AsNoTracking().Where(a => a.UserId == id).Select(a => a.Settings).FirstOrDefaultAsync(ct)
                : await db.Accounts.AsNoTracking().Select(a => a.Settings).Take(2).ToListAsync(ct) is [var only] ? only : null) ?? new AccountSettings();
            return settings;
        }
        DateTimeOffset? newest = session.LastEntryAt;
        // Rows other than stays this batch adds, for one live event once they are saved.
        var storedKinds = new HashSet<string>(StringComparer.Ordinal);

        var currentAt = DateTimeOffset.UtcNow;
        // The original's addGameLogVideo / setNowPlaying: a YouTube video is named by the YouTube API when
        // the account has a key; a world's own line already carries the name.
        async Task AddVideoAsync(MediaPlay play, bool lookUpYouTube)
        {
            var (videoId, videoName, videoLength) = (play.VideoId == "YouTube" ? null : play.VideoId, play.VideoName, play.VideoLength);
            if (lookUpYouTube && MediaParsers.YouTubeId(play.VideoUrl) is { } youTubeId)
            {
                var s = await SettingsAsync();
                if (s.YoutubeApi && !string.IsNullOrEmpty(s.YoutubeApiKey) && await youTube.LookupAsync(youTubeId, s.YoutubeApiKey, ct) is { } video)
                    (videoId, videoName, videoLength) = ("YouTube", video.Title, video.Length ?? videoLength);
            }
            var player = await ResolvePlayerAsync(db, session, play.DisplayName ?? "", ct);
            storedKinds.Add("video");
            db.VideoPlays.Add(new VideoPlay
            {
                At = currentAt,
                InstanceLocation = session.CurrentInstanceLocation,
                UserId = player,
                DisplayName = play.DisplayName,
                VideoUrl = play.VideoUrl,
                VideoId = videoId,
                VideoName = videoName,
                VideoPos = play.VideoPos,
                VideoLength = videoLength,
            });
        }

        foreach (var entry in entries)
        {
            var at = entry.CreatedAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow;
            currentAt = at;
            if (ResumeTime(entry) is { } resume && (newest is null || resume > newest)) newest = resume;
            switch (entry.EntryCase)
            {
                case LogEntry.EntryOneofCase.UserAuthenticated:
                    if (!string.IsNullOrEmpty(entry.UserAuthenticated.UserId))
                    {
                        await store.EnsureUserAsync(db, entry.UserAuthenticated.UserId, entry.UserAuthenticated.DisplayName, at, ct);
                        session.AccountId = entry.UserAuthenticated.UserId;
                    }
                    accepted++;
                    break;

                case LogEntry.EntryOneofCase.Location:
                    await EnterAsync(db, session, entry.Location.Location, entry.Location.WorldName, at, published, ct);
                    accepted++;
                    break;

                case LogEntry.EntryOneofCase.LocationDestination:
                    // The original's location-destination: the game is leaving for another instance, so
                    // every stay here ends now (not when the next instance loads) and the PC is traveling.
                    await LeaveAllAsync(db, session, at, published, ct);
                    session.AvatarsByName.Clear();
                    session.LastVideoUrl = null;
                    accepted++;
                    break;

                case LogEntry.EntryOneofCase.PlayerJoined:
                    await PlayerAsync(db, session, entry.PlayerJoined, joined: true, at, published, ct);
                    accepted++;
                    break;

                case LogEntry.EntryOneofCase.PlayerLeft:
                    await PlayerAsync(db, session, entry.PlayerLeft, joined: false, at, published, ct);
                    accepted++;
                    break;

                case LogEntry.EntryOneofCase.VrcQuit:
                    await LeaveAllAsync(db, session, at, published, ct);
                    // The original's quit fix: VRChat tends to hang after logging its quit, so the
                    // desktop app kills it - but only for a fresh line, never while replaying old log.
                    if ((await SettingsAsync()).VrcQuitFix && DateTimeOffset.UtcNow - at < TimeSpan.FromSeconds(3))
                        actions.Add(new ClientAction { QuitVrchat = new QuitVrchat() });
                    accepted++;
                    break;

                case LogEntry.EntryOneofCase.Screenshot:
                    {
                        var s = await SettingsAsync();
                        var path = entry.Screenshot.Text;
                        if (!string.IsNullOrEmpty(path) && (s.ScreenshotHelper || s.ScreenshotHelperCopyToClipboard) && DateTimeOffset.UtcNow - at < TimeSpan.FromMinutes(5))
                            pendingScreenshots.Add((path, s));
                        accepted++;
                        break;
                    }

                case LogEntry.EntryOneofCase.Event:
                    if (!string.IsNullOrEmpty(entry.Event.Text))
                    {
                        var (instance, text) = (session.CurrentInstanceLocation, entry.Event.Text);
                        var n = repeats.Next("event", at, instance, text);
                        if (await db.GameLogEvents.CountAsync(e => e.At == at && e.InstanceLocation == instance && e.Kind == "Event" && e.Data == text, ct) < n)
                        {
                            db.GameLogEvents.Add(new GameLogEvent { At = at, InstanceLocation = instance, Kind = "Event", Data = text });
                            storedKinds.Add("event");
                        }
                        if (text.StartsWith("VideoError: ", StringComparison.Ordinal) && text.Contains("Sign in to confirm", StringComparison.Ordinal) && DateTimeOffset.UtcNow - at < TimeSpan.FromMinutes(5))
                            published.Add(LiveEvents.YouTubeBotError(session.AccountId, text));
                    }
                    accepted++;
                    break;

                case LogEntry.EntryOneofCase.VideoPlay:
                    {
                        var url = DecodeUri(entry.VideoPlay.VideoUrl);
                        // The original's video-play: the same URL again is not a new play, and worlds with
                        // their own integration name the video in their own "[VRCX]" line instead.
                        if (!string.IsNullOrEmpty(url) && url != session.LastVideoUrl)
                        {
                            session.LastVideoUrl = url;
                            if (!MediaParsers.IsRpcWorld(session.CurrentInstanceLocation))
                                await AddVideoAsync(new MediaPlay(url, null, null, Blank(entry.VideoPlay.DisplayName), VideoPos: 8), lookUpYouTube: true);
                        }
                        accepted++;
                        break;
                    }

                case LogEntry.EntryOneofCase.Vrcx:
                    // A dance or cinema world saying what it plays (VRDancing, LSMedia, PopcornPalace).
                    if (MediaParsers.ParseVrcxLine(entry.Vrcx.Text) is { } play)
                    {
                        if (play.VideoUrl != session.LastVideoUrl)
                        {
                            session.LastVideoUrl = play.VideoUrl;
                            await AddVideoAsync(play, lookUpYouTube: play.VideoId == "YouTube");
                        }
                        else if (play.VideoPos is { } pos)
                            // The same video again: only its position moved (the original's setNowPlaying update).
                            published.Add(LiveEvents.NowPlayingPosition(session.AccountId, play.VideoUrl, pos, play.VideoLength ?? 0, at));
                    }
                    accepted++;
                    break;

                case LogEntry.EntryOneofCase.AvatarChange:
                    {
                        var (name, avatar) = (entry.AvatarChange.DisplayName, entry.AvatarChange.AvatarName);
                        if (session.CurrentInstanceLocation is { } instance && !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(avatar))
                        {
                            var known = session.AvatarsByName.TryGetValue(name, out var previous);
                            session.AvatarsByName[name] = avatar;
                            if (known && previous != avatar)
                            {
                                var userId = await ResolvePlayerAsync(db, session, name, ct);
                                var n = repeats.Next("avatar", at, instance, name + "|" + avatar);
                                if (await db.GameLogEvents.CountAsync(e => e.At == at && e.InstanceLocation == instance && e.Kind == "AvatarChange" && e.DisplayName == name && e.Data == avatar, ct) < n)
                                {
                                    db.GameLogEvents.Add(new GameLogEvent { At = at, InstanceLocation = instance, Kind = "AvatarChange", Data = avatar, UserId = userId, DisplayName = name });
                                    storedKinds.Add("avatar");
                                }
                            }
                        }
                        accepted++;
                        break;
                    }

                case LogEntry.EntryOneofCase.PortalSpawn:
                    {
                        var instance = session.CurrentInstanceLocation;
                        var n = repeats.Next("portal", at, instance, null);
                        if (await db.PortalSpawns.CountAsync(p => p.At == at && p.InstanceLocation == instance, ct) < n)
                        {
                            db.PortalSpawns.Add(new PortalSpawn { At = at, InstanceLocation = instance });
                            storedKinds.Add("portal");
                        }
                    }
                    accepted++;
                    break;

                case LogEntry.EntryOneofCase.StickerSpawn:
                    if (!string.IsNullOrEmpty(entry.StickerSpawn.InventoryId))
                    {
                        string? userId = string.IsNullOrEmpty(entry.StickerSpawn.UserId) ? null : entry.StickerSpawn.UserId;
                        if (userId is not null) await store.EnsureUserAsync(db, userId, entry.StickerSpawn.DisplayName, at, ct);
                        var (instance, inventoryId) = (session.CurrentInstanceLocation, entry.StickerSpawn.InventoryId);
                        var n = repeats.Next("sticker", at, instance, userId + "|" + inventoryId);
                        var addSticker = await db.StickerSpawns.CountAsync(s => s.At == at && s.InstanceLocation == instance && s.UserId == userId && s.InventoryId == inventoryId, ct) < n;
                        if (addSticker) storedKinds.Add("sticker");
                        if (addSticker)
                        db.StickerSpawns.Add(new StickerSpawn
                        {
                            At = at,
                            InstanceLocation = session.CurrentInstanceLocation,
                            UserId = userId,
                            DisplayName = string.IsNullOrEmpty(entry.StickerSpawn.DisplayName) ? null : entry.StickerSpawn.DisplayName,
                            InventoryId = entry.StickerSpawn.InventoryId,
                        });
                    }
                    accepted++;
                    break;

                case LogEntry.EntryOneofCase.ResourceLoadString:
                case LogEntry.EntryOneofCase.ResourceLoadImage:
                    {
                        var isImage = entry.EntryCase == LogEntry.EntryOneofCase.ResourceLoadImage;
                        var url = isImage ? entry.ResourceLoadImage.Text : entry.ResourceLoadString.Text;
                        if (!string.IsNullOrEmpty(url) && url != session.LastResourceLoadUrl && (await SettingsAsync()).LogResourceLoad)
                        {
                            db.ResourceLoads.Add(new ResourceLoad { At = at, InstanceLocation = session.CurrentInstanceLocation, Url = url, Kind = isImage ? "image" : "string" });
                            storedKinds.Add("resource");
                            session.LastResourceLoadUrl = url;
                        }
                        accepted++;
                        break;
                    }

                case LogEntry.EntryOneofCase.UdonException:
                    // The original's udonExceptionLogging: the exception goes to the log, only when asked for.
                    if (DateTimeOffset.UtcNow - at < TimeSpan.FromMinutes(5) && (await SettingsAsync()).UdonExceptionLogging)
                        logger.LogInformation("UdonException from {Client}: {Text}", session.ClientId, entry.UdonException.Text);
                    accepted++;
                    break;

                case LogEntry.EntryOneofCase.OpenvrInit:
                case LogEntry.EntryOneofCase.DesktopMode:
                    // The original's isGameNoVR: which way VRChat started, for the Desktop Mode notification condition.
                    gameNoVr = entry.EntryCase == LogEntry.EntryOneofCase.DesktopMode;
                    accepted++;
                    break;

                case LogEntry.EntryOneofCase.None:
                    break;

                default:
                    // Known line, nothing to store for it yet.
                    accepted++;
                    break;
            }
        }

        var client = await db.GameLogClients.FindAsync([session.ClientId], ct);
        if (client is null)
        {
            client = new GameLogClient { ClientId = session.ClientId, FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow };
            db.GameLogClients.Add(client);
        }
        client.ClientName = session.ClientName ?? client.ClientName;
        client.AccountId = session.AccountId;
        client.LastEntryAt = newest;
        var noVrChanged = gameNoVr is { } noVr && noVr != client.GameNoVr;
        if (gameNoVr is { } mode) client.GameNoVr = mode;

        // A duplicate row throws to HandleBatchAsync, which stores the batch line by line instead.
        await db.SaveChangesAsync(ct);

        session.LastEntryAt = newest;
        await clientState.SetAsync(session.ClientId, new GameClientLive(session.CurrentInstanceLocation, DateTimeOffset.UtcNow), ct);
        foreach (var e in published) bus.Publish(e);
        if (storedKinds.Count > 0) bus.Publish(LiveEvents.GameLog(session.AccountId, session.CurrentInstanceLocation, storedKinds));
        if (noVrChanged && desktops?.Get(session.ClientId) is { } desktop)
            bus.Publish(LiveEvents.Desktop(session.ClientId, connected: true, desktop.GameRunning, client.GameNoVr));
        foreach (var (path, s) in pendingScreenshots) actions.Add(await ScreenshotActionAsync(db, session, path, s, ct));
        return new GameLogBatchResult(accepted, actions);
    }

    public async Task GameStateAsync(GameLogSession session, bool running, DateTimeOffset at, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.GameLogClients.FindAsync([session.ClientId], ct);
        if (row is null) return;
        if (running)
        {
            // A reconnect while it keeps running keeps the start it had.
            row.GameStartedAt ??= at;
        }
        else if (row.GameStartedAt is { } started)
        {
            var stopped = at;
            // Stopped while the app was away: it ended no later than its last log line.
            if (session.GameRunning != true && session.LastEntryAt is { } last && last > started && last < at) stopped = last;
            row.LastGameStartedAt = started;
            row.LastGameStoppedAt = stopped;
            row.GameStartedAt = null;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task GameStoppedAsync(GameLogSession session, DateTimeOffset at, CancellationToken ct)
    {
        if (session.CurrentInstanceLocation is null) return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var published = new List<ServerEvent>();
        await LeaveAllAsync(db, session, at, published, ct);
        await db.SaveChangesAsync(ct);
        await clientState.SetAsync(session.ClientId, new GameClientLive(null, DateTimeOffset.UtcNow), ct);
        foreach (var e in published) bus.Publish(e);
    }

    /// <summary>
    /// The original's processScreenshot: a JSON block naming the author, the world and
    /// instance, and everyone in it, for the desktop app to write into the PNG.
    /// </summary>
    private async Task<ClientAction> ScreenshotActionAsync(VrcxContext db, GameLogSession session, string path, AccountSettings settings, CancellationToken ct)
    {
        var location = VrchatLocation.Parse(session.CurrentInstanceLocation);
        var author = session.AccountId is { } id ? await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => new { u.Id, u.DisplayName }).FirstOrDefaultAsync(ct) : null;
        var worldName = location.IsRealInstance
            ? await db.Worlds.AsNoTracking().Where(w => w.Id == location.WorldId).Select(w => w.Name).FirstOrDefaultAsync(ct)
            : null;
        var players = session.CurrentInstanceLocation is { } here
            ? await db.InstanceVisits.AsNoTracking()
                .Where(v => v.InstanceLocation == here && v.Source == VisitSource.GameLog && v.LeftAt == null)
                .Select(v => new { id = v.UserId, displayName = v.User.DisplayName })
                .ToListAsync(ct)
            : [];
        var metadata = new
        {
            application = "VRCX",
            version = 1,
            author = new { id = author?.Id ?? session.AccountId ?? "", displayName = author?.DisplayName ?? "" },
            world = new { name = worldName ?? "", id = location.WorldId, instanceId = session.CurrentInstanceLocation ?? "" },
            players,
        };
        return new ClientAction
        {
            ProcessScreenshot = new ProcessScreenshot
            {
                Path = path,
                MetadataJson = settings.ScreenshotHelper ? System.Text.Json.JsonSerializer.Serialize(metadata) : "",
                AppendWorldId = settings.ScreenshotHelper && settings.ScreenshotHelperModifyFilename,
                WorldId = location.WorldId,
                CopyToClipboard = settings.ScreenshotHelperCopyToClipboard,
            },
        };
    }

    private async Task EnterAsync(VrcxContext db, GameLogSession session, string tag, string? worldName, DateTimeOffset at, List<ServerEvent> published, CancellationToken ct)
    {
        var location = VrchatLocation.Parse(tag);
        if (!location.IsRealInstance) return;

        var instance = await store.EnsureInstanceAsync(db, location, at, ct);
        if (!string.IsNullOrEmpty(worldName)) await store.EnsureWorldAsync(db, location.WorldId, worldName, at, ct);
        if (session.CurrentInstanceLocation == instance.Location) return;

        await LeaveAllAsync(db, session, at, published, ct);
        session.CurrentInstanceLocation = instance.Location;
        session.AvatarsByName.Clear();

        if (session.AccountId is null) return;
        var me = await store.EnsureUserAsync(db, session.AccountId, null, at, ct);
        if (await AlreadyStoredAsync(db, me.Id, instance.Location, at, ct)) return;
        db.InstanceVisits.Add(new InstanceVisit { UserId = me.Id, InstanceLocation = instance.Location, JoinedAt = at, Source = VisitSource.GameLog });
        published.Add(LiveEvents.Visit(me.Id, me.DisplayName, instance.Location, instance.WorldId, joined: true, VisitSource.GameLog));
    }

    private async Task PlayerAsync(VrcxContext db, GameLogSession session, LogPlayer player, bool joined, DateTimeOffset at, List<ServerEvent> published, CancellationToken ct)
    {
        if (session.CurrentInstanceLocation is null) return;
        var userId = string.IsNullOrEmpty(player.UserId) ? await ResolvePlayerAsync(db, session, player.DisplayName, ct) : player.UserId;
        if (userId is null) return;
        // The PC's own join/leave lines duplicate what EnterAsync already recorded.
        if (userId == session.AccountId) return;

        var user = await store.EnsureUserAsync(db, userId, player.DisplayName, at, ct);
        var open = db.InstanceVisits.Local.FirstOrDefault(v => v.UserId == userId && v.Source == VisitSource.GameLog && v.LeftAt is null)
            ?? await db.InstanceVisits.FirstOrDefaultAsync(v => v.UserId == userId && v.Source == VisitSource.GameLog && v.LeftAt == null, ct);
        var instance = await db.Instances.FindAsync([session.CurrentInstanceLocation], ct);
        var worldId = instance?.WorldId ?? VrchatLocation.Parse(session.CurrentInstanceLocation).WorldId;

        if (joined)
        {
            if (open is not null && open.InstanceLocation == session.CurrentInstanceLocation) return;
            if (open is not null) open.LeftAt = at;
            if (await AlreadyStoredAsync(db, userId, session.CurrentInstanceLocation, at, ct)) return;
            db.InstanceVisits.Add(new InstanceVisit { UserId = userId, InstanceLocation = session.CurrentInstanceLocation, JoinedAt = at, Source = VisitSource.GameLog });
            published.Add(LiveEvents.Visit(userId, user.DisplayName, session.CurrentInstanceLocation, worldId, joined: true, VisitSource.GameLog));
        }
        else if (open is not null)
        {
            session.AvatarsByName.Remove(player.DisplayName);
            open.LeftAt = at;
            published.Add(LiveEvents.Visit(userId, user.DisplayName, open.InstanceLocation, worldId, joined: false, VisitSource.GameLog));
        }
    }

    /// <summary>
    /// The desktop app resends log it is unsure was received; a join already stored (with the same
    /// time) is the same stay, which must not be stored again. That copy would never be closed.
    /// </summary>
    private static async Task<bool> AlreadyStoredAsync(VrcxContext db, string userId, string location, DateTimeOffset at, CancellationToken ct) =>
        db.InstanceVisits.Local.Any(v => v.UserId == userId && v.InstanceLocation == location && v.JoinedAt == at && v.Source == VisitSource.GameLog)
        || await db.InstanceVisits.AnyAsync(v => v.UserId == userId && v.InstanceLocation == location && v.JoinedAt == at && v.Source == VisitSource.GameLog, ct);

    /// <summary>The PC left its instance: close its own visit and everyone it had seen there.</summary>
    private static async Task LeaveAllAsync(VrcxContext db, GameLogSession session, DateTimeOffset at, List<ServerEvent> published, CancellationToken ct)
    {
        if (session.CurrentInstanceLocation is null) return;
        var location = session.CurrentInstanceLocation;
        session.CurrentInstanceLocation = null;

        var open = await db.InstanceVisits.Include(v => v.User)
            .Where(v => v.InstanceLocation == location && v.Source == VisitSource.GameLog && v.LeftAt == null)
            .ToListAsync(ct);
        var worldId = VrchatLocation.Parse(location).WorldId;
        foreach (var visit in open)
        {
            visit.LeftAt = at;
            published.Add(LiveEvents.Visit(visit.UserId, visit.User.DisplayName, location, worldId, joined: false, VisitSource.GameLog));
        }
    }

    /// <summary>Older log lines name players without an id; whoever we have seen in this instance under that name is the best guess.</summary>
    private static async Task<string?> ResolvePlayerAsync(VrcxContext db, GameLogSession session, string? displayName, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(displayName)) return null;
        if (session.CurrentInstanceLocation is not null)
        {
            // A join earlier in the same batch is not saved yet.
            var pending = db.InstanceVisits.Local
                .Where(v => v.InstanceLocation == session.CurrentInstanceLocation && v.Source == VisitSource.GameLog && v.LeftAt is null)
                .Select(v => v.UserId)
                .FirstOrDefault(id => db.Users.Local.FirstOrDefault(u => u.Id == id)?.DisplayName == displayName);
            if (pending is not null) return pending;
            var inInstance = await db.InstanceVisits
                .Where(v => v.InstanceLocation == session.CurrentInstanceLocation && v.Source == VisitSource.GameLog && v.LeftAt == null && v.User.DisplayName == displayName)
                .Select(v => v.UserId)
                .FirstOrDefaultAsync(ct);
            if (inInstance is not null) return inInstance;
        }
        return await db.Users.Where(u => u.DisplayName == displayName).OrderByDescending(u => u.UpdatedAt).Select(u => u.Id).FirstOrDefaultAsync(ct);
    }
}

/// <summary>Game-log counters on /metrics.</summary>
public static class GameLogMetrics
{
    public const string MeterName = "VRCX.GameLog";
    private static readonly System.Diagnostics.Metrics.Meter Meter = new(MeterName);

    /// <summary>Lines dropped because they could not be stored, so one bad line does not block the rest.</summary>
    public static readonly System.Diagnostics.Metrics.Counter<long> SkippedLines =
        Meter.CreateCounter<long>("vrcx_gamelog_lines_skipped", description: "Game-log lines that could not be stored and were skipped");
}
