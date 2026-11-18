using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using VRCX.Common.Services.Queries;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Mcp;

/// <summary>A game log row. Which fields are set depends on Type.</summary>
public sealed record McpGameLogRow(
    string Type, DateTimeOffset At, McpLocation? Location, string? UserId, string? DisplayName, bool IsFriend, long? TimeMs,
    string? VideoUrl, string? VideoName, string? PortalTo, string? Text);

public sealed record McpSessionPlayer(string UserId, string DisplayName, bool IsFriend);

public sealed record McpGameSession(
    long SessionId, McpLocation Location, DateTimeOffset JoinedAt, long? TimeMs, int PlayerCount,
    IReadOnlyList<McpSessionPlayer> Players, bool PlayersTruncated, int VideoCount);

public sealed record McpGameSessions(McpAccount Account, IReadOnlyList<McpGameSession> Sessions, bool Truncated, long? NextBeforeSessionId);

public sealed record McpInstancePlayer(string UserId, string DisplayName, DateTimeOffset JoinedAt, bool IsFriend, string? Memo, string? Note, bool IsBlocked, bool IsMuted);

public sealed record McpCurrentInstance(McpAccount Account, bool InInstance, McpLocation? Location, DateTimeOffset? JoinedAt, McpPage<McpInstancePlayer> Players);

public sealed record McpPreviousInstance(McpLocation Location, string? OwnerUserId, string? OwnerName, DateTimeOffset FirstJoinedAt, long TimeMs);

public sealed record McpPreviousPlayer(string UserId, string DisplayName, DateTimeOffset FirstJoinedAt, long TimeMs, int Stays, bool IsFriend, bool IsFavorite);

[McpServerToolType]
public sealed class GameLogTools(McpAccountResolver accounts, GameLogQueries gameLog, PreviousInstanceQueries previous, VrcxContext db)
{
    private const int SessionPlayerCap = 50;

    [McpServerTool(Name = "get_game_log", Title = "Game log", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        The account's game log, newest first, as the VRCXD agent on the account's PC shipped it:
        Location (the account joined an instance; timeMs is how long it stayed), OnPlayerJoined and
        OnPlayerLeft (others in the same instance; timeMs on a leave is how long they were there),
        VideoPlay, PortalSpawn, Event, External, StringLoad, ImageLoad. search matches player
        names, world names, video titles and urls, and messages. Only covers time the agent was
        running while the account played: no rows for a period means it was not logged, not that
        the account did not play.
        """)]
    public async Task<McpPage<McpGameLogRow>> GetGameLog(
        [Description("usr_ id or display name of the logged-in account; optional when only one account exists.")] string? account = null,
        [Description("Only these types, comma separated: Location, OnPlayerJoined, OnPlayerLeft, VideoPlay, PortalSpawn, Event, External, StringLoad, ImageLoad. Default all.")] string? types = null,
        [Description("Text to search for (case-insensitive).")] string? search = null,
        [Description("Rows to return, 1-200 (default 50).")] int limit = McpPaging.DefaultLimit,
        [Description("Rows to skip, from a previous result's nextOffset.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var who = await accounts.ResolveAsync(account, cancellationToken);
        offset = McpPaging.Offset(offset);
        limit = McpPaging.Limit(limit);
        var rows = await gameLog.ListAsync(who.Id, types, search, vip: false, offset + limit + 1, cancellationToken);
        var page = McpPaging.Page(who, rows, offset, limit);
        var name = await McpLocations.NamerAsync(db, page.Items.Select(r => r.Location), cancellationToken);
        return new McpPage<McpGameLogRow>(who, page.Items.Select(r => new McpGameLogRow(
            r.Type, r.CreatedAt, name(r.Location) is { } l ? l with { WorldName = r.Type == "Location" ? r.WorldName ?? l.WorldName : l.WorldName } : null,
            r.UserId, r.DisplayName, r.IsFriend, r.TimeMs, r.VideoUrl, r.VideoName,
            r.Type == "PortalSpawn" ? r.InstanceId ?? r.WorldName : null,
            r.Data ?? r.Message ?? r.ResourceUrl)).ToList(), page.Offset, page.Truncated, page.NextOffset);
    }

    [McpServerTool(Name = "get_game_sessions", Title = "Game sessions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        The instances the account was in, newest first, each with when it joined, how long it stayed
        (timeMs null while still there), who the game log saw there (players, friends marked) and how
        many videos played. Answers "what did I do yesterday", "who was I with in X". For more, call
        again with beforeSessionId = nextBeforeSessionId. Only covers sessions the agent logged on
        the account's PC.
        """)]
    public async Task<McpGameSessions> GetGameSessions(
        [Description("usr_ id or display name of the logged-in account; optional when only one account exists.")] string? account = null,
        [Description("Only sessions that started at or after this instant (ISO 8601).")] DateTimeOffset? from = null,
        [Description("Only sessions that started at or before this instant (ISO 8601).")] DateTimeOffset? to = null,
        [Description("Continue after this session, from a previous result's nextBeforeSessionId.")] long? beforeSessionId = null,
        [Description("Sessions to return, 1-50 (default 10).")] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var who = await accounts.ResolveAsync(account, cancellationToken);
        var result = await gameLog.SessionsAsync(who.Id, beforeSessionId, from, to, Math.Clamp(limit, 1, 50), cancellationToken);
        var name = await McpLocations.NamerAsync(db, result.Locations.Select(l => l.Location), cancellationToken);
        var sessions = result.Locations.Select(s =>
        {
            var end = s.TimeMs is { } ms ? s.CreatedAt.AddMilliseconds(ms) : DateTimeOffset.MaxValue;
            var here = result.Events.Where(e => e.Location == s.Location && e.CreatedAt >= s.CreatedAt && e.CreatedAt <= end).ToList();
            var players = here.Where(e => e.Type == "OnPlayerJoined" && e.UserId is not null)
                .DistinctBy(e => e.UserId)
                .Select(e => new McpSessionPlayer(e.UserId!, e.DisplayName ?? e.UserId!, e.IsFriend))
                .OrderByDescending(p => p.IsFriend).ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var location = name(s.Location)!;
            return new McpGameSession(s.Id, location with { WorldName = s.WorldName ?? location.WorldName, GroupName = s.GroupName ?? location.GroupName },
                s.CreatedAt, s.TimeMs, players.Count, players.Take(SessionPlayerCap).ToList(), players.Count > SessionPlayerCap, here.Count(e => e.Type == "VideoPlay"));
        }).ToList();
        return new McpGameSessions(who, sessions, result.HasMore, result.HasMore ? sessions[^1].SessionId : null);
    }

