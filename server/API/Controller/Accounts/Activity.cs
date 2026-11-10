using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.API.Controller.Accounts;

/// <summary>A friend going online or offline, the rows the original's activity engine builds sessions from.</summary>
public sealed record ActivityPresenceRow(DateTimeOffset CreatedAt, string Type);

/// <summary>One stay of the account in an instance: when it began and how long it lasted, 0 while still there.</summary>
public sealed record ActivityVisitRow(DateTimeOffset CreatedAt, long Time);

public sealed class UserActivityResponse
{
    /// <summary>The user's online and offline events, with the last one before the period first so a session already running is known. Empty for the account itself.</summary>
    public required IReadOnlyList<ActivityPresenceRow> Events { get; init; }
    /// <summary>The account's own stays, for its own activity and for the overlap with a friend.</summary>
    public required IReadOnlyList<ActivityVisitRow> SelfVisits { get; init; }
}

public sealed class TopWorldResponse
{
    public required string WorldId { get; init; }
    public required string WorldName { get; init; }
    public string? ThumbnailImageUrl { get; init; }
    public required int VisitCount { get; init; }
    public required long TotalTime { get; init; }
}

// The original's Activity tab of the user pop-up (its activity store and gamelog queries): the
// rows for the online heatmap, the overlap with a friend, daily playtime and most visited worlds.
public sealed partial class AccountsController
{
    /// <summary>The rows the Activity tab builds its charts from, over the last <paramref name="days"/> days, or everything for 0.</summary>
    /// <response code="200">The rows, oldest first</response>
    [HttpGet("{accountId}/users/{userId}/activity")]
    [ProducesResponseType<UserActivityResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> GetUserActivity([FromRoute] string accountId, [FromRoute] string userId, [FromQuery] int days, CancellationToken cancellationToken)
    {
        DateTimeOffset? from = days > 0 ? DateTimeOffset.UtcNow.AddDays(-days) : null;

        var events = new List<ActivityPresenceRow>();
        if (userId != accountId)
        {
            var online = _db.PresenceEvents.AsNoTracking()
                .Where(e => e.UserId == userId && (e.Kind == PresenceKind.Online || e.Kind == PresenceKind.Offline));
            if (from is { } start)
            {
                var boundary = await online.Where(e => e.At < start).OrderByDescending(e => e.At).ThenByDescending(e => e.Id).FirstOrDefaultAsync(cancellationToken);
                if (boundary is not null) events.Add(Row(boundary));
                online = online.Where(e => e.At >= start);
            }
            events.AddRange((await online.OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync(cancellationToken)).Select(Row));
        }

        var stays = await OwnStaysAsync(accountId, from, cancellationToken);
        // A stay that began before the period but runs into it still counts toward it.
        if (from is { } since) stays = stays.Where(s => s.JoinedAt >= since || s.LeftAt is null || s.LeftAt > since).ToList();

        return Ok(new UserActivityResponse
        {
            Events = events,
            SelfVisits = stays.Select(s => new ActivityVisitRow(s.JoinedAt, StayMs(s.JoinedAt, s.LeftAt))).ToList(),
        });

        static ActivityPresenceRow Row(PresenceEvent e) => new(e.At, e.Kind == PresenceKind.Online ? "Online" : "Offline");
    }

    /// <summary>The worlds this account spent the most time in, or visited most, the original's getMyTopWorlds.</summary>
    /// <response code="200">The worlds, best first</response>
    [HttpGet("{accountId}/top-worlds")]
    [ProducesResponseType<IReadOnlyList<TopWorldResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [MapToApiVersion("1")]
    public async Task<IActionResult> ListTopWorlds([FromRoute] string accountId, [FromQuery] int days, [FromQuery] int limit = 5, [FromQuery] string sortBy = "time", [FromQuery] string? excludeWorldId = null, CancellationToken cancellationToken = default)
    {
        DateTimeOffset? from = days > 0 ? DateTimeOffset.UtcNow.AddDays(-days) : null;
        var stays = await OwnStaysAsync(accountId, from, cancellationToken);
        var rows = stays.Where(s => (from is null || s.JoinedAt >= from) && (string.IsNullOrEmpty(excludeWorldId) || s.WorldId != excludeWorldId));
        var worlds = rows.GroupBy(r => r.WorldId).Select(g => new TopWorldResponse
        {
            WorldId = g.Key,
            WorldName = g.Select(r => r.WorldName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? g.Key,
            ThumbnailImageUrl = g.Select(r => r.ThumbnailImageUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u)),
            VisitCount = g.Count(),
            TotalTime = g.Sum(r => StayMs(r.JoinedAt, r.LeftAt)),
        });
        worlds = sortBy == "count" ? worlds.OrderByDescending(w => w.VisitCount) : worlds.OrderByDescending(w => w.TotalTime);
        return Ok(worlds.Take(Math.Clamp(limit, 1, 100)).ToList());
    }

    private sealed record OwnStay(string Location, string WorldId, string? WorldName, string? ThumbnailImageUrl, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt, VisitSource Source);

    /// <summary>
    /// The account's own stays as the original's gamelog_location has them: one row per stay, from the
    /// game log. The VRChat connection records the same stays again; those count only where no game log
    /// covers them (the desktop app was not running). Stored rows are read as they are; only what is
    /// returned is corrected.
    /// </summary>
    private async Task<List<OwnStay>> OwnStaysAsync(string accountId, DateTimeOffset? from, CancellationToken ct)
    {
        var visits = _db.InstanceVisits.AsNoTracking().Where(v => v.UserId == accountId);
        // A day before the period, so a stay running into it and the stay after an open one are known.
        if (from is { } since) visits = visits.Where(v => v.JoinedAt >= since.AddDays(-1) || v.LeftAt == null || v.LeftAt > since);
        var rows = await visits.OrderBy(v => v.JoinedAt)
            .Select(v => new OwnStay(v.InstanceLocation, v.Instance.WorldId, v.Instance.World.Name, v.Instance.World.ThumbnailImageUrl, v.JoinedAt, v.LeftAt, v.Source))
            .ToListAsync(ct);

        // A replayed log can store the same entry twice; the copy that was closed first is the real one.
        var gameLog = rows.Where(r => r.Source == VisitSource.GameLog)
            .GroupBy(r => (r.Location, r.JoinedAt))
            .Select(g => g.OrderBy(r => r.LeftAt ?? DateTimeOffset.MaxValue).First())
            .ToList();
        // An open stay with a later stay somewhere else was never closed (the desktop app stopped sending);
        // it ended when that next stay began. Only the latest one may still be running.
        for (var i = 0; i < gameLog.Count; i++)
        {
            var stay = gameLog[i];
            if (stay.LeftAt is not null) continue;
            var next = rows.FirstOrDefault(r => r.JoinedAt > stay.JoinedAt && r.Location != stay.Location);
            if (next is not null) gameLog[i] = stay with { LeftAt = next.JoinedAt };
        }
        bool Covered(OwnStay s) => gameLog.Any(g => g.JoinedAt < (s.LeftAt ?? DateTimeOffset.MaxValue) && s.JoinedAt < (g.LeftAt ?? DateTimeOffset.MaxValue));
        var connection = rows.Where(r => r.Source != VisitSource.GameLog && !Covered(r)).DistinctBy(r => (r.Location, r.JoinedAt));
        return [.. gameLog.Concat(connection).OrderBy(s => s.JoinedAt)];
    }
}
