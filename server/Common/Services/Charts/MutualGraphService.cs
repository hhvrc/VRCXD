using System.Collections.Concurrent;
using System.Net;
using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services.Sessions;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.Common.Services.Charts;

public sealed class MutualGraphMetaEntry
{
    public required DateTimeOffset LastFetchedAt { get; init; }
    public required bool OptedOut { get; init; }
}

public sealed class MutualGraphFetchStatus
{
    public required bool Running { get; init; }
    public required bool CancelRequested { get; init; }
    public required int Processed { get; init; }
    public required int Total { get; init; }
    /// <summary>Set once a full fetch has finished since the server started (the original's hasFetched).</summary>
    public required DateTimeOffset? LastCompletedAt { get; init; }
}

public sealed class MutualGraphSnapshot
{
    /// <summary>Every current friend, with the ids of the friends they share with the account (empty when never fetched).</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Friends { get; init; }
    public required IReadOnlyDictionary<string, MutualGraphMetaEntry> Meta { get; init; }
    /// <summary>Display names for every id above, so the graph can label nodes without further lookups.</summary>
    public required IReadOnlyDictionary<string, string> Names { get; init; }
    public required bool OptedOut { get; init; }
    public required MutualGraphFetchStatus Fetch { get; init; }
}

public sealed record MutualRefreshResult(bool Success, bool OptedOut);

/// <summary>
/// The mutual-friend network the original built client side: for each friend, GET
/// users/{id}/mutuals/friends, stored as links and replaced per friend on each fetch.
/// The full fetch runs in the background (one per account at a time) under the shared
/// VRChat rate limit; the page polls the status. A friend answering 403/404 has sharing
/// turned off and keeps whatever links were stored before.
/// </summary>
public sealed class MutualGraphService(IDbContextFactory<VrcxContext> dbFactory, IAccountSessionManager sessions, ILogger<MutualGraphService> logger)
{
    private const string EmptyUserId = "usr_00000000-0000-0000-0000-000000000000";

    private sealed class Job
    {
        public CancellationTokenSource Cancel { get; } = new();
        public int Processed;
        public int Total;
        public bool CancelRequested;
        public Task Task { get; set; } = Task.CompletedTask;
    }

    private readonly ConcurrentDictionary<string, Job> _jobs = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _completed = new();

    public async Task<MutualGraphSnapshot> GetSnapshotAsync(string accountId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var friendIds = await db.Friendships.AsNoTracking().Where(f => f.OwnerUserId == accountId && f.EndedAt == null).Select(f => f.FriendUserId).Distinct().ToListAsync(ct);
        var links = await db.MutualFriendLinks.AsNoTracking().Where(l => l.OwnerUserId == accountId)
            .Select(l => new { l.FriendUserId, l.MutualUserId }).ToListAsync(ct);
        var meta = await db.MutualGraphMetas.AsNoTracking().Where(m => m.OwnerUserId == accountId).ToListAsync(ct);

        var friends = friendIds.ToDictionary(id => id, _ => (IReadOnlyList<string>)[]);
        foreach (var g in links.GroupBy(l => l.FriendUserId))
            friends[g.Key] = g.Select(l => l.MutualUserId).Where(id => id != EmptyUserId).Distinct().ToList();

        var ids = friends.Keys.Concat(friends.Values.SelectMany(v => v)).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var optedOut = await db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => a.SharedConnectionsOptOut).FirstOrDefaultAsync(ct);

