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

    private async Task RefreshOneAsync(string accountId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, at) in _failed.ToArray())
            if (now - at > Retry) _failed.TryRemove(id, out _);

        var staleBefore = now - StaleAfter;
        var skip = _failed.Keys.ToArray();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
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
}
