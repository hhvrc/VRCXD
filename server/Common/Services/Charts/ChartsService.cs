using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services.Charts;

/// <summary>One stay in an instance, as the Instance Activity chart draws it.</summary>
public sealed class ActivityVisit
{
    public required long Id { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string Location { get; init; }
    public required string WorldName { get; init; }
    public required DateTimeOffset JoinedAt { get; init; }
    public required DateTimeOffset LeftAt { get; init; }
    /// <summary>Milliseconds between joining and leaving.</summary>
    public required long Time { get; init; }
    /// <summary>Null for the account itself.</summary>
    public bool? IsFriend { get; init; }
    public bool? IsFavorite { get; init; }
}

public sealed class InstanceActivity
{
    /// <summary>The account's own stays in the range, in join order.</summary>
    public required IReadOnlyList<ActivityVisit> CurrentUserData { get; init; }
    /// <summary>Everyone's stays in the instances the account was in, grouped by instance location.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<ActivityVisit>> DetailData { get; init; }
}

public sealed class HotWorld
{
    public required string WorldId { get; init; }
    public required string WorldName { get; init; }
    public required int VisitCount { get; init; }
    public required int UniqueFriends { get; init; }
    public required DateTimeOffset LastVisited { get; init; }
    /// <summary>rising, cooling or stable: unique friends in the second half of the period against the first.</summary>
    public required string Trend { get; init; }
}

public sealed class HotWorldFriend
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required int VisitCount { get; init; }
    public required DateTimeOffset LastVisit { get; init; }
}

public interface IChartsService
{
    /// <summary>When the account's game-log stays ended: the dates the activity chart can show.</summary>
    Task<IReadOnlyList<DateTimeOffset>> GetActivityDatesAsync(string accountId, CancellationToken ct);
    Task<InstanceActivity> GetInstanceActivityAsync(string accountId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<IReadOnlyList<HotWorld>> GetHotWorldsAsync(string accountId, int days, int limit, CancellationToken ct);
    Task<IReadOnlyList<HotWorldFriend>> GetHotWorldFriendsAsync(string accountId, string worldId, int days, CancellationToken ct);
}

/// <summary>
/// The queries behind the Charts pages. Instance activity comes from the game log's instance
/// visits (the original's gamelog_join_leave); hot worlds from friends' moves between
/// instances (the original's feed_gps).
/// </summary>
public sealed class ChartsService(IDbContextFactory<VrcxContext> dbFactory) : IChartsService
{
    public async Task<IReadOnlyList<DateTimeOffset>> GetActivityDatesAsync(string accountId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.InstanceVisits.AsNoTracking()
            .Where(v => v.UserId == accountId && v.Source == VisitSource.GameLog && v.LeftAt != null)
            .Select(v => v.LeftAt!.Value)
            .ToListAsync(ct);
    }

