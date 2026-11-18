using Google.Protobuf;

namespace VRCX.Common.Live.Topics;

/// <summary>
/// A topic whose state is a set of rows per account, each with a key, streamed as whole-row upserts
/// and removals: what a list endpoint returns, kept current.
/// <para>
/// Every account with subscribers has one feed: the rows last sent, shared by all of that account's
/// subscriptions. The feature's writer says which keys changed (<see cref="Changed"/>); the feed
/// waits <see cref="CoalesceWindow"/>, reads just those rows again and sends what differs from what
/// it sent before. So a burst for one row is one delta, a change nobody can see is none, and a new
/// subscriber's snapshot is the feed's rows, which the deltas after it are the change from.
/// </para>
/// <para>
/// The friends topic (Features/Friends/FriendsTopic) was written before this and does the same by
/// hand, with one more thing: a change to a user reaches every account that has them as a friend.
/// </para>
/// </summary>
public abstract class AccountRowsTopic<TRow>(ILogger logger) : ILiveTopic where TRow : class, IMessage<TRow>
{
    public static readonly TimeSpan DefaultCoalesceWindow = TimeSpan.FromMilliseconds(250);

    /// <summary>How long changes for an account are gathered before they are sent as one delta.</summary>
    /// <remarks>Settable so tests can widen it to prove coalescing.</remarks>
    public TimeSpan CoalesceWindow { get; set; } = DefaultCoalesceWindow;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Feed> _feeds = new(StringComparer.Ordinal);

    public abstract string Name { get; }
    public bool AccountScoped => true;

    /// <summary>Accounts with a feed (someone subscribed), for tests.</summary>
    public int FeedCount
    {
        get { lock (_gate) return _feeds.Count; }
    }

    /// <summary>What identifies a row within its account.</summary>
    protected abstract string KeyOf(TRow row);

    /// <summary>
    /// The account's rows: all of the topic's state when <paramref name="keys"/> is null, else just
    /// those of them that still belong to it. A key that is asked for and not returned is removed.
    /// </summary>
    protected abstract Task<List<TRow>> ReadAsync(string accountId, IReadOnlyCollection<string>? keys, CancellationToken ct);

    /// <summary>The snapshot for these rows, in the topic's order.</summary>
    protected abstract TopicSnapshot ToSnapshot(IReadOnlyCollection<TRow> rows);

    /// <summary>The delta for these changes; the upserts in the topic's order.</summary>
    protected abstract TopicDelta ToDelta(IReadOnlyCollection<TRow> upserts, IReadOnlyCollection<string> removedKeys);

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
                if (feed.Broken) throw new InvalidOperationException($"The {Name} feed failed while subscribing");
                feed.Rows ??= (await ReadAsync(accountId, null, ct)).ToDictionary(KeyOf, StringComparer.Ordinal);
                // Under the gate, and the sink joins only now: no delta can reach it before its
                // snapshot, and every delta after it is the change from these rows.
                sink.Snapshot(ToSnapshot(feed.Rows.Values.Select(r => r.Clone()).ToList()));
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
    /// These rows of the account changed (committed); null when which ones is not known, and then
    /// all of them are read again. Nothing happens for an account nobody is subscribed to.
    /// </summary>
    public void Changed(string accountId, IReadOnlyCollection<string>? keys)
    {
        if (keys is { Count: 0 }) return;
        Feed? feed;
        lock (_gate) feed = _feeds.GetValueOrDefault(accountId);
        if (feed is null) return;
        lock (feed.Marks)
        {
            if (keys is null) feed.All = true;
            else feed.Dirty.UnionWith(keys);
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

            var fresh = (await ReadAsync(feed.AccountId, all ? null : dirty, CancellationToken.None)).ToDictionary(KeyOf, StringComparer.Ordinal);
            List<TRow> upserts = [];
            foreach (var (key, row) in fresh)
            {
                if (feed.Rows.TryGetValue(key, out var before) && before.Equals(row)) continue;
                feed.Rows[key] = row;
                upserts.Add(row.Clone());
            }
            List<string> removed = [];
            foreach (var key in all ? feed.Rows.Keys.ToList() : dirty.ToList())
                if (!fresh.ContainsKey(key) && feed.Rows.Remove(key)) removed.Add(key);
            if (upserts.Count == 0 && removed.Count == 0) return;

            List<ITopicSink> sinks;
            lock (_gate) sinks = [.. feed.Sinks];
            var delta = ToDelta(upserts, removed);
            foreach (var sink in sinks) sink.Delta(delta.Clone());
        }
        catch (Exception ex)
        {
            // The rows sent so far may no longer be what the clients hold after this: start everyone over.
            logger.LogError(ex, "Reading {Topic} changes for {Account} failed", Name, feed.AccountId);
            List<ITopicSink> sinks;
            lock (_gate)
            {
                sinks = [.. feed.Sinks];
                feed.Sinks.Clear();
                feed.Broken = true;
                if (_feeds.GetValueOrDefault(feed.AccountId) == feed) _feeds.Remove(feed.AccountId);
            }
            foreach (var sink in sinks) sink.Close(TopicCloseReason.Error, $"reading {Name} failed");
        }
        finally { feed.Gate.Release(); }
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

    /// <summary>One account's rows as last sent, and its subscribers.</summary>
    private sealed class Feed(string accountId)
    {
        public string AccountId { get; } = accountId;
        /// <summary>Serializes reading the first rows, snapshots and deltas.</summary>
        public SemaphoreSlim Gate { get; } = new(1, 1);
        /// <summary>Under Gate. Null until first read.</summary>
        public Dictionary<string, TRow>? Rows { get; set; }
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

    private sealed class Handle(AccountRowsTopic<TRow> topic, Feed feed, ITopicSink sink) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) topic.Unsubscribe(feed, sink);
        }
    }
}
