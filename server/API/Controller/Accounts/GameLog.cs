using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.Internal.Common.Problems;
using VRCX.API.Models.Response;
using VRCX.Common.Errors;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Controller.Accounts;

// The Game Log page: every kind of game log row in one list, and instances with what happened in them.
public sealed partial class AccountsController
{
    private static readonly string[] GameLogTypes = ["Location", "OnPlayerJoined", "OnPlayerLeft", "PortalSpawn", "VideoPlay", "Event", "External", "StringLoad", "ImageLoad"];
    private static readonly string[] SessionEventTypes = ["OnPlayerJoined", "OnPlayerLeft", "VideoPlay"];

    /// <summary>The account's game log, newest first: places it went, players it saw, videos, portals, events and resource loads.</summary>
    /// <param name="accountId"></param>
    /// <param name="types">Comma separated types, or empty for all</param>
    /// <param name="search">Part of a name, world, url or message</param>
    /// <param name="vip">Only favorite friends among rows that name a user</param>
    /// <param name="limit">Most rows to return</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">The rows</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/game-log")]
    [ProducesResponseType<GameLogRowResponse[]>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListGameLog([FromRoute] string accountId, [FromQuery] string? types, [FromQuery] string? search, [FromQuery] bool vip, [FromQuery] int limit = 500, CancellationToken cancellationToken = default)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        // The client's Search Limit Returns setting goes up to 100000 (the original's SEARCH_LIMIT_MAX).
        limit = Math.Clamp(limit, 1, 100_000);
        // AvatarChange only when asked for: the original's game log table never lists it (its notifications do).
        var wanted = (types ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Intersect([.. GameLogTypes, "AvatarChange"]).ToHashSet();
        if (wanted.Count == 0) wanted = GameLogTypes.ToHashSet();

        var rows = await LoadGameLogRowsAsync(accountId, wanted, null, string.IsNullOrWhiteSpace(search) ? null : search.Trim(), limit, cancellationToken);
        var (friends, favorites) = await FriendSetsAsync(accountId, cancellationToken);
        var hidden = (await _db.GameLogHiddenEntries.AsNoTracking().Where(h => h.OwnerUserId == accountId).Select(h => h.EntryKey).ToListAsync(cancellationToken)).ToHashSet();

        return Ok(rows
            .Where(r => !hidden.Contains(r.Key))
            .Where(r => !vip || r.Type is "Location" or "Event" or "StringLoad" or "ImageLoad" || (r.UserId is not null && favorites.Contains(r.UserId)))
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Key, StringComparer.Ordinal)
            .Take(limit)
            .Select(r => r.WithFriend(friends, favorites))
            .ToArray());
    }

    /// <summary>Instances the account was in, newest first, with who came and went and what played there.</summary>
    /// <param name="accountId"></param>
    /// <param name="beforeId">Continue after this location row</param>
    /// <param name="limit">Most instances to return</param>
    /// <param name="from">Only stays that started after this</param>
    /// <param name="to">Only stays that started before this</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">The instances and their rows</response>
    /// <response code="404">No such account</response>
    [HttpGet("{accountId}/game-log/sessions")]
    [ProducesResponseType<GameLogSessionsResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListGameLogSessions([FromRoute] string accountId, [FromQuery] long? beforeId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] int limit = 10, CancellationToken cancellationToken = default)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        limit = Math.Clamp(limit, 1, 200);

        var query = _db.InstanceVisits.AsNoTracking().Where(v => v.UserId == accountId && v.Source == VisitSource.GameLog);
        if (beforeId is { } before)
        {
            var anchor = await query.Where(v => v.Id == before).Select(v => (DateTimeOffset?)v.JoinedAt).FirstOrDefaultAsync(cancellationToken);
            if (anchor is { } at) query = query.Where(v => v.JoinedAt < at || (v.JoinedAt == at && v.Id < before));
        }
        if (from is { } since) query = query.Where(v => v.JoinedAt >= since);
        if (to is { } until) query = query.Where(v => v.JoinedAt <= until);

        var found = await query.OrderByDescending(v => v.JoinedAt).ThenByDescending(v => v.Id).Take(limit + 1)
            .Select(v => new GameLogRowResponse
            {
                Key = $"Location:{v.Id}", Id = v.Id, Type = "Location", CreatedAt = v.JoinedAt, Location = v.InstanceLocation,
                WorldId = v.Instance.WorldId, WorldName = v.Instance.World.Name, GroupName = v.Instance.Group != null ? v.Instance.Group.Name : null,
                TimeMs = v.LeftAt != null ? (long)(v.LeftAt.Value - v.JoinedAt).TotalMilliseconds : null,
            })
            .ToListAsync(cancellationToken);
        var hasMore = found.Count > limit;
        var locations = found.Take(limit).ToList();
        if (locations.Count == 0) return Ok(new GameLogSessionsResponse { Locations = [], Events = [], HasMore = false });