        return new MutualGraphSnapshot
        {
            Friends = friends,
            Meta = meta.ToDictionary(m => m.FriendUserId, m => new MutualGraphMetaEntry { LastFetchedAt = m.LastFetchedAt, OptedOut = m.OptedOut }),
            Names = names,
            OptedOut = optedOut,
            Fetch = GetStatus(accountId),
        };
    }

    public MutualGraphFetchStatus GetStatus(string accountId)
    {
        _jobs.TryGetValue(accountId, out var job);
        var running = job is not null && !job.Task.IsCompleted;
        return new MutualGraphFetchStatus
        {
            Running = running,
            CancelRequested = running && job!.CancelRequested,
            Processed = job?.Processed ?? 0,
            Total = job?.Total ?? 0,
            LastCompletedAt = _completed.TryGetValue(accountId, out var at) ? at : null,
        };
    }

    /// <summary>Starts the full fetch for the account. Returns false when one is already running.</summary>
    public async Task<bool> StartFetchAsync(string accountId, CancellationToken ct)
    {
        if (_jobs.TryGetValue(accountId, out var existing) && !existing.Task.IsCompleted) return false;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var friendIds = await db.Friendships.AsNoTracking().Where(f => f.OwnerUserId == accountId && f.EndedAt == null).Select(f => f.FriendUserId).Distinct().ToListAsync(ct);
        var job = new Job { Total = friendIds.Count };
        _jobs[accountId] = job;
        job.Task = Task.Run(() => RunAsync(accountId, friendIds, job), CancellationToken.None);
        return true;
    }

    public bool CancelFetch(string accountId)
    {
        if (!_jobs.TryGetValue(accountId, out var job) || job.Task.IsCompleted) return false;
        job.CancelRequested = true;
        job.Cancel.Cancel();
        return true;
    }

    private async Task RunAsync(string accountId, List<string> friendIds, Job job)
    {
        var ct = job.Cancel.Token;
        try
        {
            for (var i = 0; i < friendIds.Count; i++)
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    await RefreshFriendAsync(accountId, friendIds[i], ct);
                }
                catch (OperationCanceledException) { return; }
                catch (VrchatUnauthorizedException) { return; }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Mutual friends of {Friend} could not be fetched", friendIds[i]);
                }
                job.Processed = i + 1;
            }
            _completed[accountId] = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Mutual graph fetch for {Account} failed", accountId);
        }
    }

    /// <summary>Fetches one friend's mutuals and replaces their links. 403/404 marks them opted out and keeps old links.</summary>
    public async Task<MutualRefreshResult> RefreshFriendAsync(string accountId, string friendId, CancellationToken ct)
    {
        var client = sessions.Require(accountId);
        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<string> mutualIds;
        try
        {
            var mutuals = await client.GetAllMutualFriendsAsync(friendId, ct);
            mutualIds = mutuals.Select(m => m.Id).Where(id => !string.IsNullOrEmpty(id) && id != EmptyUserId).Distinct().ToList();

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            // Names for the nodes: mutuals that are not already known get stub rows.
            var known = (await db.Users.Where(u => mutualIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct)).ToHashSet();
            foreach (var m in mutuals.Where(m => !known.Contains(m.Id) && mutualIds.Contains(m.Id)).DistinctBy(m => m.Id))
            {
                db.Users.Add(new User { Id = m.Id, DisplayName = m.DisplayName, IsStub = true, FirstSeenAt = now, UpdatedAt = now });
                known.Add(m.Id);
            }
            await db.SaveChangesAsync(ct);

            await db.MutualFriendLinks.Where(l => l.OwnerUserId == accountId && l.FriendUserId == friendId).ExecuteDeleteAsync(ct);
            foreach (var id in mutualIds)
                db.MutualFriendLinks.Add(new MutualFriendLink { OwnerUserId = accountId, FriendUserId = friendId, MutualUserId = id, FetchedAt = now });
            await UpsertMetaAsync(db, accountId, friendId, now, optedOut: false, ct);
            await db.SaveChangesAsync(ct);
            return new MutualRefreshResult(true, false);
        }
        catch (VrchatApiException ex) when (ex is not VrchatUnauthorizedException && ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await UpsertMetaAsync(db, accountId, friendId, now, optedOut: true, ct);
            await db.SaveChangesAsync(ct);
            return new MutualRefreshResult(false, true);
        }
    }

    private static async Task UpsertMetaAsync(VrcxContext db, string accountId, string friendId, DateTimeOffset now, bool optedOut, CancellationToken ct)
    {
        var meta = await db.MutualGraphMetas.FindAsync([accountId, friendId], ct);
        if (meta is null)
            db.MutualGraphMetas.Add(new MutualGraphMeta { OwnerUserId = accountId, FriendUserId = friendId, LastFetchedAt = now, OptedOut = optedOut });
        else
        {
            meta.LastFetchedAt = now;
            meta.OptedOut = optedOut;
        }
    }
}
