using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.GameLog;

public sealed class PreviousInstanceResponse
{
    public required string Location { get; init; }
    public required string WorldId { get; init; }
    public required string WorldName { get; init; }
    public string? GroupName { get; init; }
    public string? OwnerUserId { get; init; }
    public string? OwnerName { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required long TimeMs { get; init; }
}

public sealed class PreviousInstancePlayerResponse
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required DateTimeOffset FirstJoinedAt { get; init; }
    public required long TimeMs { get; init; }
    public required int Count { get; init; }
    /// <summary>The original's gameLogIsFriend and gameLogIsFavorite, for the heart and star (upstream PR 777).</summary>
    public bool IsFriend { get; init; }
    public bool IsFavorite { get; init; }
}

public sealed class PreviousInstanceStayResponse
{
    public required long Id { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required DateTimeOffset JoinedAt { get; init; }
    public DateTimeOffset? LeftAt { get; init; }
    public bool IsFriend { get; set; }
    public bool IsFavorite { get; set; }
}

public sealed class PreviousInstancePlayersResponse
{
    public required IReadOnlyList<PreviousInstancePlayerResponse> Players { get; init; }
    public required IReadOnlyList<PreviousInstanceStayResponse> Stays { get; init; }
}

/// <summary>
/// The original's Previous Instances dialogs, read from the stored visits. Rows repeated by the log
/// ingest count once here, so they don't inflate the times shown. The rows themselves stay in the database.
/// </summary>
public sealed class PreviousInstanceQueries(VrcxContext db)
{
    /// <summary>Instances a user was seen in, or this account's own instances of a world or group. One row per instance, newest first.</summary>
    public async Task<List<PreviousInstanceResponse>> ListAsync(string accountId, string? userId, string? worldId, string? groupId, CancellationToken cancellationToken)
    {
        var visits = db.InstanceVisits.AsNoTracking();
        visits = userId is not null ? visits.Where(v => v.UserId == userId)
            : worldId is not null ? visits.Where(v => v.UserId == accountId && v.Instance.WorldId == worldId)
            : groupId is not null ? visits.Where(v => v.UserId == accountId && v.Instance.GroupId == groupId)
            : visits.Where(v => false);

        var rows = await visits.Select(v => new
        {
            v.InstanceLocation, v.JoinedAt, v.LeftAt,
            v.Instance.WorldId, WorldName = v.Instance.World.Name,
            GroupName = v.Instance.Group != null ? v.Instance.Group.Name : null,
            v.Instance.OwnerUserId,
            OwnerName = v.Instance.OwnerUser != null ? v.Instance.OwnerUser.DisplayName : null,
        }).ToListAsync(cancellationToken);

        // The original's getPreviousInstancesByUserId: a user's stays in one instance are one row only
        // while they follow each other within an hour, so a 24/7 instance gets a row per session
        // (upstream PR 1305). Worlds and groups keep one row per instance.
        var sessions = userId is null
            ? rows.GroupBy(r => r.InstanceLocation).Select(g => g.ToList()).ToList()
            : SplitSessions(rows.DistinctBy(r => (r.InstanceLocation, r.JoinedAt, r.LeftAt)).OrderBy(r => r.JoinedAt).ToList(), r => r.InstanceLocation, r => r.JoinedAt, r => r.LeftAt);

        return sessions.Select(g =>
        {
            var first = g.First();
            return new PreviousInstanceResponse
            {
                Location = first.InstanceLocation,
                WorldId = first.WorldId,
                WorldName = first.WorldName,
                GroupName = first.GroupName,
                OwnerUserId = first.OwnerUserId,
                OwnerName = first.OwnerName,
                CreatedAt = g.Min(r => r.JoinedAt),
                TimeMs = g.DistinctBy(r => (r.JoinedAt, r.LeftAt)).Sum(r => StayMs(r.JoinedAt, r.LeftAt)),
            };
        }).OrderByDescending(r => r.CreatedAt).ToList();
    }

    /// <summary>Who was seen in one instance: a total per player, and each stay for the chart.</summary>
    public async Task<PreviousInstancePlayersResponse> PlayersAsync(string accountId, string location, CancellationToken cancellationToken)
    {
        var rows = await db.InstanceVisits.AsNoTracking()
            .Where(v => v.InstanceLocation == location)
            .OrderBy(v => v.JoinedAt).ThenBy(v => v.Id)
            .Select(v => new PreviousInstanceStayResponse { Id = v.Id, UserId = v.UserId, DisplayName = v.User.DisplayName, JoinedAt = v.JoinedAt, LeftAt = v.LeftAt })
            .ToListAsync(cancellationToken);
        var stays = rows.DistinctBy(r => (r.UserId, r.JoinedAt, r.LeftAt)).ToList();
        var friends = (await db.Friendships.AsNoTracking().Where(f => f.OwnerUserId == accountId && f.EndedAt == null).Select(f => f.FriendUserId).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var favorites = await FavoriteFriends.IdSetAsync(db, accountId, cancellationToken);
        foreach (var stay in stays)
        {
            stay.IsFriend = friends.Contains(stay.UserId);
            stay.IsFavorite = stay.IsFriend && favorites.Contains(stay.UserId);
        }

        return new PreviousInstancePlayersResponse
        {
            Players = stays.GroupBy(s => s.UserId).Select(g => new PreviousInstancePlayerResponse
            {
                UserId = g.Key,
                DisplayName = g.First().DisplayName,
                FirstJoinedAt = g.Min(s => s.JoinedAt),
                TimeMs = g.Sum(s => StayMs(s.JoinedAt, s.LeftAt)),
                Count = g.Count(),
                IsFriend = g.First().IsFriend,
                IsFavorite = g.First().IsFavorite,
            }).OrderByDescending(p => p.TimeMs).ToList(),
            Stays = stays,
        };
    }

    private static readonly TimeSpan SessionGap = TimeSpan.FromHours(1);

    /// <summary>Runs of consecutive stays in one location, each starting within an hour of the last event of the run.</summary>
    private static List<List<T>> SplitSessions<T>(List<T> ordered, Func<T, string> location, Func<T, DateTimeOffset> joined, Func<T, DateTimeOffset?> left)
    {
        var runs = new List<List<T>>();
        List<T>? current = null;
        var last = DateTimeOffset.MinValue;
        foreach (var stay in ordered)
        {
            if (current is null || location(current[0]) != location(stay) || joined(stay) - last > SessionGap)
            {
                current = [];
                runs.Add(current);
                last = joined(stay);
            }
            current.Add(stay);
            var end = left(stay) ?? joined(stay);
            if (end > last) last = end;
        }
        return runs;
    }

    /// <summary>A stay's length in milliseconds; 0 while it is still open.</summary>
    public static long StayMs(DateTimeOffset joined, DateTimeOffset? left) => left is { } end ? (long)(end - joined).TotalMilliseconds : 0;
}