    public async Task<InstanceActivity> GetInstanceActivityAsync(string accountId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Like the original: completed stays that start or end inside the range.
        var own = await db.InstanceVisits.AsNoTracking()
            .Where(v => v.UserId == accountId && v.Source == VisitSource.GameLog && v.LeftAt != null)
            .Where(v => (v.JoinedAt >= from && v.JoinedAt <= to) || (v.LeftAt >= from && v.LeftAt <= to))
            .OrderBy(v => v.JoinedAt)
            .Select(v => new { v.Id, v.UserId, v.User.DisplayName, v.InstanceLocation, WorldName = v.Instance.World.Name, v.JoinedAt, LeftAt = v.LeftAt!.Value })
            .ToListAsync(ct);
        var locations = own.Select(v => v.InstanceLocation).Distinct().ToList();

        var detail = locations.Count == 0 ? [] : await db.InstanceVisits.AsNoTracking()
            .Where(v => v.Source == VisitSource.GameLog && v.LeftAt != null && locations.Contains(v.InstanceLocation))
            .Where(v => (v.JoinedAt >= from && v.JoinedAt <= to) || (v.LeftAt >= from && v.LeftAt <= to))
            .OrderBy(v => v.JoinedAt)
            .Select(v => new { v.Id, v.UserId, v.User.DisplayName, v.InstanceLocation, WorldName = v.Instance.World.Name, v.JoinedAt, LeftAt = v.LeftAt!.Value })
            .ToListAsync(ct);

        var friendIds = (await db.Friendships.AsNoTracking().Where(f => f.OwnerUserId == accountId).Select(f => f.FriendUserId).ToListAsync(ct)).ToHashSet();
        var favoriteIds = await FavoriteFriends.IdSetAsync(db, accountId, ct);

        ActivityVisit Map(long id, string userId, string displayName, string location, string worldName, DateTimeOffset joined, DateTimeOffset left) => new()
        {
            Id = id, UserId = userId, DisplayName = displayName, Location = location, WorldName = worldName,
            JoinedAt = joined, LeftAt = left, Time = Math.Max(0, (long)(left - joined).TotalMilliseconds),
            IsFriend = userId == accountId ? null : friendIds.Contains(userId) || favoriteIds.Contains(userId),
            IsFavorite = userId == accountId ? null : favoriteIds.Contains(userId),
        };

        return new InstanceActivity
        {
            CurrentUserData = own.Select(v => Map(v.Id, v.UserId, v.DisplayName, v.InstanceLocation, v.WorldName, v.JoinedAt, v.LeftAt)).ToList(),
            DetailData = detail.GroupBy(v => v.InstanceLocation).ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ActivityVisit>)g.Select(v => Map(v.Id, v.UserId, v.DisplayName, v.InstanceLocation, v.WorldName, v.JoinedAt, v.LeftAt)).ToList()),
        };
    }

    public async Task<IReadOnlyList<HotWorld>> GetHotWorldsAsync(string accountId, int days, int limit, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var since = now.AddDays(-days);
        var half = now.AddDays(-Math.Floor(days / 2.0));
        var friendIds = db.Friendships.Where(f => f.OwnerUserId == accountId).Select(f => f.FriendUserId);
        var moves = db.PresenceEvents.AsNoTracking()
            .Where(e => e.Kind == PresenceKind.Location && e.InstanceLocation != null && e.At >= since && friendIds.Contains(e.UserId));

        var top = await moves
            .GroupBy(e => new { e.Instance!.WorldId, WorldName = e.Instance.World.Name })
            .Select(g => new
            {
                g.Key.WorldId, g.Key.WorldName,
                VisitCount = g.Count(),
                UniqueFriends = g.Select(e => e.UserId).Distinct().Count(),
                LastVisited = g.Max(e => e.At),
            })
            .OrderByDescending(x => x.UniqueFriends).ThenByDescending(x => x.VisitCount)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);
        if (top.Count == 0) return [];

        var worldIds = top.Select(t => t.WorldId).ToList();
        var older = await moves.Where(e => e.At < half && worldIds.Contains(e.Instance!.WorldId))
            .GroupBy(e => e.Instance!.WorldId).Select(g => new { WorldId = g.Key, Unique = g.Select(e => e.UserId).Distinct().Count() })
            .ToDictionaryAsync(x => x.WorldId, x => x.Unique, ct);
        var recent = await moves.Where(e => e.At >= half && worldIds.Contains(e.Instance!.WorldId))
            .GroupBy(e => e.Instance!.WorldId).Select(g => new { WorldId = g.Key, Unique = g.Select(e => e.UserId).Distinct().Count() })
            .ToDictionaryAsync(x => x.WorldId, x => x.Unique, ct);

        return top.Select(t =>
        {
            var o = older.GetValueOrDefault(t.WorldId);
            var r = recent.GetValueOrDefault(t.WorldId);
            return new HotWorld
            {
                WorldId = t.WorldId, WorldName = t.WorldName, VisitCount = t.VisitCount, UniqueFriends = t.UniqueFriends, LastVisited = t.LastVisited,
                Trend = r > o ? "rising" : r < o ? "cooling" : "stable",
            };
        }).ToList();
    }

    public async Task<IReadOnlyList<HotWorldFriend>> GetHotWorldFriendsAsync(string accountId, string worldId, int days, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var since = DateTimeOffset.UtcNow.AddDays(-days);
        var friendIds = db.Friendships.Where(f => f.OwnerUserId == accountId).Select(f => f.FriendUserId);
        return await db.PresenceEvents.AsNoTracking()
            .Where(e => e.Kind == PresenceKind.Location && e.Instance != null && e.Instance.WorldId == worldId && e.At >= since && friendIds.Contains(e.UserId))
            .GroupBy(e => new { e.UserId, e.User.DisplayName })
            .Select(g => new HotWorldFriend { UserId = g.Key.UserId, DisplayName = g.Key.DisplayName, VisitCount = g.Count(), LastVisit = g.Max(e => e.At) })
            .OrderByDescending(f => f.VisitCount)
            .ToListAsync(ct);
    }
}
