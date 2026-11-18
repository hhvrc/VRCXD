using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using VRCX.Common.Live;
using VRCX.Common.Live.Topics;
using VRCX.Common.Services.Presence;
using VRCX.Common.Services.Queries;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Friends;

/// <summary>
/// The "friends" topic: an account's friend list as GET friends returns it (the rows come from the
/// same <see cref="FriendQueries.ListFriendsAsync"/>), streamed as whole-row upserts and removals.
/// <para>
/// Every account with subscribers has one <see cref="Feed"/>: the rows last sent, shared by all of
/// that account's subscriptions. <see cref="FriendWriter"/> says which users or friendships changed;
/// the feed waits <see cref="CoalesceWindow"/>, reads just those rows again, and sends what differs
/// from what it sent before. So a burst for one friend is one delta, a change nobody can see (a
/// refetch with the same values) is none, and a new subscriber's snapshot is the feed's rows, which
/// the deltas after it are the change from. The original VRCX's friend store applies each pipeline
/// event to its friend refs; here the server does that once and every client gets the result.
/// </para>
/// </summary>
public sealed class FriendsTopic(IDbContextFactory<VrcxContext> dbFactory, IPresenceCache presence, IPendingPresenceQueue pending, ILogger<FriendsTopic> logger) : ILiveTopic
{
    public const string TopicName = "friends";

    /// <summary>How long changes for an account are gathered before they are sent as one delta.</summary>
    /// <remarks>Settable so tests can widen it to prove coalescing.</remarks>
    public TimeSpan CoalesceWindow { get; set; } = DefaultCoalesceWindow;

    public static readonly TimeSpan DefaultCoalesceWindow = TimeSpan.FromMilliseconds(250);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Feed> _feeds = new(StringComparer.Ordinal);

    public string Name => TopicName;
    public bool AccountScoped => true;

    /// <summary>Accounts with a feed (someone subscribed), for tests.</summary>
    public int FeedCount
    {
        get { lock (_gate) return _feeds.Count; }
    }

    public async Task<IDisposable> SubscribeAsync(string accountId, ITopicSink sink, CancellationToken ct)
    {
        Feed feed;
        lock (_gate)
        {
            if (!_feeds.TryGetValue(accountId, out feed!)) _feeds[accountId] = feed = new Feed(accountId);
            // Kept while this joins, so changes committed meanwhile are marked on it.
            feed.Joining++;
        }
        try
        {
            await feed.Gate.WaitAsync(ct);
            try
            {
                if (feed.Broken) throw new InvalidOperationException("The friend feed failed while subscribing");
                if (feed.Rows is null)
                {
                    var rows = await ReadAsync(accountId, null, ct);
                    feed.Rows = rows.ToDictionary(r => r.UserId, StringComparer.Ordinal);
                    lock (_gate) feed.FriendIds = feed.Rows.Keys.ToHashSet(StringComparer.Ordinal);
                }
                // Under the gate, and the sink joins only now: no delta can reach it before its
                // snapshot, and every delta after it is the change from these rows.
                var snapshot = new FriendsSnapshot();
                snapshot.Friends.AddRange(feed.Rows.Values.OrderBy(r => r.FriendNumber).Select(r => r.Clone()));
                sink.Snapshot(new TopicSnapshot { Friends = snapshot });
                lock (_gate) feed.Sinks.Add(sink);
            }
            finally { feed.Gate.Release(); }
        }
        finally
        {
            lock (_gate)
            {
                feed.Joining--;
                RemoveIfUnused(feed);
            }
        }
        return new Handle(this, feed, sink);
    }

