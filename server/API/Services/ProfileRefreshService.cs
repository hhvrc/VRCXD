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
/// friend at a time is fetched through their friend's own session, never-fetched first.
/// Players in the instance the account's game is in come before friends: the original fetched
/// everyone it saw join (getUser and getPublicProfile, upstream db7cb63b), so the Player List
/// has their rank, status and bio links; here one never-fetched player is fetched per tick.
/// </summary>
public sealed class ProfileRefreshService(
    IDbContextFactory<VrcxContext> dbFactory,
    IAccountSessionManager sessions,
    IAccountSyncService sync,
    ILogger<ProfileRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);
    private static readonly TimeSpan Retry = TimeSpan.FromHours(1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _failed = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            foreach (var accountId in sessions.ActiveAccountIds.ToArray())
            {
                try
                {
                    await RefreshOneAsync(accountId, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Refreshing a friend's profile through {Account} failed", accountId);
                }
            }
        }
    }

    /// <summary>One tick for one account: a never-fetched player in its instance, else the stalest friend.</summary>
    public async Task RefreshOneAsync(string accountId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, at) in _failed.ToArray())
            if (now - at > Retry) _failed.TryRemove(id, out _);

        var staleBefore = now - StaleAfter;
        var skip = _failed.Keys.ToArray();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var playerId = await NextPlayerAsync(db, accountId, skip, ct);
        if (playerId is not null)
        {
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
            return;
        }

        var userId = await db.Friendships.AsNoTracking()
            .Where(f => f.OwnerUserId == accountId && f.EndedAt == null)
            .Where(f => f.Friend.ProfileFetchedAt == null || f.Friend.ProfileFetchedAt < staleBefore)
            .Where(f => !skip.Contains(f.FriendUserId))
            .OrderBy(f => f.Friend.ProfileFetchedAt != null)
            .ThenBy(f => f.Friend.ProfileFetchedAt)
            .Select(f => f.FriendUserId)
            .FirstOrDefaultAsync(ct);
        if (userId is null) return;

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
