using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VRCX.Common.Live;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.Services.Sessions;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;
using Vrc = VRCX.Vrchat.Models;

namespace VRCX.Common.Services.Live.Photon;

/// <summary>A player in the lobby, for the player list.</summary>
public sealed record PhotonPlayer(int PhotonId, string? UserId, string? DisplayName, bool IsMaster, PhotonJoin? Join);

/// <summary>What the player list and its Photon table show.</summary>
public sealed record PhotonSnapshot(
    bool Enabled,
    string? Location,
    int Master,
    int CurrentUser,
    IReadOnlyList<PhotonPlayer> Players,
    IReadOnlyList<PhotonTimeout> Timeouts,
    IReadOnlyList<PhotonFeedEntry> Events,
    IReadOnlyList<PhotonFeedEntry> PreviousEvents);

/// <summary>
/// The Photon logger's stream from each PC (the original's ipcEvent Photon cases and photon
/// store): a lobby per PC, its effects done here (game log rows, portal targets, group names,
/// moderations against the account, avatars), and a PhotonUpdated event when it changed.
/// </summary>
public sealed class PhotonService(
    IDbContextFactory<VrcxContext> dbFactory,
    VrchatStore store,
    IAccountSessionManager sessions,
    ILiveEventBus bus,
    ILogger<PhotonService> logger,
    DesktopClients desktops,
    TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private sealed class Pc
    {
        public PhotonLobby Lobby { get; } = new();
        public string? AccountId { get; set; }
        public bool Enabled { get; set; }
        public int LastTimeouts { get; set; }
        public (AccountSettings Settings, DateTimeOffset At)? Settings { get; set; }
    }

    private readonly ConcurrentDictionary<string, Pc> _pcs = new();
    private readonly Dictionary<string, List<Dictionary<string, object?>>> _pending = new();
    private readonly Lock _publishLock = new();

    /// <summary>How long a published change waits for more, so a burst is one event.</summary>
    public static TimeSpan PublishDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    private Pc PcOf(GameLogSession session)
    {
        var pc = _pcs.GetOrAdd(session.ClientId, _ => new Pc());
        pc.AccountId = session.AccountId ?? pc.AccountId;
        return pc;
    }

    /// <summary>A Ping: the Photon logger runs on this PC (photonLoggingEnabled).</summary>
    public void LoggerSeen(GameLogSession session)
    {
        var pc = PcOf(session);
        if (pc.Enabled) return;
        pc.Enabled = true;
        if (pc.AccountId is { } account) Publish(account, []);
    }

    /// <summary>The PC's instance may have changed: another one starts the lobby over (resetLocationPhotonState).</summary>
    public void Track(GameLogSession session)
    {
        if (!_pcs.TryGetValue(session.ClientId, out var pc)) return;
        pc.AccountId = session.AccountId ?? pc.AccountId;
        bool changed;
        lock (pc.Lobby)
        {
            changed = pc.Lobby.Location != session.CurrentInstanceLocation;
            if (changed) pc.Lobby.Reset(session.CurrentInstanceLocation);
        }
        if (changed && pc.AccountId is { } account) Publish(account, []);
    }

    /// <summary>The account's PC with a lobby, for the page.</summary>
    public PhotonSnapshot? Snapshot(string accountId)
    {
        var pc = _pcs.Values.FirstOrDefault(p => p.AccountId == accountId && p.Enabled) ?? _pcs.Values.FirstOrDefault(p => p.AccountId == accountId);
        if (pc is null) return null;
        var threshold = pc.Settings?.Settings.PhotonLobbyTimeoutThreshold ?? 6000;
        lock (pc.Lobby)
        {
            var l = pc.Lobby;
            var players = l.Current.Keys.Order().Select(id =>
            {
                var user = l.Lobby.GetValueOrDefault(id);
                return new PhotonPlayer(id, user?.Id, user?.DisplayName, id == l.Master, l.Joins.GetValueOrDefault(id));
            }).ToList();
            return new PhotonSnapshot(pc.Enabled, l.Location, l.Master, l.CurrentUser, players, l.Timeouts(_time.GetUtcNow(), threshold, l.Current.Count), [.. l.Events], [.. l.PreviousEvents]);
        }
    }

    public async Task HandleAsync(GameLogSession session, string json, CancellationToken ct)
    {
        JsonElement packet;
        try { packet = JsonSerializer.Deserialize<JsonElement>(json); }
        catch (JsonException) { return; }
        var pc = PcOf(session);
        pc.Enabled = true;
        // The original skipped the stream while the game was closed, but kept Event7List.
        var type = packet.ValueKind == JsonValueKind.Object && packet.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (type != "Event7List" && session.GameRunning != true) return;
        if (pc.AccountId is not { } accountId) return;

        var settings = await SettingsAsync(pc, accountId, ct);
        var photonSettings = new PhotonSettings(settings.ChatboxUserBlacklist, settings.ChatboxBlacklist);
        var now = _time.GetUtcNow();
        List<PhotonEffect> effects;
        int before, timeouts;
        PhotonFeedEntry? head;
        List<PhotonTimeout>? timeoutList;
        lock (pc.Lobby)
        {
            if (pc.Lobby.Location != session.CurrentInstanceLocation) pc.Lobby.Reset(session.CurrentInstanceLocation);
            before = pc.Lobby.Events.Count + pc.Lobby.Current.Count * 1000 + pc.Lobby.Master * 7;
            head = pc.Lobby.Events.FirstOrDefault();
            effects = pc.Lobby.Apply(packet, photonSettings, now);
            timeoutList = type == "Event7List" ? pc.Lobby.Timeouts(now, settings.PhotonLobbyTimeoutThreshold, pc.Lobby.Current.Count) : null;
            timeouts = timeoutList?.Count ?? pc.LastTimeouts;
        }

        var notys = new List<Dictionary<string, object?>>();
        foreach (var effect in effects)
        {
            try { await ApplyAsync(session, pc, accountId, effect, notys, now, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "A Photon {Effect} from {Client} could not be applied", effect.GetType().Name, session.ClientId);
            }
        }

        int after;
        List<PhotonFeedEntry> added;
        lock (pc.Lobby)
        {
            after = pc.Lobby.Events.Count + pc.Lobby.Current.Count * 1000 + pc.Lobby.Master * 7;
            // Newest first; the original's HUD keeps ten.
            added = [.. pc.Lobby.Events.TakeWhile(e => !ReferenceEquals(e, head)).Take(10)];
        }
        var timeoutsChanged = timeouts > 0 || pc.LastTimeouts > 0;
        pc.LastTimeouts = timeouts;
        try { await HudAsync(session, accountId, settings, added, timeoutsChanged ? timeoutList : null, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The Photon HUD for {Client} could not be sent", session.ClientId);
        }
        if (after != before || notys.Count > 0 || timeoutsChanged || type == "OnOperationResponse") Publish(accountId, notys);
    }

    /// <summary>The original's addEntryHudFeed and updateHudTimeout, to the PC's own SteamVR overlay.</summary>
    private async Task HudAsync(GameLogSession session, string accountId, AccountSettings settings, List<PhotonFeedEntry> added, List<PhotonTimeout>? timeouts, CancellationToken ct)
    {
        if (!settings.OpenVr || session.ClientId is not { } clientId || desktops.Get(clientId) is not { } desktop || !desktop.Can(Capability.Openvr)) return;
        var events = settings.PhotonEventOverlay ? added.Where(e => PhotonHud.TypeShown(settings.PhotonEventTypeOverlayFilter, e.Type)).ToList() : [];
        var sendTimeouts = settings.TimeoutHudOverlay && timeouts is not null;
        if (events.Count == 0 && !sendTimeouts) return;

        HashSet<string> friends, favorites;
        Dictionary<string, string?> colours;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var ids = events.Select(e => e.UserId).Concat(timeouts?.Select(t => t.UserId) ?? []).Where(id => id.Length > 0).Distinct().ToList();
            friends = (await db.Friendships.AsNoTracking().Where(f => f.OwnerUserId == accountId && ids.Contains(f.FriendUserId)).Select(f => f.FriendUserId).ToListAsync(ct)).ToHashSet();
            favorites = await FavoriteFriends.IdSetAsync(db, accountId, ct);
            // The original's tagRef.colour on each HUD line.
            colours = await db.CustomUserTags.AsNoTracking().Where(t => t.OwnerUserId == accountId && ids.Contains(t.UserId)).ToDictionaryAsync(t => t.UserId, t => t.Colour, ct);
        }

        var feed = new HudFeed { MessageTimeoutMs = settings.PhotonOverlayMessageTimeout, TimeoutsSet = sendTimeouts };
        foreach (var e in events)
        {
            bool friend = friends.Contains(e.UserId), favorite = favorites.Contains(e.UserId);
            if (PhotonHud.Passes(settings.PhotonEventOverlayFilter, e.UserId, friend, favorite)) feed.Added.Add(PhotonHud.Entry(e, friend, favorite, colours.GetValueOrDefault(e.UserId)));
        }
        if (sendTimeouts)
            feed.Timeouts.AddRange(timeouts!
                .Where(t => PhotonHud.Passes(settings.TimeoutHudOverlayFilter, t.UserId, friends.Contains(t.UserId), favorites.Contains(t.UserId)))
                .Select(t => new HudTimeout { DisplayName = t.DisplayName, Seconds = t.Time }));
        if (feed.Added.Count == 0 && !sendTimeouts) return;
        await desktops.PostAsync(clientId, new ClientAction { UpdateHud = feed }, ct);
    }

    private async Task<AccountSettings> SettingsAsync(Pc pc, string accountId, CancellationToken ct)
    {
        if (pc.Settings is { } cached && _time.GetUtcNow() - cached.At < TimeSpan.FromSeconds(30)) return cached.Settings;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => a.Settings).FirstOrDefaultAsync(ct) ?? new AccountSettings();
        pc.Settings = (settings, _time.GetUtcNow());
        return settings;
    }

    /// <summary>The account's settings were saved.</summary>
    public void SettingsChanged(string accountId)
    {
        foreach (var pc in _pcs.Values)
            if (pc.AccountId == accountId) pc.Settings = null;
    }

    private async Task ApplyAsync(GameLogSession session, Pc pc, string accountId, PhotonEffect effect, List<Dictionary<string, object?>> notys, DateTimeOffset now, CancellationToken ct)
    {
        switch (effect)
        {
            case PhotonGameLogEvent e:
                {
                    await using var db = await dbFactory.CreateDbContextAsync(ct);
                    db.GameLogEvents.Add(new GameLogEvent { At = e.At, InstanceLocation = session.CurrentInstanceLocation, Kind = "Event", Data = e.Data });
                    await db.SaveChangesAsync(ct);
                    bus.Publish(LiveEvents.GameLog(accountId, session.CurrentInstanceLocation, ["event"]));
                    break;
                }
            case PhotonNoty { Type: "AvatarChange" } n:
                {
                    // The log's avatar changes are stored, so the Photon ones are too (the log's step aside while Photon runs).
                    if (session.CurrentInstanceLocation is not { } instance || n.UserId is null) break;
                    await using var db = await dbFactory.CreateDbContextAsync(ct);
                    await store.EnsureUserAsync(db, n.UserId, n.DisplayName, now, ct);
                    db.GameLogEvents.Add(new GameLogEvent { At = n.At, InstanceLocation = instance, Kind = "AvatarChange", Data = n.Text ?? "", UserId = n.UserId, DisplayName = n.DisplayName });
                    await db.SaveChangesAsync(ct);
                    bus.Publish(LiveEvents.GameLog(accountId, instance, ["avatar"]));
                    break;
                }
            case PhotonNoty n:
                notys.Add(new() { ["type"] = n.Type, ["created_at"] = n.At, ["userId"] = n.UserId, ["displayName"] = n.DisplayName, ["text"] = n.Text });
                break;
            case PhotonPortalSpawn p:
                await PortalSpawnAsync(session, pc, accountId, p, now, ct);
                break;
            case PhotonGroupChange g:
                {
                    await using var db = await dbFactory.CreateDbContextAsync(ct);
                    g.Entry.GroupName = await GroupNameAsync(db, accountId, g.Entry.GroupId, ct);
                    g.Entry.PreviousGroupName = await GroupNameAsync(db, accountId, g.Entry.PreviousGroupId, ct);
                    lock (pc.Lobby) pc.Lobby.Add(new PhotonFeedEntry
                    {
                        PhotonId = g.Entry.PhotonId, DisplayName = g.Entry.DisplayName, UserId = g.Entry.UserId, Type = g.Entry.Type, CreatedAt = g.Entry.CreatedAt,
                        Text = $"ChangeGroup {g.Entry.GroupName}", GroupId = g.Entry.GroupId, GroupName = g.Entry.GroupName,
                        PreviousGroupId = g.Entry.PreviousGroupId, PreviousGroupName = g.Entry.PreviousGroupName,
                    });
                    break;
                }
            case PhotonModeration m:
                await ModerationAsync(pc, accountId, m, notys, now, ct);
                break;
            case PhotonAvatarSeen a:
                {
                    if (a.Avatar.Deserialize<Vrc.Avatar>(VrchatJson.Options) is not { } avatar) break;
                    await using var db = await dbFactory.CreateDbContextAsync(ct);
                    await store.UpsertAvatarAsync(db, avatar, now, ct);
                    await db.SaveChangesAsync(ct);
                    break;
                }
            case PhotonUserSeen u:
                {
                    // applyUser with the stream's avatar images, while the stream is live.
                    if (u.At.AddMinutes(1) <= now || u.User.Id is not { } userId || string.IsNullOrEmpty(u.User.CurrentAvatarImageUrl)) break;
                    await using var db = await dbFactory.CreateDbContextAsync(ct);
                    await db.Users.Where(x => x.Id == userId && x.CurrentAvatarImageUrl != u.User.CurrentAvatarImageUrl)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.CurrentAvatarImageUrl, u.User.CurrentAvatarImageUrl)
                            .SetProperty(x => x.CurrentAvatarThumbnailImageUrl, u.User.CurrentAvatarThumbnailImageUrl), ct);
                    break;
                }
        }
    }

    private async Task<string> GroupNameAsync(VrcxContext db, string accountId, string? groupId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(groupId)) return "";
        if (await db.Groups.AsNoTracking().Where(g => g.Id == groupId).Select(g => g.Name).FirstOrDefaultAsync(ct) is { Length: > 0 } name) return name;
        if (sessions.Get(accountId) is not { } client) return "";
        try { return (await client.GetGroupAsync(groupId, includeRoles: false, ct))?.Name ?? ""; }
        catch (Exception ex) when (ex is not OperationCanceledException) { return ""; }
    }

    /// <summary>addPhotonPortalSpawn: where the portal goes, then the table row and the game log row.</summary>
    private async Task PortalSpawnAsync(GameLogSession session, Pc pc, string accountId, PhotonPortalSpawn p, DateTimeOffset now, CancellationToken ct)
    {
        if (sessions.Get(accountId) is not { } client || p.ShortName.Length == 0) return;
        var instance = await client.GetInstanceFromShortNameAsync(p.ShortName, ct);
        if (instance is null) return;
        var location = instance.Location;
        var l = VrchatLocation.Parse(location);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var groupName = l.GroupId is { } groupId ? await GroupNameAsync(db, accountId, groupId, ct) : "";
        var worldName = p.WorldName;
        if (worldName.Length == 0)
            worldName = await db.Worlds.AsNoTracking().Where(w => w.Id == l.WorldId).Select(w => w.Name).FirstOrDefaultAsync(ct)
                ?? (await client.GetWorldAsync(l.WorldId, ct))?.Name ?? "";
        string? displayName = null;
        if (p.UserId is { } userId)
            displayName = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        lock (pc.Lobby)
        {
            pc.Lobby.Add(new PhotonFeedEntry
            {
                PhotonId = pc.Lobby.PhotonIdOfUser(p.UserId), Text = $"PortalSpawn to {PipelineIngestor.DisplayLocation(l, worldName, groupName)}", Type = "PortalSpawn",
                ShortName = p.ShortName, Location = location, WorldName = worldName, GroupName = groupName, CreatedAt = p.At,
            });
            displayName ??= pc.Lobby.Lobby.Values.FirstOrDefault(u => u?.Id == p.UserId)?.DisplayName;
        }
        if (session.CurrentInstanceLocation is null) return;
        if (p.UserId is { } owner) await store.EnsureUserAsync(db, owner, displayName, now, ct);
        db.PortalSpawns.Add(new PortalSpawn
        {
            At = p.At, InstanceLocation = session.CurrentInstanceLocation, UserId = p.UserId, DisplayName = displayName,
            TargetInstanceLocation = location, TargetWorldName = worldName,
        });
        await db.SaveChangesAsync(ct);
        bus.Publish(LiveEvents.GameLog(accountId, session.CurrentInstanceLocation, ["portal"]));
    }

    /// <summary>photonModerationUpdate against the stored row.</summary>
    private async Task ModerationAsync(Pc pc, string accountId, PhotonModeration m, List<Dictionary<string, object?>> notys, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.ModerationsAgainst.FirstOrDefaultAsync(r => r.OwnerUserId == accountId && r.UserId == m.UserId, ct);
        string? lastType;
        lock (pc.Lobby) lastType = pc.Lobby.LastModeration.GetValueOrDefault(m.PhotonId);
        var (type, text) = m.Block ? ("Blocked", "Blocked") : m.Mute ? ("Muted", "Muted") : ("", "");
        if (row is not null)
        {
            if (!m.Block && row.Block) (type, text) = ("Unblocked", "Unblocked");
            else if (!m.Mute && row.Mute) (type, text) = ("Unmuted", "Unmuted");
            if (m.Block == row.Block && m.Mute == row.Mute)
            {
                lock (pc.Lobby)
                {
                    if (type.Length > 0 && type != lastType)
                        pc.Lobby.Add(new PhotonFeedEntry { PhotonId = m.PhotonId, Text = $"Moderation {text}", Type = "Moderation", Color = "yellow", CreatedAt = m.At });
                    pc.Lobby.LastModeration[m.PhotonId] = type;
                }
                return;
            }
        }
        lock (pc.Lobby)
        {
            pc.Lobby.LastModeration[m.PhotonId] = type;
            if (type.Length > 0) pc.Lobby.Add(new PhotonFeedEntry { PhotonId = m.PhotonId, Text = $"Moderation {text}", Type = "Moderation", Color = "yellow", CreatedAt = m.At });
        }
        if (type.Length > 0) notys.Add(new() { ["type"] = type, ["created_at"] = now, ["userId"] = m.UserId, ["displayName"] = m.DisplayName });
        if (m.Block || m.Mute)
        {
            await store.EnsureUserAsync(db, m.UserId, m.DisplayName, now, ct);
            if (row is null)
            {
                row = new ModerationAgainst { OwnerUserId = accountId, UserId = m.UserId };
                db.ModerationsAgainst.Add(row);
            }
            row.DisplayName = m.DisplayName;
            row.Block = m.Block;
            row.Mute = m.Mute;
            row.UpdatedAt = m.At;
        }
        else if (row is not null)
        {
            db.ModerationsAgainst.Remove(row);
        }
        await db.SaveChangesAsync(ct);
    }

    private void Publish(string accountId, List<Dictionary<string, object?>> notys)
    {
        lock (_publishLock)
        {
            if (_pending.TryGetValue(accountId, out var list))
            {
                list.AddRange(notys);
                return;
            }
            _pending[accountId] = [.. notys];
        }
        _ = Task.Run(async () =>
        {
            await Task.Delay(PublishDelay);
            List<Dictionary<string, object?>> list;
            lock (_publishLock)
            {
                list = _pending[accountId];
                _pending.Remove(accountId);
            }
            bus.Publish(LiveEvents.PhotonUpdated(accountId, list.Count > 0 ? JsonSerializer.Serialize(list, JsonSerializerOptions.Web) : ""));
        });
    }
}