    /// <summary>
    /// Users whose profile, presence, pending offline or memo changed (committed): every account
    /// that has them as a friend sends their row again if it differs.
    /// </summary>
    internal void UsersChanged(IReadOnlyCollection<string> userIds)
    {
        if (userIds.Count == 0) return;
        List<(Feed, List<string>?)> marks = [];
        lock (_gate)
        {
            foreach (var feed in _feeds.Values)
            {
                // Still reading its first rows: whatever changed may be in them or not, so all are read again after.
                if (feed.FriendIds is null) marks.Add((feed, null));
                else if (userIds.Where(feed.FriendIds.Contains).ToList() is { Count: > 0 } ids) marks.Add((feed, ids));
            }
        }
        foreach (var (feed, ids) in marks) Mark(feed, ids);
    }

    /// <summary>The account's friendship with these users opened or ended (committed).</summary>
    internal void FriendshipsChanged(string accountId, IReadOnlyCollection<string> userIds)
    {
        if (userIds.Count == 0) return;
        Feed? feed;
        lock (_gate) feed = _feeds.GetValueOrDefault(accountId);
        if (feed is not null) Mark(feed, feed.FriendIds is null ? null : userIds.ToList());
    }

    /// <summary>Adds to what the feed's next delta reads again (everything when <paramref name="userIds"/> is null), starting its window if none runs.</summary>
    private void Mark(Feed feed, List<string>? userIds)
    {
        lock (feed.Marks)
        {
            if (userIds is null) feed.All = true;
            else feed.Dirty.UnionWith(userIds);
            if (feed.Scheduled) return;
            feed.Scheduled = true;
        }
        _ = Task.Run(async () =>
        {
            await Task.Delay(CoalesceWindow);
            await FlushAsync(feed);
        });
    }

    private async Task FlushAsync(Feed feed)
    {
        await feed.Gate.WaitAsync();
        try
        {
            HashSet<string> dirty;
            bool all;
            lock (feed.Marks)
            {
                (dirty, all) = (feed.Dirty, feed.All);
                feed.Dirty = new HashSet<string>(StringComparer.Ordinal);
                feed.All = false;
                feed.Scheduled = false;
            }
            // Not read yet: the subscriber reading it sees these changes already.
            if (feed.Rows is null) return;

            var fresh = (await ReadAsync(feed.AccountId, all ? null : dirty, CancellationToken.None)).ToDictionary(r => r.UserId, StringComparer.Ordinal);
            var delta = new FriendsDelta();
            foreach (var row in fresh.Values.OrderBy(r => r.FriendNumber))
            {
                if (feed.Rows.TryGetValue(row.UserId, out var before) && before.Equals(row)) continue;
                feed.Rows[row.UserId] = row;
                delta.Upserts.Add(row.Clone());
            }
            foreach (var userId in all ? feed.Rows.Keys.ToList() : dirty.ToList())
            {
                if (fresh.ContainsKey(userId) || !feed.Rows.Remove(userId)) continue;
                delta.RemovedUserIds.Add(userId);
            }
            if (delta.Upserts.Count == 0 && delta.RemovedUserIds.Count == 0) return;
            List<ITopicSink> sinks;
            lock (_gate)
            {
                feed.FriendIds = feed.Rows.Keys.ToHashSet(StringComparer.Ordinal);
                sinks = [.. feed.Sinks];
            }
            foreach (var sink in sinks) sink.Delta(new TopicDelta { Friends = delta.Clone() });
        }
        catch (Exception ex)
        {
            // The rows sent so far may no longer be what the clients hold after this: start everyone over.
            logger.LogError(ex, "Reading friend changes for {Account} failed", feed.AccountId);
            List<ITopicSink> sinks;
            lock (_gate)
            {
                sinks = [.. feed.Sinks];
                feed.Sinks.Clear();
                feed.Broken = true;
                if (_feeds.GetValueOrDefault(feed.AccountId) == feed) _feeds.Remove(feed.AccountId);
            }
            foreach (var sink in sinks) sink.Close(TopicCloseReason.Error, "reading friends failed");
        }
        finally { feed.Gate.Release(); }
    }

