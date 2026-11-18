using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services;
using VRCX.Common.Services.Presence;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.GameLog;

/// <summary>One stay of a user in an instance, with what the instance was.</summary>
public sealed class InstanceVisitResponse
{
    public required long Id { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string InstanceLocation { get; init; }
    public required string WorldId { get; init; }
    public required string WorldName { get; init; }
    public required InstanceAccessType AccessType { get; init; }
    public required DateTimeOffset JoinedAt { get; init; }
    public DateTimeOffset? LeftAt { get; init; }
    public required VisitSource Source { get; init; }
}

/// <summary>One game log row. Which fields are set depends on Type.</summary>
public sealed class GameLogRowResponse
{
    /// <summary>Stable row key: what a delete takes.</summary>
    public required string Key { get; init; }
    public required long Id { get; init; }
    /// <summary>Location, OnPlayerJoined, OnPlayerLeft, PortalSpawn, VideoPlay, Event, External, StringLoad or ImageLoad.</summary>
    public required string Type { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Location { get; init; }
    public string? WorldId { get; init; }
    public string? WorldName { get; init; }
    public string? GroupName { get; init; }
    public string? UserId { get; init; }
    public string? DisplayName { get; init; }
    /// <summary>How long a stay lasted, for Location and OnPlayerLeft.</summary>
    public long? TimeMs { get; init; }
    public string? VideoUrl { get; init; }
    public string? VideoName { get; init; }
    public string? VideoId { get; init; }
    /// <summary>A VideoPlay's position when logged and its length, in seconds.</summary>
    public double? VideoPos { get; init; }
    public double? VideoLength { get; init; }
    public string? ResourceUrl { get; init; }
    public string? Data { get; init; }
    public string? Message { get; init; }
    /// <summary>Where a portal leads.</summary>
    public string? InstanceId { get; init; }
    public bool IsFriend { get; init; }
    public bool IsFavorite { get; init; }

    public GameLogRowResponse WithFriend(HashSet<string> friends, HashSet<string> favorites) => new()
    {
        Key = Key, Id = Id, Type = Type, CreatedAt = CreatedAt, Location = Location, WorldId = WorldId, WorldName = WorldName, GroupName = GroupName,
        UserId = UserId, DisplayName = DisplayName, TimeMs = TimeMs, VideoUrl = VideoUrl, VideoName = VideoName, VideoId = VideoId, VideoPos = VideoPos, VideoLength = VideoLength, ResourceUrl = ResourceUrl,
        Data = Data, Message = Message, InstanceId = InstanceId,
        IsFriend = UserId is not null && friends.Contains(UserId),
        IsFavorite = UserId is not null && favorites.Contains(UserId),
    };
}

public sealed class GameLogSessionsResponse
{
    public required IReadOnlyList<GameLogRowResponse> Locations { get; init; }
    public required IReadOnlyList<GameLogRowResponse> Events { get; init; }
    public required bool HasMore { get; init; }
}

/// <summary>The account's current instance and who is in it.</summary>
public sealed class PlayerListResponse
{
    public string? Location { get; init; }
    public DateTimeOffset? JoinedAt { get; init; }
    public PlayerListWorld? World { get; init; }
    public required IReadOnlyList<PlayerListPlayer> Players { get; init; }
}

public sealed class PlayerListWorld
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? AuthorId { get; init; }
    public string? AuthorName { get; init; }
    public string? ImageUrl { get; init; }
    public string? ThumbnailImageUrl { get; init; }
    public string? ReleaseStatus { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required IReadOnlyList<string> Platforms { get; init; }
    public int? Capacity { get; init; }
    public int? RecommendedCapacity { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public required bool IsHome { get; init; }
}

public sealed class PlayerListPlayer
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required DateTimeOffset JoinedAt { get; init; }
    public required string TrustLevel { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required string DeveloperType { get; init; }
    public required string Status { get; init; }
    public string? StatusDescription { get; init; }
    public string? ThumbnailImageUrl { get; init; }
    public required IReadOnlyList<string> BioLinks { get; init; }
    public string? Note { get; init; }
    public string? Memo { get; init; }
    public string? Platform { get; init; }
    public required bool IsFriend { get; init; }
    public required bool IsBlocked { get; init; }
    public required bool IsMuted { get; init; }
    public required bool IsAvatarInteractionDisabled { get; init; }
    public required bool AgeVerified { get; init; }
}

/// <summary>
/// The account's game log as stored: every kind of row in one list, the instances it was in with
/// what happened there, and the instance it is in now. Read only; shared by the Accounts endpoints
/// and the MCP tools.
/// </summary>
public sealed class GameLogQueries(VrcxContext db, IPresenceCache presence)
{
    private static readonly string[] GameLogTypes = ["Location", "OnPlayerJoined", "OnPlayerLeft", "PortalSpawn", "VideoPlay", "Event", "External", "StringLoad", "ImageLoad"];
    private static readonly string[] SessionEventTypes = ["OnPlayerJoined", "OnPlayerLeft", "VideoPlay"];

    /// <summary>The account's game log, newest first. <paramref name="types"/> is comma separated, empty for all.</summary>
    public async Task<GameLogRowResponse[]> ListAsync(string accountId, string? types, string? search, bool vip, int limit, CancellationToken cancellationToken)
    {
        // The client's Search Limit Returns setting goes up to 100000 (the original's SEARCH_LIMIT_MAX).
        limit = Math.Clamp(limit, 1, 100_000);
        // AvatarChange only when asked for: the original's game log table never lists it (its notifications do).
        var wanted = (types ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Intersect([.. GameLogTypes, "AvatarChange"]).ToHashSet();
        if (wanted.Count == 0) wanted = GameLogTypes.ToHashSet();

        var rows = await LoadRowsAsync(accountId, wanted, null, string.IsNullOrWhiteSpace(search) ? null : search.Trim(), limit, cancellationToken);
        var (friends, favorites) = await FriendSetsAsync(accountId, cancellationToken);
        var hidden = (await db.GameLogHiddenEntries.AsNoTracking().Where(h => h.OwnerUserId == accountId).Select(h => h.EntryKey).ToListAsync(cancellationToken)).ToHashSet();

        return rows
            .Where(r => !hidden.Contains(r.Key))
            .Where(r => !vip || r.Type is "Location" or "Event" or "StringLoad" or "ImageLoad" || (r.UserId is not null && favorites.Contains(r.UserId)))
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Key, StringComparer.Ordinal)
            .Take(limit)
            .Select(r => r.WithFriend(friends, favorites))
            .ToArray();
    }

    /// <summary>Instances the account was in, newest first, with who came and went and what played there.</summary>
    public async Task<GameLogSessionsResponse> SessionsAsync(string accountId, long? beforeId, DateTimeOffset? from, DateTimeOffset? to, int limit, CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 200);

        var query = db.InstanceVisits.AsNoTracking().Where(v => v.UserId == accountId && v.Source == VisitSource.GameLog);
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
        if (locations.Count == 0) return new GameLogSessionsResponse { Locations = [], Events = [], HasMore = false };

        var scope = new GameLogRange(locations.Select(l => l.Location!).Distinct().ToArray(), locations.Min(l => l.CreatedAt).AddSeconds(-1));
        var events = await LoadRowsAsync(accountId, SessionEventTypes.ToHashSet(), scope, null, 20000, cancellationToken);
        var (friends, favorites) = await FriendSetsAsync(accountId, cancellationToken);
        return new GameLogSessionsResponse
        {
            Locations = locations,
            Events = events.Select(r => r.WithFriend(friends, favorites)).ToArray(),
            HasMore = hasMore,
        };
    }

    /// <summary>The account's current instance and who is in it; empty fields when it is not in one.</summary>
    public async Task<PlayerListResponse> PlayerListAsync(string accountId, CancellationToken cancellationToken)
    {
        var stay = await db.InstanceVisits.AsNoTracking()
            .Where(v => v.UserId == accountId && v.LeftAt == null && v.Source == VisitSource.GameLog)
            .OrderByDescending(v => v.JoinedAt)
            .Select(v => new { v.InstanceLocation, v.JoinedAt })
            .FirstOrDefaultAsync(cancellationToken);
        var own = (await presence.GetManyAsync([accountId], cancellationToken)).GetValueOrDefault(accountId);
        var arrived = own?.LocationSince ?? own?.PresenceChangedAt;

        // A stay stays open when the game exits without logging the leave. VRChat saying the account
        // moved later wins over it: without this the page shows a room left hours ago.
        var stayCurrent = stay is not null && (own?.CurrentInstanceLocation is null || own.CurrentInstanceLocation == stay.InstanceLocation || arrived is null || arrived <= stay.JoinedAt);
        var location = stayCurrent ? stay!.InstanceLocation : own?.CurrentInstanceLocation;
        if (location is null) return new PlayerListResponse { Players = [] };

        var world = await db.Instances.AsNoTracking().Where(i => i.Location == location).Select(i => i.World).FirstOrDefaultAsync(cancellationToken);
        var home = await db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => a.HomeLocation).FirstOrDefaultAsync(cancellationToken);

        var visits = await db.InstanceVisits.AsNoTracking()
            .Where(v => v.InstanceLocation == location && v.LeftAt == null && (!stayCurrent || v.Source == VisitSource.GameLog))
            .OrderBy(v => v.JoinedAt)
            .Select(v => new { v.JoinedAt, v.User })
            .ToListAsync(cancellationToken);
        var players = visits.DistinctBy(v => v.User.Id).ToList();
        var ids = players.Select(p => p.User.Id).ToArray();

        var (friends, _) = await FriendSetsAsync(accountId, cancellationToken);
        var notes = (await db.UserNotes.AsNoTracking().Where(n => n.OwnerUserId == accountId && ids.Contains(n.TargetUserId))
            .Select(n => new { n.TargetUserId, n.Note }).ToListAsync(cancellationToken)).GroupBy(n => n.TargetUserId).ToDictionary(g => g.Key, g => g.First().Note);
        var memos = await db.UserMemos.AsNoTracking().Where(m => ids.Contains(m.UserId)).ToDictionaryAsync(m => m.UserId, m => m.Memo, cancellationToken);
        var moderations = (await db.PlayerModerations.AsNoTracking().Where(m => m.OwnerUserId == accountId && ids.Contains(m.TargetUserId))
            .Select(m => new { m.TargetUserId, m.Type }).ToListAsync(cancellationToken)).ToLookup(m => m.TargetUserId, m => m.Type);
        var present = await presence.GetManyAsync(ids, cancellationToken);

        return new PlayerListResponse
        {
            Location = location,
            JoinedAt = stayCurrent ? stay!.JoinedAt : arrived,
            World = world is null ? null : new PlayerListWorld
            {
                Id = world.Id, Name = world.Name, Description = world.Description, AuthorId = world.AuthorId, AuthorName = world.AuthorName,
                ImageUrl = world.ImageUrl, ThumbnailImageUrl = world.ThumbnailImageUrl, ReleaseStatus = world.ReleaseStatus, Tags = world.Tags, Platforms = world.Platforms,
                Capacity = world.Capacity, RecommendedCapacity = world.RecommendedCapacity, CreatedAt = world.CreatedAt, UpdatedAt = world.UpdatedAt,
                IsHome = home is not null && home.StartsWith(world.Id, StringComparison.Ordinal),
            },
            Players = players.Select(p => new PlayerListPlayer
            {
                UserId = p.User.Id,
                DisplayName = p.User.DisplayName,
                JoinedAt = p.JoinedAt,
                TrustLevel = p.User.TrustLevel,
                Tags = p.User.Tags,
                DeveloperType = p.User.DeveloperType,
                Status = p.User.Status,
                StatusDescription = p.User.StatusDescription,
                ThumbnailImageUrl = UserImage.Pick(p.User.IconUrl, p.User.CurrentAvatarThumbnailImageUrl, p.User.CurrentAvatarImageUrl),
                BioLinks = p.User.BioLinks,
                Note = notes.GetValueOrDefault(p.User.Id),
                Memo = memos.GetValueOrDefault(p.User.Id),
                Platform = present.GetValueOrDefault(p.User.Id)?.Platform ?? p.User.LastPlatform,
                IsFriend = friends.Contains(p.User.Id),
                IsBlocked = moderations[p.User.Id].Contains("block"),
                IsMuted = moderations[p.User.Id].Contains("mute"),
                IsAvatarInteractionDisabled = moderations[p.User.Id].Contains("interactOff"),
                AgeVerified = p.User.AgeVerificationStatus == "18+",
            }).ToArray(),
        };
    }

