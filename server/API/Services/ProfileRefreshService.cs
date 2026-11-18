using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services.Sessions;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.API.Services;

/// <summary>
/// Keeps friends' bios current. VRChat only serves bios from profile/{id}, one user at a time,
/// and nothing announces a change, so the original refetched a friend's profile once a week
/// whenever it saw them update; a change found that way is what feeds the Bio feed. Here one
/// friend every 15 seconds is fetched through their friend's own session, never-fetched first.
/// Each friend's week is a day longer or shorter, fixed by their id, so a friend list first read
/// on one day does not come due all on one day a week later and keep the loop busy from then on.
/// Players in the instance the account's game is in come before friends, and sooner: the original
/// fetched everyone it saw join (getUser and getPublicProfile, upstream db7cb63b), so the Player
/// List has their rank, status and bio links; here one never-fetched player every 5 seconds, so a
/// full instance is filled in within minutes rather than the better part of an hour.
/// </summary>
public sealed class ProfileRefreshService(
    IDbContextFactory<VrcxContext> dbFactory,
    IAccountSessionManager sessions,
    IAccountSyncService sync,
    ILogger<ProfileRefreshService> logger) : BackgroundService
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);
    public static readonly TimeSpan StaleJitter = TimeSpan.FromDays(1);
    /// <summary>The loop's tick, and how often a player in the account's instance may be fetched.</summary>
    public static readonly TimeSpan PlayerInterval = TimeSpan.FromSeconds(5);
    /// <summary>How often a friend's profile may be fetched, per account.</summary>
    public static readonly TimeSpan FriendInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Retry = TimeSpan.FromHours(1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _failed = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastFriendAt = new(StringComparer.Ordinal);

    /// <summary>
    /// How old this user's profile may get: <see cref="StaleAfter"/> give or take up to
    /// <see cref="StaleJitter"/>, the same for one id every time (FNV-1a over it; string hash codes
    /// change with every process).
    /// </summary>
    public static TimeSpan StaleAfterFor(string userId)
    {
        var hash = 2166136261u;
        foreach (var c in userId)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        var fraction = hash / (double)uint.MaxValue;
        return StaleAfter - StaleJitter + StaleJitter * 2 * fraction;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PlayerInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            foreach (var accountId in sessions.ActiveAccountIds.ToArray())
            {
                try
                {
                    // At most one request per account per tick: a player when one is waiting, else a
                    // friend when the last friend was a while ago.
                    if (await RefreshPlayerAsync(accountId, stoppingToken) is not null) continue;
                    var now = DateTimeOffset.UtcNow;
                    if (_lastFriendAt.TryGetValue(accountId, out var last) && now - last < FriendInterval) continue;
                    if (await RefreshFriendAsync(accountId, stoppingToken) is not null) _lastFriendAt[accountId] = now;
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Refreshing a friend's profile through {Account} failed", accountId);
                }
            }
        }
    }

    /// <summary>One tick for one account, unpaced: a never-fetched player in its instance, else the most overdue friend. Returns whose.</summary>
    public async Task<string?> RefreshOneAsync(string accountId, CancellationToken ct) =>
        await RefreshPlayerAsync(accountId, ct) ?? await RefreshFriendAsync(accountId, ct);

    private string[] Skip(DateTimeOffset now)
    {
        foreach (var (id, at) in _failed.ToArray())
            if (now - at > Retry) _failed.TryRemove(id, out _);
        return _failed.Keys.ToArray();
    }

    /// <summary>Fetches a never-fetched player in the account's instance, if there is one. Returns whose.</summary>
    public async Task<string?> RefreshPlayerAsync(string accountId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var skip = Skip(now);
        string? playerId;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            playerId = await NextPlayerAsync(db, accountId, skip, ct);
        if (playerId is null) return null;
        try
        {
            // users/{id} then profile/{id}; a user VRChat does not serve is left alone for a while.
            if (await sync.RefreshUserAsync(accountId, playerId, ct) is null) _failed[playerId] = now;
        }
        catch (VrchatUnauthorizedException)
        {
        }
        catch (VrchatApiException ex)
        {
            _failed[playerId] = now;
            logger.LogDebug(ex, "Player {User} could not be fetched", playerId);
        }
        return playerId;
    }

    /// <summary>Fetches the profile most overdue among the account's friends, if any is. Returns whose.</summary>
    public async Task<string?> RefreshFriendAsync(string accountId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var skip = Skip(now);
        // Everyone who could be due (older than the shortest week); then each by their own week.
        var mayBeStaleBefore = now - (StaleAfter - StaleJitter);
        string? userId;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var candidates = await db.Friendships.AsNoTracking()
                .Where(f => f.OwnerUserId == accountId && f.EndedAt == null)
                .Where(f => f.Friend.ProfileFetchedAt == null || f.Friend.ProfileFetchedAt < mayBeStaleBefore)
                .Where(f => !skip.Contains(f.FriendUserId))
                .Select(f => new { f.FriendUserId, f.Friend.ProfileFetchedAt })
                .ToListAsync(ct);
            userId = candidates
                .Select(c => (c.FriendUserId, DueAt: c.ProfileFetchedAt + StaleAfterFor(c.FriendUserId)))
                .Where(c => c.DueAt is null || c.DueAt < now)
                .OrderBy(c => c.DueAt is not null).ThenBy(c => c.DueAt).ThenBy(c => c.FriendUserId, StringComparer.Ordinal)
                .Select(c => c.FriendUserId)
                .FirstOrDefault();
        }
        if (userId is null) return null;

        try
        {
            await sync.RefreshProfileAsync(accountId, userId, ct);
        }
        catch (VrchatUnauthorizedException)
        {
            // The session manager notices this on the account's own sync; nothing to mark here.
        }
        catch (VrchatApiException ex)
        {
            _failed[userId] = now;
            logger.LogDebug(ex, "Profile of {User} could not be fetched", userId);
        }
        return userId;
    }

    /// <summary>Someone the account's game log saw join its current instance whose profile was never read, earliest join first.</summary>
    private static async Task<string?> NextPlayerAsync(VrcxContext db, string accountId, string[] skip, CancellationToken ct)
    {
        var location = await db.InstanceVisits.AsNoTracking()
            .Where(v => v.UserId == accountId && v.LeftAt == null && v.Source == VisitSource.GameLog)
            .OrderByDescending(v => v.JoinedAt)
            .Select(v => v.InstanceLocation)
            .FirstOrDefaultAsync(ct);
        if (location is null) return null;
        return await db.InstanceVisits.AsNoTracking()
            .Where(v => v.InstanceLocation == location && v.LeftAt == null && v.Source == VisitSource.GameLog && v.UserId != accountId)
            .Where(v => v.User.ProfileFetchedAt == null && !skip.Contains(v.UserId))
            .OrderBy(v => v.JoinedAt)
            .Select(v => v.UserId)
            .FirstOrDefaultAsync(ct);
    }
}