    private async Task<List<FriendEntry>> ReadAsync(string accountId, IReadOnlyCollection<string>? userIds, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await new FriendQueries(db, presence, pending).ListFriendsAsync(accountId, ct, userIds);
        return rows.Select(Entry).ToList();
    }

    private void Unsubscribe(Feed feed, ITopicSink sink)
    {
        lock (_gate)
        {
            feed.Sinks.Remove(sink);
            RemoveIfUnused(feed);
        }
    }

    /// <summary>Under the lock. The last subscriber gone: nobody needs the rows kept current; the next one reads them again.</summary>
    private void RemoveIfUnused(Feed feed)
    {
        if (feed.Sinks.Count == 0 && feed.Joining == 0 && _feeds.GetValueOrDefault(feed.AccountId) == feed) _feeds.Remove(feed.AccountId);
    }

    /// <summary>The wire shape of one GET friends row.</summary>
    public static FriendEntry Entry(FriendResponse f)
    {
        var entry = new FriendEntry
        {
            UserId = f.UserId,
            FriendNumber = f.FriendNumber,
            FriendsSince = Timestamp.FromDateTimeOffset(f.FriendsSince),
            DisplayName = f.DisplayName,
            TrustLevel = f.TrustLevel,
            State = f.State.ToString().ToLowerInvariant(),
            Status = f.Status,
            StatusDescription = f.StatusDescription ?? "",
            Location = f.Location ?? "",
            TravelingToLocation = f.TravelingToLocation ?? "",
            ThumbnailImageUrl = f.ThumbnailImageUrl ?? "",
            DeveloperType = f.DeveloperType,
            Platform = f.Platform ?? "",
            Nickname = f.Nickname ?? "",
            PendingOffline = f.PendingOffline,
            NameHue = f.NameHue,
            IconFrame = f.IconFrame ?? "",
            ProfileEffect = f.ProfileEffect ?? "",
        };
        entry.Tags.AddRange(f.Tags);
        if (f.LocationSince is { } locationSince) entry.LocationSince = Timestamp.FromDateTimeOffset(locationSince);
        if (f.OnlineFor is { } onlineFor) entry.OnlineFor = Timestamp.FromDateTimeOffset(onlineFor);
        if (f.LastActivity is { } lastActivity) entry.LastActivity = Timestamp.FromDateTimeOffset(lastActivity);
        if (f.LastLogin is { } lastLogin) entry.LastLogin = Timestamp.FromDateTimeOffset(lastLogin);
        return entry;
    }

    /// <summary>One account's rows as last sent, and its subscribers.</summary>
    private sealed class Feed(string accountId)
    {
        public string AccountId { get; } = accountId;
        /// <summary>Serializes reading the first rows, snapshots and deltas.</summary>
        public SemaphoreSlim Gate { get; } = new(1, 1);
        /// <summary>Under Gate. Null until first read.</summary>
        public Dictionary<string, FriendEntry>? Rows { get; set; }
        /// <summary>Under the topic's lock: who is in Rows, for matching user changes. Null until first read.</summary>
        public HashSet<string>? FriendIds { get; set; }
        /// <summary>Under the topic's lock.</summary>
        public List<ITopicSink> Sinks { get; } = [];
        /// <summary>Under the topic's lock: subscriptions waiting for their snapshot.</summary>
        public int Joining { get; set; }
        /// <summary>Under Gate: reading changes failed and its subscribers were closed.</summary>
        public bool Broken { get; set; }
        /// <summary>The lock for Dirty, All and Scheduled.</summary>
        public Lock Marks { get; } = new();
        public HashSet<string> Dirty { get; set; } = new(StringComparer.Ordinal);
        public bool All { get; set; }
        public bool Scheduled { get; set; }
    }

    private sealed class Handle(FriendsTopic topic, Feed feed, ITopicSink sink) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) topic.Unsubscribe(feed, sink);
        }
    }
}