    private sealed record GameLogRange(string[] Locations, DateTimeOffset Since);

    private async Task<(HashSet<string> Friends, HashSet<string> Favorites)> FriendSetsAsync(string accountId, CancellationToken cancellationToken)
    {
        var friends = (await db.Friendships.AsNoTracking().Where(f => f.OwnerUserId == accountId && f.EndedAt == null).Select(f => f.FriendUserId).ToListAsync(cancellationToken)).ToHashSet();
        var favorites = await FavoriteFriends.IdSetAsync(db, accountId, cancellationToken);
        return (friends, favorites);
    }

    private async Task<List<GameLogRowResponse>> LoadRowsAsync(string accountId, HashSet<string> types, GameLogRange? range, string? search, int limit, CancellationToken ct)
    {
        var like = search is null ? null : $"%{search.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_")}%";
        // The tables hold what every PC reported: only instances this account was in are its own log.
        var mine = db.InstanceVisits.AsNoTracking().Where(v => v.UserId == accountId && v.Source == VisitSource.GameLog).Select(v => v.InstanceLocation).Distinct();
        var rows = new List<GameLogRowResponse>();

        if (types.Contains("Location"))
        {
            var q = db.InstanceVisits.AsNoTracking().Where(v => v.UserId == accountId && v.Source == VisitSource.GameLog);
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
            var q = db.InstanceVisits.AsNoTracking().Where(v => v.UserId != accountId && v.Source == VisitSource.GameLog && mine.Contains(v.InstanceLocation));
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
            var q = db.VideoPlays.AsNoTracking().Where(v => v.InstanceLocation != null && mine.Contains(v.InstanceLocation));
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
            var q = db.PortalSpawns.AsNoTracking().Where(p => p.InstanceLocation != null && mine.Contains(p.InstanceLocation));
            if (like is not null) q = q.Where(p => (p.DisplayName != null && EF.Functions.ILike(p.DisplayName, like)) || (p.TargetWorldName != null && EF.Functions.ILike(p.TargetWorldName, like)));
            rows.AddRange(await q.OrderByDescending(p => p.At).Take(limit).Select(p => new GameLogRowResponse
            {
                Key = $"PortalSpawn:{p.Id}", Id = p.Id, Type = "PortalSpawn", CreatedAt = p.At, Location = p.InstanceLocation, UserId = p.UserId, DisplayName = p.DisplayName,
                InstanceId = p.TargetInstanceLocation, WorldName = p.TargetWorldName,
            }).ToListAsync(ct));
        }