        var scope = new GameLogRange(locations.Select(l => l.Location!).Distinct().ToArray(), locations.Min(l => l.CreatedAt).AddSeconds(-1));
        var events = await LoadGameLogRowsAsync(accountId, SessionEventTypes.ToHashSet(), scope, null, 20000, cancellationToken);
        var (friends, favorites) = await FriendSetsAsync(accountId, cancellationToken);
        return Ok(new GameLogSessionsResponse
        {
            Locations = locations,
            Events = events.Select(r => r.WithFriend(friends, favorites)).ToArray(),
            HasMore = hasMore,
        });
    }

    /// <summary>Remove a row from the account's game log view. The row itself stays in the database.</summary>
    /// <response code="204">Removed</response>
    /// <response code="404">No such account</response>
    [HttpDelete("{accountId}/game-log/{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<OpenShockProblem>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> HideGameLogEntry([FromRoute] string accountId, [FromRoute] string key, CancellationToken cancellationToken)
    {
        if (!await _db.Accounts.AnyAsync(a => a.UserId == accountId, cancellationToken)) return Problem(AccountError.AccountNotFound);
        if (!await _db.GameLogHiddenEntries.AnyAsync(h => h.OwnerUserId == accountId && h.EntryKey == key, cancellationToken))
        {
            _db.GameLogHiddenEntries.Add(new GameLogHiddenEntry { OwnerUserId = accountId, EntryKey = key, HiddenAt = DateTimeOffset.UtcNow });
            await _db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }

    private sealed record GameLogRange(string[] Locations, DateTimeOffset Since);

    private async Task<(HashSet<string> Friends, HashSet<string> Favorites)> FriendSetsAsync(string accountId, CancellationToken cancellationToken)
    {
        var friends = (await _db.Friendships.AsNoTracking().Where(f => f.OwnerUserId == accountId && f.EndedAt == null).Select(f => f.FriendUserId).ToListAsync(cancellationToken)).ToHashSet();
        var favorites = await VRCX.Common.Services.FavoriteFriends.IdSetAsync(_db, accountId, cancellationToken);
        return (friends, favorites);
    }

    private async Task<List<GameLogRowResponse>> LoadGameLogRowsAsync(string accountId, HashSet<string> types, GameLogRange? range, string? search, int limit, CancellationToken ct)
    {
        var like = search is null ? null : $"%{search.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_")}%";
        // The tables hold what every PC reported: only instances this account was in are its own log.
        var mine = _db.InstanceVisits.AsNoTracking().Where(v => v.UserId == accountId && v.Source == VisitSource.GameLog).Select(v => v.InstanceLocation).Distinct();
        var rows = new List<GameLogRowResponse>();

        if (types.Contains("Location"))
        {
            var q = _db.InstanceVisits.AsNoTracking().Where(v => v.UserId == accountId && v.Source == VisitSource.GameLog);
            if (like is not null) q = q.Where(v => EF.Functions.ILike(v.Instance.World.Name, like) || EF.Functions.ILike(v.InstanceLocation, like));
            rows.AddRange(await q.OrderByDescending(v => v.JoinedAt).Take(limit).Select(v => new GameLogRowResponse
            {
                Key = $"Location:{v.Id}", Id = v.Id, Type = "Location", CreatedAt = v.JoinedAt, Location = v.InstanceLocation, WorldId = v.Instance.WorldId,
                WorldName = v.Instance.World.Name, GroupName = v.Instance.Group != null ? v.Instance.Group.Name : null,
                TimeMs = v.LeftAt != null ? (long)(v.LeftAt.Value - v.JoinedAt).TotalMilliseconds : null,
            }).ToListAsync(ct));
        }

        if (types.Contains("OnPlayerJoined") || types.Contains("OnPlayerLeft"))
        {
            var q = _db.InstanceVisits.AsNoTracking().Where(v => v.UserId != accountId && v.Source == VisitSource.GameLog && mine.Contains(v.InstanceLocation));
            if (range is not null) q = q.Where(v => range.Locations.Contains(v.InstanceLocation) && (v.JoinedAt >= range.Since || (v.LeftAt != null && v.LeftAt >= range.Since)));
            if (like is not null) q = q.Where(v => EF.Functions.ILike(v.User.DisplayName, like));
            if (types.Contains("OnPlayerJoined"))
                rows.AddRange((await q.OrderByDescending(v => v.JoinedAt).Take(limit).Select(v => new GameLogRowResponse
                {
                    Key = $"OnPlayerJoined:{v.Id}", Id = v.Id, Type = "OnPlayerJoined", CreatedAt = v.JoinedAt, Location = v.InstanceLocation, UserId = v.UserId, DisplayName = v.User.DisplayName,
                }).ToListAsync(ct)).DistinctBy(r => (r.UserId, r.Location, r.CreatedAt)));
            if (types.Contains("OnPlayerLeft"))
                rows.AddRange((await q.Where(v => v.LeftAt != null).OrderByDescending(v => v.LeftAt).Take(limit).Select(v => new GameLogRowResponse
                {
                    Key = $"OnPlayerLeft:{v.Id}", Id = v.Id, Type = "OnPlayerLeft", CreatedAt = v.LeftAt!.Value, Location = v.InstanceLocation, UserId = v.UserId, DisplayName = v.User.DisplayName,
                    TimeMs = (long)(v.LeftAt!.Value - v.JoinedAt).TotalMilliseconds,
                }).ToListAsync(ct)).DistinctBy(r => (r.UserId, r.Location, r.CreatedAt)));
        }

        if (types.Contains("VideoPlay"))
        {
            var q = _db.VideoPlays.AsNoTracking().Where(v => v.InstanceLocation != null && mine.Contains(v.InstanceLocation));
            if (range is not null) q = q.Where(v => range.Locations.Contains(v.InstanceLocation!) && v.At >= range.Since);
            if (like is not null) q = q.Where(v => EF.Functions.ILike(v.VideoUrl, like) || (v.VideoName != null && EF.Functions.ILike(v.VideoName, like)) || (v.DisplayName != null && EF.Functions.ILike(v.DisplayName, like)));
            rows.AddRange(await q.OrderByDescending(v => v.At).Take(limit).Select(v => new GameLogRowResponse
            {
                Key = $"VideoPlay:{v.Id}", Id = v.Id, Type = "VideoPlay", CreatedAt = v.At, Location = v.InstanceLocation, UserId = v.UserId, DisplayName = v.DisplayName,
                VideoUrl = v.VideoUrl, VideoName = v.VideoName, VideoId = v.VideoId, VideoPos = v.VideoPos, VideoLength = v.VideoLength,
            }).ToListAsync(ct));
        }

        if (types.Contains("PortalSpawn"))
        {
            var q = _db.PortalSpawns.AsNoTracking().Where(p => p.InstanceLocation != null && mine.Contains(p.InstanceLocation));
            if (like is not null) q = q.Where(p => (p.DisplayName != null && EF.Functions.ILike(p.DisplayName, like)) || (p.TargetWorldName != null && EF.Functions.ILike(p.TargetWorldName, like)));
            rows.AddRange(await q.OrderByDescending(p => p.At).Take(limit).Select(p => new GameLogRowResponse
            {
                Key = $"PortalSpawn:{p.Id}", Id = p.Id, Type = "PortalSpawn", CreatedAt = p.At, Location = p.InstanceLocation, UserId = p.UserId, DisplayName = p.DisplayName,
                InstanceId = p.TargetInstanceLocation, WorldName = p.TargetWorldName,
            }).ToListAsync(ct));
        }

        if (types.Contains("Event") || types.Contains("External") || types.Contains("AvatarChange"))
        {
            var q = _db.GameLogEvents.AsNoTracking().Where(e => types.Contains(e.Kind) && (e.InstanceLocation == null || mine.Contains(e.InstanceLocation)));
            if (like is not null) q = q.Where(e => EF.Functions.ILike(e.Data, like) || (e.DisplayName != null && EF.Functions.ILike(e.DisplayName, like)));
            rows.AddRange(await q.OrderByDescending(e => e.At).Take(limit).Select(e => new GameLogRowResponse
            {
                Key = $"{e.Kind}:{e.Id}", Id = e.Id, Type = e.Kind, CreatedAt = e.At, Location = e.InstanceLocation, UserId = e.UserId, DisplayName = e.DisplayName,
                Data = e.Kind == "Event" || e.Kind == "AvatarChange" ? e.Data : null, Message = e.Kind == "External" ? e.Data : null,
            }).ToListAsync(ct));
        }

        if (types.Contains("StringLoad") || types.Contains("ImageLoad"))
        {
            var kinds = new[] { types.Contains("StringLoad") ? "string" : "", types.Contains("ImageLoad") ? "image" : "" };
            var q = _db.ResourceLoads.AsNoTracking().Where(r => kinds.Contains(r.Kind) && r.InstanceLocation != null && mine.Contains(r.InstanceLocation));
            if (like is not null) q = q.Where(r => EF.Functions.ILike(r.Url, like));
            rows.AddRange(await q.OrderByDescending(r => r.At).Take(limit).Select(r => new GameLogRowResponse
            {
                Key = $"{(r.Kind == "image" ? "ImageLoad" : "StringLoad")}:{r.Id}", Id = r.Id, Type = r.Kind == "image" ? "ImageLoad" : "StringLoad", CreatedAt = r.At, Location = r.InstanceLocation, ResourceUrl = r.Url,
            }).ToListAsync(ct));
        }

        return rows;
    }
}