    [McpServerTool(Name = "get_current_instance", Title = "Current instance", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        The instance the account is in now and who is there, as the Player List page shows it: from
        the agent's game log when it is running, else from VRChat's presence for the account (then
        players only lists those the server saw arrive). inInstance false means the account is not
        in an instance as far as the server knows. Players carry the memo, the account's note, and
        whether the account blocked or muted them.
        """)]
    public async Task<McpCurrentInstance> GetCurrentInstance(
        [Description("usr_ id or display name of the logged-in account; optional when only one account exists.")] string? account = null,
        [Description("Players to return, 1-200 (default 50).")] int limit = McpPaging.DefaultLimit,
        [Description("Players to skip, from a previous result's nextOffset.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var who = await accounts.ResolveAsync(account, cancellationToken);
        var list = await gameLog.PlayerListAsync(who.Id, cancellationToken);
        var name = await McpLocations.NamerAsync(db, [list.Location], cancellationToken);
        var location = name(list.Location);
        if (location is not null && list.World is not null) location = location with { WorldName = list.World.Name };
        var players = list.Players.Select(p => new McpInstancePlayer(p.UserId, p.DisplayName, p.JoinedAt, p.IsFriend,
            string.IsNullOrEmpty(p.Memo) ? null : p.Memo, string.IsNullOrEmpty(p.Note) ? null : p.Note, p.IsBlocked, p.IsMuted));
        return new McpCurrentInstance(who, list.Location is not null, location, list.JoinedAt, McpPaging.Page(null, players, offset, limit, list.Players.Count));
    }

    [McpServerTool(Name = "get_previous_instances", Title = "Previous instances", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        Past instances, newest first, with the time spent in each. Give exactly one of: userId (the
        instances that user was seen in, by any source; a user's stays in one instance split into
        separate rows when more than an hour apart), worldId or groupId (the account's own
        instances of that world or group). Only covers stays the server observed.
        """)]
    public async Task<McpPage<McpPreviousInstance>> GetPreviousInstances(
        [Description("usr_ id of a user.")] string? userId = null,
        [Description("wrld_ id of a world.")] string? worldId = null,
        [Description("grp_ id of a group.")] string? groupId = null,
        [Description("usr_ id or display name of the logged-in account; optional when only one account exists.")] string? account = null,
        [Description("Rows to return, 1-200 (default 50).")] int limit = McpPaging.DefaultLimit,
        [Description("Rows to skip, from a previous result's nextOffset.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (new[] { userId, worldId, groupId }.Count(v => !string.IsNullOrWhiteSpace(v)) != 1)
            throw new McpException("Give exactly one of userId, worldId or groupId.");
        var who = await accounts.ResolveAsync(account, cancellationToken);
        var rows = await previous.ListAsync(who.Id, NullIfBlank(userId), NullIfBlank(worldId), NullIfBlank(groupId), cancellationToken);
        var name = await McpLocations.NamerAsync(db, rows.Select(r => r.Location), cancellationToken);
        return McpPaging.Page(who, rows.Select(r =>
        {
            var location = name(r.Location)!;
            return new McpPreviousInstance(location with { WorldName = r.WorldName, GroupName = r.GroupName ?? location.GroupName }, r.OwnerUserId, r.OwnerName, r.CreatedAt, r.TimeMs);
        }), offset, limit, rows.Count);
    }

    [McpServerTool(Name = "get_instance_players", Title = "Instance players", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        Everyone the server saw in one instance (a location tag such as wrld_...:12345~region(eu),
        from get_game_sessions or get_previous_instances), most time spent first: when they first
        joined, total time, number of stays, and whether they are the account's friend or favorite.
        Only covers stays the server observed.
        """)]
    public async Task<McpPage<McpPreviousPlayer>> GetInstancePlayers(
        [Description("The instance's full location tag.")] string location,
        [Description("usr_ id or display name of the logged-in account; optional when only one account exists.")] string? account = null,
        [Description("Rows to return, 1-200 (default 50).")] int limit = McpPaging.DefaultLimit,
        [Description("Rows to skip, from a previous result's nextOffset.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var tag = McpLocations.RequireId(location, "wrld_", "location", "Pass a full location tag from get_game_sessions or get_previous_instances.");
        var who = await accounts.ResolveAsync(account, cancellationToken);
        var result = await previous.PlayersAsync(who.Id, tag, cancellationToken);
        return McpPaging.Page(who, result.Players.Select(p => new McpPreviousPlayer(p.UserId, p.DisplayName, p.FirstJoinedAt, p.TimeMs, p.Count, p.IsFriend, p.IsFavorite)),
            offset, limit, result.Players.Count);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