        if (types.Contains("Event") || types.Contains("External") || types.Contains("AvatarChange"))
        {
            var q = db.GameLogEvents.AsNoTracking().Where(e => types.Contains(e.Kind) && (e.InstanceLocation == null || mine.Contains(e.InstanceLocation)));
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
            var q = db.ResourceLoads.AsNoTracking().Where(r => kinds.Contains(r.Kind) && r.InstanceLocation != null && mine.Contains(r.InstanceLocation));
            if (like is not null) q = q.Where(r => EF.Functions.ILike(r.Url, like));
            rows.AddRange(await q.OrderByDescending(r => r.At).Take(limit).Select(r => new GameLogRowResponse
            {
                Key = $"{(r.Kind == "image" ? "ImageLoad" : "StringLoad")}:{r.Id}", Id = r.Id, Type = r.Kind == "image" ? "ImageLoad" : "StringLoad", CreatedAt = r.At, Location = r.InstanceLocation, ResourceUrl = r.Url,
            }).ToListAsync(ct));
        }

        return rows;
    }

    /// <summary>The account's own instance visits (its game log), newest first, for the caller to page.</summary>
    public IQueryable<InstanceVisitResponse> Visits(string accountId) =>
        db.InstanceVisits.AsNoTracking()
            .Where(v => v.UserId == accountId)
            .OrderByDescending(v => v.JoinedAt).ThenByDescending(v => v.Id)
            .Select(v => new InstanceVisitResponse
            {
                Id = v.Id, UserId = v.UserId, DisplayName = v.User.DisplayName, InstanceLocation = v.InstanceLocation,
                WorldId = v.Instance.WorldId, WorldName = v.Instance.World.Name, AccessType = v.Instance.Type,
                JoinedAt = v.JoinedAt, LeftAt = v.LeftAt, Source = v.Source,
            });
}
